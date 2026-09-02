namespace Matraca.Core;

public interface IShell : IDisposable
{
    void SetState(ShellState state, string text);
    void ShowNotification(string title, string message, ShellNotificationLevel level = ShellNotificationLevel.Info);
    int PlaySound(bool start, string? filePath, float volume);
    bool IsSoundActive(TimeSpan quietPeriod);
}
