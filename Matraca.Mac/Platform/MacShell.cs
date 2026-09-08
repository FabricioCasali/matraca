using Matraca.Core;
using Matraca.Mac.Platform.Audio;

namespace Matraca.Mac.Platform;

internal sealed class MacShell : IShell
{
    private readonly MacStatusItem _statusItem;
    private readonly MacSoundPlayer _soundPlayer = new();
    private MacUiText _ui;

    public MacShell(MacStatusItem statusItem, string effectiveUiLanguage)
    {
        _statusItem = statusItem ?? throw new ArgumentNullException(nameof(statusItem));
        _ui = new MacUiText(effectiveUiLanguage);
        CurrentText = _ui.HudReady;
    }

    public event Action<ShellState, string>? StateChanged;
    public ShellState CurrentState { get; private set; } = ShellState.Idle;
    public string CurrentText { get; private set; } = "Matraca";

    public void SetState(ShellState state, string text)
    {
        CurrentState = state;
        CurrentText = text;
        string title = _ui.StatusTitle(state);
        _statusItem.SetState(title, text);
        StateChanged?.Invoke(state, text);
    }

    public void SetLanguage(string effectiveUiLanguage)
    {
        _ui = new MacUiText(effectiveUiLanguage);
        _statusItem.SetLanguage(effectiveUiLanguage);
        _statusItem.SetState(_ui.StatusTitle(CurrentState), CurrentText);
    }

    public void ShowNotification(
        string title,
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Info)
    {
        string text = $"{title}: {message}";
        if (level is ShellNotificationLevel.Warning or ShellNotificationLevel.Error)
            _statusItem.SetState(_ui.StatusTitle(ShellState.Error), text);
        if (level == ShellNotificationLevel.Error) Logger.Error(text);
        else if (level == ShellNotificationLevel.Warning) Logger.Warn(text);
        else Logger.Info(text);
    }

    public int PlaySound(bool start, string? filePath, float volume)
        => _soundPlayer.Play(start, filePath, volume);

    public bool IsSoundActive(TimeSpan quietPeriod)
        => _soundPlayer.IsActive(quietPeriod);

    public void Dispose()
    {
        StateChanged = null;
        _soundPlayer.Dispose();
        // MacStatusItem is owned by Program and outlives the dictation controller.
    }
}
