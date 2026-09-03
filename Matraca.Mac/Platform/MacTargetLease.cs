using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacTargetLease : IDisposable
{
    private readonly IntPtr _application;
    private readonly IntPtr _window;
    private int _disposed;

    internal MacTargetLease(IntPtr application, IntPtr window, int processId)
    {
        _application = application;
        _window = window;
        ProcessId = processId;
    }

    internal IntPtr Application
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _application;
        }
    }

    internal IntPtr Window
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _window;
        }
    }

    internal int ProcessId { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        CoreFoundation.Release(_window);
        CoreFoundation.Release(_application);
    }
}
