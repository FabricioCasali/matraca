namespace Matraca.Core;

public sealed class TranscriptionModel : IDisposable
{
    private readonly Func<float[], CancellationToken, Task<string>> _transcribe;
    private readonly Action? _dispose;
    private int _disposed;

    public TranscriptionModel(
        Func<float[], CancellationToken, Task<string>> transcribe,
        Action? dispose = null)
    {
        _transcribe = transcribe ?? throw new ArgumentNullException(nameof(transcribe));
        _dispose = dispose;
    }

    public Task<string> TranscribeAsync(
        float[] samples,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _transcribe(samples, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _dispose?.Invoke();
    }
}
