using System.Buffers;
using System.Diagnostics;
using System.Text;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Text;

internal sealed unsafe class MacTextSink : ITextSink
{
    // MT-017 proved that multi-unit bursts can be truncated by Terminal and Electron targets.
    private const int ChunkPauseMs = 2;
    private const int EnterPauseMs = 25;
    private const int EventQueueSettleMs = 50;

    private readonly IntPtr _source;
    private readonly MacTargetWindow? _targets;
    private readonly object _deliveryGate = new();
    private int _disposed;

    public MacTextSink(MacTargetWindow? targets = null)
    {
        Frameworks.EnsureLoaded();
        _targets = targets;
        _source = CoreGraphics.CGEventSourceCreate(CoreGraphics.HidSystemState);
        if (_source == IntPtr.Zero)
            throw new InvalidOperationException("CGEventSourceCreate failed.");
    }

    public Task<TextDeliveryResult> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(TextDeliveryResult.Cancelled);
        if (!IsValidRequest(request))
            return Task.FromResult(TextDeliveryResult.InvalidRequest);

        try
        {
            lock (_deliveryGate)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return Task.FromResult(TextDeliveryResult.Failed);
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromResult(TextDeliveryResult.Cancelled);
                return Task.FromResult(DeliverRequest(request, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(TextDeliveryResult.Cancelled);
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao entregar texto no Mac", exception);
            return Task.FromResult(TextDeliveryResult.Failed);
        }
    }

    private bool IsValidRequest(TextDeliveryRequest request)
        => request.Method switch
        {
            TextDeliveryMethod.Unicode => request.Target == null,
            TextDeliveryMethod.TargetWithFocus or TextDeliveryMethod.TargetWithoutFocus
                => request.Target != null && _targets != null,
            _ => false,
        };

    private TextDeliveryResult DeliverRequest(
        TextDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Method == TextDeliveryMethod.Unicode)
            return DeliverUnicode(request, cancellationToken);

        if (!_targets!.TryAcquireLease(request.Target!, out MacTargetLease? target))
            return TextDeliveryResult.TargetUnavailable;

        using (target)
        {
            if (!Accessibility.IsTargetAlive(target.Application, target.Window, target.ProcessId))
                return TextDeliveryResult.TargetUnavailable;
            return request.Method == TextDeliveryMethod.TargetWithFocus
                ? DeliverWithFocus(request, target, cancellationToken)
                : DeliverWithoutFocus(request, target, cancellationToken);
        }
    }

