using Matraca.Core;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class AsyncCallbackQueueTests
{
    [Fact]
    public void CallbackRunsOutsideThePostingThread()
    {
        int postingThread = Environment.CurrentManagedThreadId;
        int callbackThread = postingThread;
        using var called = new ManualResetEventSlim();
        using var queue = new AsyncCallbackQueue<int>(_ =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            called.Set();
        });

        Assert.True(queue.TryPost(42));
        Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
        Assert.NotEqual(postingThread, callbackThread);
    }
}
