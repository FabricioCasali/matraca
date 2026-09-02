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

    private readonly IntPtr _source;
    private readonly object _deliveryGate = new();
    private int _disposed;

    public MacTextSink()
    {
        Frameworks.EnsureLoaded();
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
        if (request.Method != TextDeliveryMethod.Unicode || request.Target != null)
            return Task.FromResult(TextDeliveryResult.InvalidRequest);

        try
        {
            lock (_deliveryGate)
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return Task.FromResult(TextDeliveryResult.Failed);
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromResult(TextDeliveryResult.Cancelled);
                return Task.FromResult(Deliver(request, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(TextDeliveryResult.Cancelled);
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao entregar texto por CGEvent", exception);
            return Task.FromResult(TextDeliveryResult.Failed);
        }
    }

    private TextDeliveryResult Deliver(
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
