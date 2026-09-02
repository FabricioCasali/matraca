using NAudio.Wave;

namespace Matraca;

/// <summary>
/// Captura contínua do microfone com VAD (detecção de fala por energia).
/// Emite SegmentReady a cada frase finalizada (quando há uma pausa &gt;= silenceMs).
/// Permite ditado "ao vivo": as frases vão sendo transcritas/coladas conforme você fala.
/// </summary>
internal sealed class LiveDictation : IDisposable
{
    private const int SampleRate = 16000;
    private const int FrameMs = 30;          // tamanho do buffer NAudio

    /// <summary>
    /// Teto do mute dinamico (ver <see cref="StillMuted"/>). Cobre so' a latencia ate' o som
    /// sair de fato; acompanhar um som longo comeria fala do usuario.
    /// </summary>
    private const int MaxExtraMuteMs = 400;

    private readonly VoiceActivityDetector _detector = new();
    private WaveInEvent? _waveIn;
    private int _muteSamplesLeft;   // audio a descartar no inicio (o proprio bip de start)
    private long _dynamicMuteDeadline; // ate' quando o mute pode se estender pelo bip real

    public event Action<float[]>? SegmentReady
    {
        add => _detector.SegmentReady += value;
        remove => _detector.SegmentReady -= value;
    }

    public bool IsRunning { get; private set; }

    /// <param name="muteMs">
    /// Descarta os primeiros N ms capturados — o bip de inicio sai pelo alto-falante e volta
    /// pelo microfone; sem isso o VAD o trata como fala e o Whisper o transcreve como palavra.
    /// </param>
    public void Start(float threshold, int silenceMs, int phraseMaxSeconds = 6, int muteMs = 0,
                      int deviceNumber = AudioDevices.DefaultDevice)
    {
        _detector.Start(threshold, silenceMs, phraseMaxSeconds);
        _muteSamplesLeft = Math.Max(0, muteMs) * SampleRate / 1000;
        _dynamicMuteDeadline = Environment.TickCount64 + Math.Max(0, muteMs) + MaxExtraMuteMs;

        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = FrameMs,
            };
            _waveIn.DataAvailable += OnData;
            _waveIn.StartRecording();
            IsRunning = true;
        }
        catch
        {
            _waveIn?.Dispose();
            _waveIn = null;
            _detector.Stop();
            throw;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int n = e.BytesRecorded / 2;
        if (n == 0) return;

        int skip = 0;
        if (_muteSamplesLeft > 0)
        {
            skip = Math.Min(_muteSamplesLeft, n);
            _muteSamplesLeft -= skip;
            n -= skip;
            if (n == 0) return;
        }

        // muteMs vem da duracao do som, mas o bip so' comeca a sair depois da decodificacao e do
        // buffer da placa; enquanto ele estiver soando de verdade, segue descartando (o deadline
        // impede que uma reproducao travada mate a captura).
        if (StillMuted()) return;

        var frame = new float[n];
        for (int i = 0; i < n; i++)
            frame[i] = BitConverter.ToInt16(e.Buffer, (i + skip) * 2) / 32768f;
        _detector.Feed(frame);
    }

    private bool StillMuted()
        => Environment.TickCount64 < _dynamicMuteDeadline && Beeper.InBeepShadow(Beeper.GuardMs);

    /// <summary>Para a captura e emite o último segmento pendente (se houver fala).</summary>
    public void Stop()
    {
        IsRunning = false;
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnData;
            try { _waveIn.StopRecording(); } catch { }
            _waveIn.Dispose();
            _waveIn = null;
        }
        _detector.Stop();
    }

    public void Dispose()
    {
        if (IsRunning) Stop();
        else _waveIn?.Dispose();
    }
}
