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
    private const int PreRollFrames = 5;     // ~150ms de áudio antes do início da fala
    private const int MaxSegSeconds = 20;    // corte duro: limite do Whisper (~30s)

    /// <summary>
    /// Pausa aceita como fim de frase depois que a fala já passou de <see cref="_phraseMaxSeconds"/>.
    /// Quem fala emendado quase nunca faz a pausa cheia de silenceMs, e sem isto o texto só saía
    /// no corte duro de 20s — chegando todo de uma vez e com atraso enorme.
    /// </summary>
    private const int SoftCutSilenceMs = 250;

    /// <summary>
    /// Teto do mute dinamico (ver <see cref="StillMuted"/>). Cobre so' a latencia ate' o som
    /// sair de fato; acompanhar um som longo comeria fala do usuario.
    /// </summary>
    private const int MaxExtraMuteMs = 400;

    private WaveInEvent? _waveIn;
    private readonly List<float> _segment = new();
    private readonly Queue<float[]> _preRoll = new();

    private float _threshold;
    private int _silenceMs;
    private int _phraseMaxSeconds;
    private int _silenceRun;
    private bool _speechActive;
    private int _muteSamplesLeft;   // audio a descartar no inicio (o proprio bip de start)
    private long _dynamicMuteDeadline; // ate' quando o mute pode se estender pelo bip real

    public event Action<float[]>? SegmentReady;
    public bool IsRunning { get; private set; }

    /// <param name="muteMs">
    /// Descarta os primeiros N ms capturados — o bip de inicio sai pelo alto-falante e volta
    /// pelo microfone; sem isso o VAD o trata como fala e o Whisper o transcreve como palavra.
    /// </param>
    public void Start(float threshold, int silenceMs, int phraseMaxSeconds = 6, int muteMs = 0,
                      int deviceNumber = AudioDevices.DefaultDevice)
    {
        _threshold = threshold <= 0 ? 0.012f : threshold;
        _silenceMs = silenceMs <= 0 ? 450 : silenceMs;
        _phraseMaxSeconds = phraseMaxSeconds <= 0 ? 6 : phraseMaxSeconds;
        _segment.Clear();
        _preRoll.Clear();
        _speechActive = false;
        _silenceRun = 0;
        _muteSamplesLeft = Math.Max(0, muteMs) * SampleRate / 1000;
        _dynamicMuteDeadline = Environment.TickCount64 + Math.Max(0, muteMs) + MaxExtraMuteMs;

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
        Feed(frame);
    }

    private bool StillMuted()
        => Environment.TickCount64 < _dynamicMuteDeadline && Beeper.InBeepShadow(Beeper.GuardMs);

    // Núcleo do VAD (um frame por vez). Reutilizado pelo mic e pelo teste por arquivo.
    private void Feed(float[] frame)
    {
        int n = frame.Length;
        if (n == 0) return;
        double sumSq = 0;
        for (int i = 0; i < n; i++) sumSq += frame[i] * frame[i];
        float rms = (float)Math.Sqrt(sumSq / n);
        int frameMs = (int)(1000.0 * n / SampleRate);
        bool isSpeech = rms > _threshold;

        if (isSpeech)
        {
            if (!_speechActive)
            {
                _speechActive = true;
                foreach (var pr in _preRoll) _segment.AddRange(pr);
                _preRoll.Clear();
            }
            _segment.AddRange(frame);
            _silenceRun = 0;

            if (_segment.Count >= SampleRate * MaxSegSeconds)
                FinalizeSegment();
        }
        else if (_speechActive)
        {
            _segment.AddRange(frame);   // mantém a cauda de silêncio
            _silenceRun += frameMs;
            if (_silenceRun >= EffectiveSilenceMs())
                FinalizeSegment();
        }
        else
        {
            _preRoll.Enqueue(frame);    // ring de pré-roll enquanto em silêncio
            while (_preRoll.Count > PreRollFrames) _preRoll.Dequeue();
        }
    }

    /// <summary>
    /// Quanto silêncio encerra a frase agora. Enquanto a fala é curta, exige a pausa cheia
    /// (silenceMs) — assim não pica a frase no meio. Passando de phraseMaxSeconds, passa a
    /// aceitar uma respirada curta, entregando o texto em pedaços menores e mais rápido.
    /// </summary>
    private int EffectiveSilenceMs()
    {
        double secs = _segment.Count / (double)SampleRate;
        return secs >= _phraseMaxSeconds ? Math.Min(_silenceMs, SoftCutSilenceMs) : _silenceMs;
    }

    private void FinalizeSegment()
    {
        _speechActive = false;
        _silenceRun = 0;
        if (_segment.Count > SampleRate / 4)   // ignora blips < 0,25s (ruído)
            SegmentReady?.Invoke(_segment.ToArray());
        _segment.Clear();
    }

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
        if (_speechActive && _segment.Count > SampleRate / 4)
            SegmentReady?.Invoke(_segment.ToArray());
        _segment.Clear();
        _speechActive = false;
    }

    /// <summary>Teste offline: alimenta o VAD com amostras de um arquivo, em frames de 30ms.</summary>
    public void FeedForTest(float[] all, float threshold, int silenceMs, int phraseMaxSeconds = 6)
    {
        _threshold = threshold <= 0 ? 0.012f : threshold;
        _silenceMs = silenceMs <= 0 ? 450 : silenceMs;
        _phraseMaxSeconds = phraseMaxSeconds <= 0 ? 6 : phraseMaxSeconds;
        _segment.Clear(); _preRoll.Clear(); _speechActive = false; _silenceRun = 0;

        int frameSize = SampleRate * FrameMs / 1000; // 480 amostras
        for (int off = 0; off < all.Length; off += frameSize)
        {
            int len = Math.Min(frameSize, all.Length - off);
            var frame = new float[len];
            Array.Copy(all, off, frame, 0, len);
            Feed(frame);
        }
        if (_speechActive && _segment.Count > SampleRate / 4)
            SegmentReady?.Invoke(_segment.ToArray());
    }

    public void Dispose() => _waveIn?.Dispose();
}
