using System.Collections.Concurrent;
using Matraca.Core;

namespace Matraca.Core.Tests;

internal sealed class RecordingTextSink : ITextSink
{
    private readonly Func<TextDeliveryRequest, int, CancellationToken, Task<TextDeliveryResult>> _deliver;
    private int _callCount;

    public RecordingTextSink(
        Func<TextDeliveryRequest, int, CancellationToken, Task<TextDeliveryResult>>? deliver = null)
    {
        _deliver = deliver ?? ((_, _, _) => Task.FromResult(TextDeliveryResult.Delivered));
    }

    public ConcurrentQueue<TextDeliveryRequest> Requests { get; } = new();
    public bool Disposed { get; private set; }

    public Task<TextDeliveryResult> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Enqueue(request);
        return _deliver(request, Interlocked.Increment(ref _callCount), cancellationToken);
    }

    public void Dispose() => Disposed = true;
}
