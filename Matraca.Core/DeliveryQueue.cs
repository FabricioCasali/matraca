using System.Collections.Concurrent;

namespace Matraca.Core;

public sealed class DeliveryQueue : IDisposable
{
    private readonly BlockingCollection<(TextDeliveryRequest Request, CancellationToken Cancellation, TaskCompletionSource<TextDeliveryResult> Completion)> _items = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ITextSink _sink;
    private readonly Thread _consumer;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;
    private int _disposed;

    public DeliveryQueue(ITextSink sink, Action<Thread>? configureConsumerThread = null)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _consumer = new Thread(Consume)
        {
            IsBackground = true,
            Name = "Matraca.TextDelivery",
        };
        configureConsumerThread?.Invoke(_consumer);
        _consumer.Start();
    }

    public Task<TextDeliveryResult> EnqueueAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(TextDeliveryResult.Cancelled);
        if (Volatile.Read(ref _completed) != 0)
            return Task.FromResult(TextDeliveryResult.Failed);

        var completion = new TaskCompletionSource<TextDeliveryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _items.Add((request, cancellationToken, completion), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetResult(TextDeliveryResult.Cancelled);
        }
        catch (InvalidOperationException)
        {
            completion.TrySetResult(TextDeliveryResult.Failed);
        }
        return completion.Task;
    }

    public async Task ShutdownAsync(
        bool cancelPending = false,
        CancellationToken cancellationToken = default)
    {
        if (cancelPending) CancelPending();
        else StopAccepting();

        await _stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void StopAccepting()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _items.CompleteAdding();
    }

    internal void CancelPending()
    {
        try { _shutdown.Cancel(); }
        catch (ObjectDisposedException) { }
        StopAccepting();
    }

    private void Consume()
    {
        try
        {
            foreach (var item in _items.GetConsumingEnumerable())
            {
                if (_shutdown.IsCancellationRequested || item.Cancellation.IsCancellationRequested)
                {
                    item.Completion.TrySetResult(TextDeliveryResult.Cancelled);
                    continue;
                }

                try
                {
                    using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        _shutdown.Token,
                        item.Cancellation);
                    var result = _sink.DeliverAsync(item.Request, deliveryCancellation.Token)
                        .GetAwaiter().GetResult();
                    item.Completion.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    item.Completion.TrySetResult(TextDeliveryResult.Cancelled);
                }
                catch (Exception exception)
                {
                    Logger.Error("Falha ao entregar o texto", exception);
                    item.Completion.TrySetResult(TextDeliveryResult.Failed);
                }
            }
        }
        finally
        {
            while (_items.TryTake(out var pending))
                pending.Completion.TrySetResult(TextDeliveryResult.Cancelled);
            _stopped.TrySetResult();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { ShutdownAsync(cancelPending: true).GetAwaiter().GetResult(); }
        finally
        {
            _shutdown.Dispose();
            _items.Dispose();
            _sink.Dispose();
        }
    }
}