    private TextDeliveryResult DeliverWithFocus(
        TextDeliveryRequest request,
        MacTargetLease target,
        CancellationToken cancellationToken)
    {
        if (!MacTargetWindow.TryCaptureActiveLease(out MacTargetLease? original))
            return TextDeliveryResult.Failed;

        using (original)
        {
            TextDeliveryResult result = TextDeliveryResult.Failed;
            bool injectionMayHaveStarted = false;
            try
            {
                if (!MacTargetActivator.ActivateRaiseAndVerify(target, cancellationToken))
                    return result;

                cancellationToken.ThrowIfCancellationRequested();
                if (!Accessibility.IsFocusedTarget(
                        target.Application,
                        target.Window,
                        target.ProcessId))
                    return result;

                injectionMayHaveStarted = true;
                result = DeliverUnicode(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                result = TextDeliveryResult.Cancelled;
            }
            catch (Exception exception)
            {
                Logger.Error("Falha ao entregar texto no destino fixo com foco", exception);
                result = TextDeliveryResult.Failed;
            }
            finally
            {
                if (injectionMayHaveStarted) Thread.Sleep(EventQueueSettleMs);
                try
                {
                    if (!MacTargetActivator.ActivateRaiseAndVerify(original))
                    {
                        Logger.Error("Nao foi possivel restaurar a janela original apos a entrega.");
                        result = TextDeliveryResult.Failed;
                    }
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha ao restaurar a janela original apos a entrega", exception);
                    result = TextDeliveryResult.Failed;
                }
            }
            return result;
        }
    }

    private static TextDeliveryResult DeliverWithoutFocus(
        TextDeliveryRequest request,
        MacTargetLease target,
        CancellationToken cancellationToken)
    {
        if (request.PressEnter) return TextDeliveryResult.Unsupported;
        if (request.Text.Length == 0) return TextDeliveryResult.Delivered;

        if (!Accessibility.TryGetFocusedInsertionElement(
                target.Application,
                target.Window,
                target.ProcessId,
                out IntPtr element))
            return TextDeliveryResult.Unsupported;

        try
        {
            if (!Accessibility.IsAttributeSettable(element, Accessibility.SelectedTextAttribute))
                return TextDeliveryResult.Unsupported;

            cancellationToken.ThrowIfCancellationRequested();
            bool delivered = Accessibility.SetStringAttribute(
                element,
                Accessibility.SelectedTextAttribute,
                request.Text);
            if (!delivered) return TextDeliveryResult.Failed;

            Logger.Info(
                $"AXSelectedText entregou {request.Text.Length} unidades UTF-16 sem mudar o foco.");
            return TextDeliveryResult.Delivered;
        }
        finally
        {
            CoreFoundation.Release(element);
        }
    }

    private TextDeliveryResult DeliverUnicode(
        TextDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        int scalarCount = 0;
        ReadOnlySpan<char> remaining = request.Text.AsSpan();

        while (!remaining.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int consumed = GetScalarLength(remaining);

            PostUnicode(remaining[..consumed]);
            PostKey(0, keyDown: false);
            remaining = remaining[consumed..];
            scalarCount++;
            if (!remaining.IsEmpty) Thread.Sleep(ChunkPauseMs);
        }

        if (request.PressEnter)
        {
            if (scalarCount > 0) Thread.Sleep(EnterPauseMs);
            cancellationToken.ThrowIfCancellationRequested();
            PostKey(CoreGraphics.ReturnKey, keyDown: true);
            PostKey(CoreGraphics.ReturnKey, keyDown: false);
        }

        Logger.Info(
            $"CGEvent entregou {request.Text.Length} unidades UTF-16 em {scalarCount} escalares "
            + $"e {elapsed.ElapsedMilliseconds} ms{(request.PressEnter ? ", com Enter" : string.Empty)}.");
        return TextDeliveryResult.Delivered;
    }

    private static int GetScalarLength(ReadOnlySpan<char> text)
    {
        OperationStatus status = Rune.DecodeFromUtf16(text, out _, out int consumed);
        return status == OperationStatus.Done ? consumed : 1;
    }

    private void PostUnicode(ReadOnlySpan<char> text)
    {
        fixed (char* units = text)
        {
            IntPtr @event = CoreGraphics.CGEventCreateKeyboardEvent(_source, 0, keyDown: true);
            if (@event == IntPtr.Zero)
                throw new InvalidOperationException("CGEventCreateKeyboardEvent failed.");
            try
            {
                CoreGraphics.CGEventKeyboardSetUnicodeString(
                    @event,
                    (nuint)text.Length,
                    (ushort*)units);
                Post(@event);
            }
            finally
            {
                CoreGraphics.CFRelease(@event);
            }
        }
    }

    private void PostKey(ushort keyCode, bool keyDown)
    {
        IntPtr @event = CoreGraphics.CGEventCreateKeyboardEvent(_source, keyCode, keyDown);
        if (@event == IntPtr.Zero)
            throw new InvalidOperationException("CGEventCreateKeyboardEvent failed.");
        try { Post(@event); }
        finally { CoreGraphics.CFRelease(@event); }
    }

    private static void Post(IntPtr @event)
    {
        CoreGraphics.CGEventSetIntegerValueField(
            @event,
            CoreGraphics.EventSourceUserData,
            MacInput.InjectionTag);
        CoreGraphics.CGEventPost(CoreGraphics.HidEventTap, @event);
    }

    public void Dispose()
    {
        lock (_deliveryGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CoreGraphics.CFRelease(_source);
        }
    }
}
