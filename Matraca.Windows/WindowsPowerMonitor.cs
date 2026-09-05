namespace Matraca;

internal sealed class WindowsPowerMonitor : IDisposable
{
    private readonly WindowsDispatcher _dispatcher;
    private int _sessionEndingQueued;
    private int _suspended;
    private int _disposed;

    public WindowsPowerMonitor(WindowsDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _dispatcher.UnhandledMessage += OnUnhandledMessage;
    }

    public event Action? Suspending;
    public event Action? Resumed;
    public event Action? SessionEnding;

    private void OnUnhandledMessage(uint message, nint wParam, nint lParam)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        if (message == WindowsNativeMethods.WmPowerBroadcast)
        {
            uint powerEvent = unchecked((uint)wParam);
            if (powerEvent == WindowsNativeMethods.PbtApmSuspend &&
                Interlocked.Exchange(ref _suspended, 1) == 0)
            {
                PublishSuspending();
            }
            else if (powerEvent == WindowsNativeMethods.PbtApmResumeAutomatic &&
                     Interlocked.Exchange(ref _suspended, 0) != 0)
            {
                PublishResumed();
            }
        }
        else if (message == WindowsNativeMethods.WmEndSession)
        {
            // WM_QUERYENDSESSION ainda pode ser cancelada por outro aplicativo. O encerramento
            // irreversivel comeca somente aqui, depois da confirmacao do Windows.
            if (wParam == nint.Zero)
                Interlocked.Exchange(ref _sessionEndingQueued, 0);
            else
                PublishSessionEndingOnce();
        }
    }

    private void PublishSessionEndingOnce()
    {
        if (Interlocked.Exchange(ref _sessionEndingQueued, 1) == 0)
            PublishSessionEnding();
    }

    private void PublishSuspending()
    {
        if (Volatile.Read(ref _disposed) == 0) Suspending?.Invoke();
    }

    private void PublishResumed()
    {
        if (Volatile.Read(ref _disposed) == 0) Resumed?.Invoke();
    }

    private void PublishSessionEnding()
    {
        if (Volatile.Read(ref _disposed) == 0) SessionEnding?.Invoke();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _dispatcher.UnhandledMessage -= OnUnhandledMessage;
        Suspending = null;
        Resumed = null;
        SessionEnding = null;
    }
}
