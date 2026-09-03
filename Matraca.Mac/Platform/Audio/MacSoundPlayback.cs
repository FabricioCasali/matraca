using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Audio;

internal sealed class MacSoundPlayback : IDisposable
{
    private const int CleanupGraceMilliseconds = 2000;

    private IntPtr _player;

    public MacSoundPlayback(IntPtr player, int durationMilliseconds)
    {
        _player = player;
        DeadlineTick = Environment.TickCount64
            + durationMilliseconds
            + CleanupGraceMilliseconds;
    }

    public long DeadlineTick { get; }

    public bool IsPlaying => AVAudioPlayerInterop.IsPlaying(_player);

    public void Dispose()
    {
        IntPtr player = Interlocked.Exchange(ref _player, IntPtr.Zero);
        AVAudioPlayerInterop.StopAndRelease(player);
    }
}
