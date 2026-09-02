using System.Collections.Concurrent;
using Matraca.Core;

namespace Matraca.Core.Tests;

internal sealed class FakeShell : IShell
{
    public ConcurrentQueue<(ShellState State, string Text)> States { get; } = new();
    public ConcurrentQueue<(string Title, string Message, ShellNotificationLevel Level)> Notifications { get; } = new();
    public int SoundMilliseconds { get; set; }
    public int SoundCount { get; private set; }
    public bool Disposed { get; private set; }

    public void SetState(ShellState state, string text) => States.Enqueue((state, text));

    public void ShowNotification(
        string title,
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Info)
        => Notifications.Enqueue((title, message, level));

    public int PlaySound(bool start, string? filePath, float volume)
    {
        SoundCount++;
        return SoundMilliseconds;
    }

    public bool IsSoundActive(TimeSpan quietPeriod) => false;
    public void Dispose() => Disposed = true;
}
