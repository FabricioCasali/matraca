using Matraca.Core;
using Matraca.Mac.Platform.Audio;

namespace Matraca.Mac.Platform;

internal sealed class MacShell : IShell
{
    private readonly MacStatusItem _statusItem;
    private readonly MacSoundPlayer _soundPlayer = new();

    public MacShell(MacStatusItem statusItem)
        => _statusItem = statusItem ?? throw new ArgumentNullException(nameof(statusItem));

    public void SetState(ShellState state, string text)
    {
        string title = state switch
        {
            ShellState.Recording => "Matraca REC",
            ShellState.Busy => "Matraca ...",
            ShellState.Error => "Matraca !",
            _ => "Matraca",
        };
        _statusItem.SetState(title, text);
    }

    public void ShowNotification(
        string title,
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Info)
    {
        string text = $"{title}: {message}";
        if (level is ShellNotificationLevel.Warning or ShellNotificationLevel.Error)
            _statusItem.SetState("Matraca !", text);
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
        _soundPlayer.Dispose();
        // MacStatusItem is owned by Program and outlives the dictation controller.
    }
}
