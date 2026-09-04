using System.Windows.Forms;

namespace Matraca;

internal sealed class WindowsShell : IShell
{
    private readonly NotifyIcon _tray;
    private readonly Icon _idleIcon;
    private readonly Icon _recordingIcon;
    private readonly Icon _busyIcon;
    private int _disposed;

    public event Action<ShellState, string>? StateChanged;
    public ShellState CurrentState { get; private set; } = ShellState.Idle;
    public string CurrentText { get; private set; } = "Matraca pronto.";

    public WindowsShell(NotifyIcon tray, Icon idleIcon, Icon recordingIcon, Icon busyIcon)
    {
        _tray = tray;
        _idleIcon = idleIcon;
        _recordingIcon = recordingIcon;
        _busyIcon = busyIcon;
    }

    public void SetState(ShellState state, string text)
    {
        CurrentState = state;
        CurrentText = text;
        _tray.Icon = state switch
        {
            ShellState.Recording => _recordingIcon,
            ShellState.Busy => _busyIcon,
            ShellState.Error => _busyIcon,
            _ => _idleIcon,
        };
        _tray.Text = text.Length <= 63 ? text : text[..60] + "...";
        StateChanged?.Invoke(state, text);
    }

    public void ShowNotification(
        string title,
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Info)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = message;
        _tray.BalloonTipIcon = level switch
        {
            ShellNotificationLevel.Warning => ToolTipIcon.Warning,
            ShellNotificationLevel.Error => ToolTipIcon.Error,
            _ => ToolTipIcon.Info,
        };
        _tray.ShowBalloonTip(5000);
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
        _tray.Visible = false;
        _tray.Dispose();
        StateChanged = null;
    }
}
