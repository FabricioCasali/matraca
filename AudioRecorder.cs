using NAudio.Wave;

namespace Matraca;

/// <summary>Captura do microfone padrao em 16 kHz mono PCM16 (formato nativo do Whisper).</summary>
internal sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream _buffer = new();
    private TaskCompletionSource<float[]>? _stopTcs;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        _buffer = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += OnData;
        _waveIn.RecordingStopped += OnStopped;
        _waveIn.StartRecording();
        IsRecording = true;
    }

    /// <summary>Para a gravacao e devolve as amostras como float[] normalizado [-1,1].</summary>
    public Task<float[]> StopAsync()
    {
        if (!IsRecording || _waveIn == null)
            return Task.FromResult(Array.Empty<float>());

        _stopTcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsRecording = false;
        _waveIn.StopRecording(); // dispara RecordingStopped -> OnStopped
        return _stopTcs.Task;
    }

    private void OnData(object? sender, WaveInEventArgs e)
        => _buffer.Write(e.Buffer, 0, e.BytesRecorded);

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        var bytes = _buffer.ToArray();
        var samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;

        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnData;
            _waveIn.RecordingStopped -= OnStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        if (e.Exception != null) Logger.Error("Erro ao gravar audio", e.Exception);
        _stopTcs?.TrySetResult(samples);
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _buffer.Dispose();
    }
}
