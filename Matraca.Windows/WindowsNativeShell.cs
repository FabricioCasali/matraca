namespace Matraca;

internal sealed class WindowsNativeShell : IShell
{
    private readonly WindowsTrayIcon _tray;
    private int _disposed;

    public WindowsNativeShell(WindowsTrayIcon tray)
    {
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));
    }

    public event Action<ShellState, string>? StateChanged;

    public ShellState CurrentState { get; private set; } = ShellState.Idle;

    public string CurrentText { get; private set; } = "Matraca pronto.";

    public void SetState(ShellState state, string text)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(text);
        CurrentState = state;
        CurrentText = text;
        _tray.SetState(state, text);
        StateChanged?.Invoke(state, text);
    }

    public void ShowNotification(
        string title,
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Info)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _tray.ShowNotification(title, message, level);
    }

    public int PlaySound(bool start, string? filePath, float volume)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return Beeper.PlaySoundFile(filePath, volume);

        var notes = start
            ? new[] { (660, 90), (990, 130) }
            : new[] { (990, 90), (590, 150) };
        return Beeper.Play(notes, volume);
    }

    public bool IsSoundActive(TimeSpan quietPeriod)
        => Beeper.InBeepShadow(Math.Max(0, (int)quietPeriod.TotalMilliseconds));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StateChanged = null;
    }
}
