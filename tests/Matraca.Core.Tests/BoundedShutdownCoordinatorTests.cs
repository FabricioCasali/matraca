using System.Diagnostics;
using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class BoundedShutdownCoordinatorTests
{
    [Fact]
    public async Task RepeatedRequestsShareOneShutdownAndDrainWithinGrace()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var coordinator = new BoundedShutdownCoordinator(
            _ =>
            {
                Interlocked.Increment(ref calls);
                started.SetResult();
                return release.Task;
            },
            TimeSpan.FromSeconds(1));

        Task<bool> first = coordinator.RequestShutdownAsync();
        Task<bool> second = coordinator.RequestShutdownAsync();

        Assert.Same(first, second);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, calls);
        release.SetResult();
        Assert.True(await first);
    }

    [Fact]
    public async Task SynchronouslyBlockedShutdownCannotBlockTheRequestingThread()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new BoundedShutdownCoordinator(
            _ =>
            {
                started.SetResult();
                release.Wait();
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(25));
        var stopwatch = Stopwatch.StartNew();

        Task<bool> shutdown = coordinator.RequestShutdownAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(await shutdown.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task DeadlineCancelsCleanupButDoesNotWaitForUncooperativeWork()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new BoundedShutdownCoordinator(
            cancellationToken =>
            {
                cancellationToken.Register(() => cancelled.TrySetResult());
                return never.Task;
            },
            TimeSpan.FromMilliseconds(25));
        var stopwatch = Stopwatch.StartNew();

        bool completed = await coordinator.RequestShutdownAsync()
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(completed);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        never.SetResult();
    }

    [Fact]
    public async Task FailureIsReportedWithoutStartingShutdownAgain()
    {
        int calls = 0;
        var coordinator = new BoundedShutdownCoordinator(
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException(new InvalidOperationException("broken"));
            },
            TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(coordinator.RequestShutdownAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(coordinator.RequestShutdownAsync);
        Assert.Equal(1, calls);
    }
}
