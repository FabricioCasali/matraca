using NAudio.Wave;

namespace Matraca;

/// <summary>Captura do microfone padrao em 16 kHz mono PCM16 (formato nativo do Whisper).</summary>
internal sealed class AudioRecorder : IDisposable
{
    private const int SampleRate = 16000;
    private const int BytesPerMs = SampleRate * 2 / 1000;   // PCM16 mono

    private WaveInEvent? _waveIn;
    private MemoryStream _buffer = new();
    private TaskCompletionSource<float[]>? _stopTcs;
    private int _muteBytesLeft;   // audio a descartar no inicio (o proprio bip de start)

    public bool IsRecording { get; private set; }

    /// <param name="muteMs">
    /// Descarta os primeiros N ms capturados. Serve p/ jogar fora o bip de inicio, que sai
    /// pelo alto-falante e volta pelo microfone — sem isso o Whisper o transcreve como palavra.
    /// </param>
    public void Start(int muteMs = 0)
    {
        _buffer = new MemoryStream();
        _muteBytesLeft = Math.Max(0, muteMs) * BytesPerMs;
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
    {
        int offset = 0, count = e.BytesRecorded;
        if (_muteBytesLeft > 0)
        {
            int skip = Math.Min(_muteBytesLeft, count);   // multiplo de 2: mantem o alinhamento PCM16
            _muteBytesLeft -= skip;
            offset = skip;
            count -= skip;
        }
        if (count > 0) _buffer.Write(e.Buffer, offset, count);
    }

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
