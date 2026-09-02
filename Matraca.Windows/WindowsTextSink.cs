using System.Collections.Concurrent;

namespace Matraca;

internal sealed class WindowsTextSink : ITextSink
{
    private readonly BlockingCollection<(TextDeliveryRequest Request, TaskCompletionSource<TextDeliveryResult> Completion, CancellationToken Cancellation)> _queue = new();
    private readonly WindowsTargetWindow _targets;
    private readonly Thread _worker;
    private int _disposed;

    public WindowsTextSink(WindowsTargetWindow targets)
    {
        _targets = targets;
        _worker = new Thread(Consume)
        {
            IsBackground = true,
            Name = "Matraca.TextDelivery",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public Task<TextDeliveryResult> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Volatile.Read(ref _disposed) != 0)
            return Task.FromResult(TextDeliveryResult.Failed);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(TextDeliveryResult.Cancelled);

        var completion = new TaskCompletionSource<TextDeliveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try { _queue.Add((request, completion, cancellationToken), cancellationToken); }
        catch (OperationCanceledException) { completion.TrySetResult(TextDeliveryResult.Cancelled); }
        catch (InvalidOperationException) { completion.TrySetResult(TextDeliveryResult.Failed); }
        return completion.Task;
    }

    private void Consume()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            if (work.Cancellation.IsCancellationRequested)
            {
                work.Completion.TrySetResult(TextDeliveryResult.Cancelled);
                continue;
            }

            try { work.Completion.TrySetResult(Deliver(work.Request)); }
            catch (Exception exception)
            {
                Logger.Error("Falha ao entregar o texto", exception);
                work.Completion.TrySetResult(TextDeliveryResult.Failed);
            }
        }
    }

    private TextDeliveryResult Deliver(TextDeliveryRequest request)
    {
        if (!Enum.IsDefined(request.Method)) return TextDeliveryResult.InvalidRequest;

        if (request.Method is TextDeliveryMethod.TargetWithFocus or TextDeliveryMethod.TargetWithoutFocus)
        {
            if (request.Target == null) return TextDeliveryResult.InvalidRequest;
            if (!_targets.TryResolve(request.Target, out var handle) || !TextInjector.IsWindowAlive(handle))
                return TextDeliveryResult.TargetUnavailable;

            bool delivered = request.Method == TextDeliveryMethod.TargetWithFocus
                ? TextInjector.DeliverWithFocus(handle, request.Text, request.PressEnter)
                : TextInjector.SendToWindow(handle, request.Text, request.PressEnter);
            return delivered ? TextDeliveryResult.Delivered : TextDeliveryResult.Failed;
        }

        if (request.Target != null) return TextDeliveryResult.InvalidRequest;
        bool success = TextInjector.PasteText(
            request.Text,
            request.PressEnter,
            request.Method == TextDeliveryMethod.Clipboard ? "clipboard" : "unicode");
        return success ? TextDeliveryResult.Delivered : TextDeliveryResult.Failed;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _queue.CompleteAdding();
        if (Thread.CurrentThread != _worker) _worker.Join();
        _queue.Dispose();
    }
}
