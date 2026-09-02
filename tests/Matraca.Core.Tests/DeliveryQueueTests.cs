using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class DeliveryQueueTests
{
    [Fact]
    public async Task DeliversOneIndivisibleRequestAtATimeInFifoOrder()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sink = new RecordingTextSink(async (_, call, _) =>
        {
            if (call == 1)
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
            }
            return TextDeliveryResult.Delivered;
        });
        using var queue = new DeliveryQueue(sink);
        var firstRequest = new TextDeliveryRequest("first", true, TextDeliveryMethod.Unicode);
        var secondRequest = new TextDeliveryRequest("second", false, TextDeliveryMethod.Clipboard);

        Task<TextDeliveryResult> first = queue.EnqueueAsync(firstRequest);
        await firstStarted.Task;
        Task<TextDeliveryResult> second = queue.EnqueueAsync(secondRequest);

        Assert.Single(sink.Requests);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        releaseFirst.TrySetResult();

        Assert.Equal(TextDeliveryResult.Delivered, await first);
        Assert.Equal(TextDeliveryResult.Delivered, await second);
        Assert.Equal([firstRequest, secondRequest], sink.Requests);
    }

    [Fact]
    public async Task FailureDoesNotPoisonTheNextDelivery()
    {
        using var sink = new RecordingTextSink((_, call, _) => call == 1
            ? throw new InvalidOperationException("first failed")
            : Task.FromResult(TextDeliveryResult.Delivered));
        using var queue = new DeliveryQueue(sink);

        var failed = queue.EnqueueAsync(new TextDeliveryRequest("bad", false, TextDeliveryMethod.Unicode));
        var delivered = queue.EnqueueAsync(new TextDeliveryRequest("good", true, TextDeliveryMethod.Unicode));

        Assert.Equal(TextDeliveryResult.Failed, await failed);
        Assert.Equal(TextDeliveryResult.Delivered, await delivered);
    }

    [Fact]
    public async Task CancellationAndShutdownCompleteEveryAcceptedItem()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sink = new RecordingTextSink(async (_, call, _) =>
        {
            if (call == 1) await release.Task;
            return TextDeliveryResult.Delivered;
        });
        using var queue = new DeliveryQueue(sink);
        using var cancellation = new CancellationTokenSource();

        var first = queue.EnqueueAsync(new TextDeliveryRequest("first", false, TextDeliveryMethod.Unicode));
        var cancelled = queue.EnqueueAsync(
            new TextDeliveryRequest("cancelled", false, TextDeliveryMethod.Unicode),
            cancellation.Token);
        cancellation.Cancel();
        Task shutdown = queue.ShutdownAsync();

        Assert.False(shutdown.IsCompleted);
        release.TrySetResult();
        await shutdown;

        Assert.Equal(TextDeliveryResult.Delivered, await first);
        Assert.Equal(TextDeliveryResult.Cancelled, await cancelled);
        Assert.Equal(TextDeliveryResult.Failed, await queue.EnqueueAsync(
            new TextDeliveryRequest("late", false, TextDeliveryMethod.Unicode)));
    }
}
