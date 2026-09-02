using NAudio.Wave;

namespace Matraca;

internal sealed class WindowsAudioCapture : IAudioCapture
{
    private const int BytesPerMillisecond = IAudioCapture.RequiredSampleRate * 2 / 1000;
    private const int MaximumExtraMuteMilliseconds = 400;
    private static readonly TimeSpan SoundGuard = TimeSpan.FromMilliseconds(150);

    private readonly IShell _shell;
    private WaveInEvent? _waveIn;
    private MemoryStream _buffer = new();
    private TaskCompletionSource<float[]>? _stopCompletion;
    private int _muteBytesLeft;
    private long _dynamicMuteDeadline;
    private int _capturing;

    public event Action<ReadOnlyMemory<float>>? FrameCaptured;

    public bool IsCapturing => Volatile.Read(ref _capturing) != 0;

    public WindowsAudioCapture(IShell shell)
    {
        _shell = shell;
    }

    public IReadOnlyList<string> ListDevices() => AudioDevices.ListNames();

    public async Task StartAsync(
        string? deviceName,
        TimeSpan initialMute,
        CancellationToken cancellationToken = default)
    {
        if (IsCapturing) throw new InvalidOperationException("A captura de audio ja esta ativa.");
        cancellationToken.ThrowIfCancellationRequested();

        int muteMilliseconds = Math.Max(0, (int)initialMute.TotalMilliseconds);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _buffer.Dispose();
            _buffer = new MemoryStream();
            _muteBytesLeft = muteMilliseconds * BytesPerMillisecond;
            _dynamicMuteDeadline = Environment.TickCount64
                + muteMilliseconds
                + MaximumExtraMuteMilliseconds;

            var waveIn = new WaveInEvent
            {
                DeviceNumber = AudioDevices.Resolve(deviceName),
                WaveFormat = new WaveFormat(IAudioCapture.RequiredSampleRate, 16, 1),
                BufferMilliseconds = 30,
            };
            waveIn.DataAvailable += OnData;
            waveIn.RecordingStopped += OnStopped;
            _waveIn = waveIn;

            try
            {
                waveIn.StartRecording();
                Volatile.Write(ref _capturing, 1);
            }
            catch
            {
                waveIn.DataAvailable -= OnData;
                waveIn.RecordingStopped -= OnStopped;
                waveIn.Dispose();
                _waveIn = null;
                throw;
            }
        }, cancellationToken);
    }

    public async Task<float[]> StopAsync(CancellationToken cancellationToken = default)
    {
        var waveIn = _waveIn;
        if (!IsCapturing || waveIn == null) return Array.Empty<float>();

        var completion = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopCompletion = completion;
        Volatile.Write(ref _capturing, 0);

        await Task.Run(waveIn.StopRecording, CancellationToken.None);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    private void OnData(object? sender, WaveInEventArgs eventArgs)
    {
        int offset = 0;
        int count = eventArgs.BytesRecorded;
        if (_muteBytesLeft > 0)
        {
            int skipped = Math.Min(_muteBytesLeft, count);
            _muteBytesLeft -= skipped;
            offset = skipped;
            count -= skipped;
        }

        if (count <= 0 || StillMuted()) return;

        _buffer.Write(eventArgs.Buffer, offset, count);
        var frame = new float[count / 2];
        for (int i = 0; i < frame.Length; i++)
            frame[i] = BitConverter.ToInt16(eventArgs.Buffer, offset + i * 2) / 32768f;
        FrameCaptured?.Invoke(frame);
    }

    private bool StillMuted()
        => Environment.TickCount64 < _dynamicMuteDeadline && _shell.IsSoundActive(SoundGuard);

    private void OnStopped(object? sender, StoppedEventArgs eventArgs)
    {
        Volatile.Write(ref _capturing, 0);
        var bytes = _buffer.ToArray();
        var samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;

        var waveIn = _waveIn;
        _waveIn = null;
        if (waveIn != null)
        {
            waveIn.DataAvailable -= OnData;
            waveIn.RecordingStopped -= OnStopped;
            waveIn.Dispose();
        }

        if (eventArgs.Exception != null)
            Logger.Error("Erro ao gravar audio", eventArgs.Exception);
        _stopCompletion?.TrySetResult(samples);
    }

    public void Dispose()
    {
        if (IsCapturing)
        {
            try { StopAsync().GetAwaiter().GetResult(); }
            catch { }
        }
        _waveIn?.Dispose();
        _buffer.Dispose();
    }
}
