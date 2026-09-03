namespace Matraca.Core;

public sealed class BoundedShutdownCoordinator
{
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task> _shutdown;
    private readonly TimeSpan _gracePeriod;
    private Task<bool>? _completion;

    public BoundedShutdownCoordinator(
        Func<CancellationToken, Task> shutdown,
        TimeSpan gracePeriod)
    {
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        if (gracePeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(gracePeriod));
        _gracePeriod = gracePeriod;
    }

    public Task<bool> RequestShutdownAsync()
    {
        lock (_gate) return _completion ??= RunAsync();
    }

    private async Task<bool> RunAsync()
    {
        var deadline = new CancellationTokenSource(_gracePeriod);
        Task shutdown;
        bool deadlineOwnershipTransferred = false;
        try
        {
            shutdown = Task.Run(() => _shutdown(deadline.Token), CancellationToken.None);
        }
        catch
        {
            deadline.Dispose();
            throw;
        }

        try
        {
            await shutdown.WaitAsync(deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            deadlineOwnershipTransferred = true;
            ObserveAndDispose(shutdown, deadline);
            return false;
        }
        finally
        {
            if (!deadlineOwnershipTransferred) deadline.Dispose();
        }
    }

    private static void ObserveAndDispose(Task task, CancellationTokenSource deadline)
        => _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                deadline.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
