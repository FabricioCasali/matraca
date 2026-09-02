using System.Collections.Concurrent;

namespace Matraca.Core;

public sealed class AsyncCallbackQueue<T> : IDisposable
{
    private readonly BlockingCollection<T> _items = new();
    private readonly Action<T> _callback;
    private readonly Action<Exception>? _errorHandler;
    private readonly Task _worker;
    private int _disposed;
    private int _workerThreadId;

    public AsyncCallbackQueue(Action<T> callback, Action<Exception>? errorHandler = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        _errorHandler = errorHandler;
        _worker = Task.Factory.StartNew(
            Consume,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public bool TryPost(T item)
    {
        if (Volatile.Read(ref _disposed) != 0) return false;
        try { return _items.TryAdd(item); }
        catch (InvalidOperationException) { return false; }
    }

    private void Consume()
    {
        _workerThreadId = Environment.CurrentManagedThreadId;
        foreach (var item in _items.GetConsumingEnumerable())
        {
            try { _callback(item); }
            catch (Exception exception) { _errorHandler?.Invoke(exception); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _items.CompleteAdding();
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref _workerThreadId))
            _worker.GetAwaiter().GetResult();
    }
}
