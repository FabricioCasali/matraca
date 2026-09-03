using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Audio;

internal sealed class MacSoundPlayer : IDisposable
{
    private const int CleanupIntervalMilliseconds = 25;
    private const int MaximumConcurrentPlaybacks = 4;
    private const int MaximumFileBytes = 16 * 1024 * 1024;
    private const int MaximumDecodedBytes = 64 * 1024 * 1024;
    private const int MaximumPlaybackMilliseconds = 30_000;

    private readonly object _gate = new();
    private readonly List<MacSoundPlayback> _playbacks = [];
    private readonly Timer _cleanupTimer;
    private readonly ManualResetEventSlim _operationsCompleted = new(initialState: true);
    private int _operations;
    private int _starting;
    private bool _hasPlayed;
    private bool _quietPending;
    private long _quietSinceTick;
    private int _disposed;

    public MacSoundPlayer()
    {
        _cleanupTimer = new Timer(
            Cleanup,
            null,
            CleanupIntervalMilliseconds,
            CleanupIntervalMilliseconds);
    }

    public int Play(bool start, string? filePath, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        if (volume <= 0 || !TryEnterOperation()) return 0;

        try
        {
            if (!ReservePlayback()) return 0;
            MacSoundPlayback? playback = null;
            int durationMilliseconds = 0;
            try
            {
                byte[] audioFile = LoadAudioFile(start, filePath);
                if (!AVAudioPlayerInterop.TryStart(
                        audioFile,
                        volume,
                        MaximumPlaybackMilliseconds,
                        out IntPtr player,
                        out durationMilliseconds,
                        out string? error))
                {
                    Logger.Warn($"beep: falha ao tocar: {error}");
                    return 0;
                }

                playback = new MacSoundPlayback(player, durationMilliseconds);
                return AcceptPlayback(playback) ? durationMilliseconds : 0;
            }
            catch (Exception exception)
            {
                Logger.Warn($"beep: falha ao preparar o som: {exception.Message}");
                return 0;
            }
            finally
            {
                CompleteReservation(playback);
            }
        }
        finally { ExitOperation(); }
    }

    public bool IsActive(TimeSpan quietPeriod)
    {
        if (!TryEnterOperation()) return false;
        try
        {
            CleanupCore();
            long quietMilliseconds = quietPeriod <= TimeSpan.Zero
                ? 0
                : (long)Math.Min(long.MaxValue, quietPeriod.TotalMilliseconds);
            lock (_gate)
            {
                return _starting > 0
                    || _playbacks.Count > 0
                    || (_hasPlayed
                        && Environment.TickCount64 - _quietSinceTick < quietMilliseconds);
            }
        }
        finally { ExitOperation(); }
    }

    private bool ReservePlayback()
    {
        CleanupCore();
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            if (_playbacks.Count + _starting >= MaximumConcurrentPlaybacks)
            {
                Logger.Warn("beep: limite de reproducoes simultaneas atingido; ignorando o som.");
                return false;
            }

            _starting++;
            return true;
        }
    }

    private bool AcceptPlayback(MacSoundPlayback playback)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            _playbacks.Add(playback);
            _hasPlayed = true;
            _quietPending = false;
            return true;
        }
    }

    private void CompleteReservation(MacSoundPlayback? playback)
    {
        bool accepted;
        lock (_gate)
        {
            _starting--;
            accepted = playback != null && _playbacks.Contains(playback);
            if (_starting == 0)
            {
                if (_quietPending && _playbacks.Count == 0)
                {
                    _quietSinceTick = Environment.TickCount64;
                    _quietPending = false;
                }
            }
        }

        if (playback != null && !accepted) DisposePlayback(playback);
    }

    private static byte[] LoadAudioFile(bool start, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return MacSoundWave.Create(start);

        long length = new FileInfo(filePath).Length;
        if (length > MaximumFileBytes)
            throw new InvalidDataException(
                $"'{Path.GetFileName(filePath)}' excede o limite de {MaximumFileBytes / 1024 / 1024} MB");
        return ExtAudioFileDecoder.DecodeToTrimmedWave(
            filePath,
            MaximumPlaybackMilliseconds,
            MaximumDecodedBytes);
    }

    private void Cleanup(object? state)
    {
        if (!TryEnterOperation()) return;
        try { CleanupCore(); }
        finally { ExitOperation(); }
    }

    private void CleanupCore()
    {
        List<MacSoundPlayback>? finished = null;
        long now = Environment.TickCount64;
        try
        {
            lock (_gate)
            {
                for (int index = _playbacks.Count - 1; index >= 0; index--)
                {
                    MacSoundPlayback playback = _playbacks[index];
                    if (now < playback.DeadlineTick && playback.IsPlaying) continue;
                    _playbacks.RemoveAt(index);
                    (finished ??= []).Add(playback);
                }

                if (finished != null && _playbacks.Count == 0)
                {
                    if (_starting == 0) _quietSinceTick = now;
                    else _quietPending = true;
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Warn($"beep: falha ao consultar reproducao: {exception.Message}");
        }

        if (finished == null) return;
        foreach (MacSoundPlayback playback in finished) DisposePlayback(playback);
    }

    private bool TryEnterOperation()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            if (_operations == 0) _operationsCompleted.Reset();
            _operations++;
            return true;
        }
    }

    private void ExitOperation()
    {
        lock (_gate)
        {
            _operations--;
            if (_operations == 0) _operationsCompleted.Set();
        }
    }

    private static void DisposePlayback(MacSoundPlayback playback)
    {
        try { playback.Dispose(); }
        catch (Exception exception)
        {
            Logger.Warn($"beep: falha ao liberar reproducao: {exception.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed != 0) return;
            Volatile.Write(ref _disposed, 1);
        }
        using var timerStopped = new ManualResetEvent(false);
        _cleanupTimer.Dispose(timerStopped);
        timerStopped.WaitOne();
        _operationsCompleted.Wait();

        MacSoundPlayback[] playbacks;
        lock (_gate)
        {
            playbacks = _playbacks.ToArray();
            _playbacks.Clear();
        }

        foreach (MacSoundPlayback playback in playbacks) DisposePlayback(playback);
        _operationsCompleted.Dispose();
    }
}
