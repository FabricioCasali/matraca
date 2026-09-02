using Matraca.Core;

namespace Matraca.Mac.Platform;

internal sealed class MacShell : IShell
{
    private readonly MacStatusItem _statusItem;

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
        if (level == ShellNotificationLevel.Error) Logger.Error(text);
        else if (level == ShellNotificationLevel.Warning) Logger.Warn(text);
        else Logger.Info(text);
    }

    public int PlaySound(bool start, string? filePath, float volume) => 0;

    public bool IsSoundActive(TimeSpan quietPeriod) => false;

    public void Dispose()
    {
        // MacStatusItem is owned by Program and outlives the dictation controller.
    }
}
