namespace Matraca.Core;

public sealed class VoiceActivityDetector
{
    public const int SampleRate = 16000;
    public const int FrameMilliseconds = 30;
    public const int FrameSampleCount = SampleRate * FrameMilliseconds / 1000;

    private const int PreRollFrameCount = 5;
    private const int SoftCutSilenceMilliseconds = 250;
    private const int HardCutSeconds = 20;
    private const int MinimumSegmentSamples = SampleRate / 4;

    private readonly List<float> _segment = new();
    private readonly Queue<float[]> _preRoll = new();

    private float _threshold;
    private int _silenceMilliseconds;
    private int _phraseMaxSeconds;
    private int _silenceRunMilliseconds;
    private int _speechSampleCount;
    private bool _speechActive;

    public event Action<float[]>? SegmentReady;

    public bool IsRunning { get; private set; }

    public void Start(float threshold, int silenceMilliseconds, int phraseMaxSeconds = 6)
    {
        if (IsRunning)
            throw new InvalidOperationException("The voice activity detector is already running.");

        _threshold = threshold <= 0 ? 0.012f : threshold;
        _silenceMilliseconds = silenceMilliseconds <= 0 ? 450 : silenceMilliseconds;
        _phraseMaxSeconds = phraseMaxSeconds <= 0 ? 6 : phraseMaxSeconds;
        ResetBuffers();
        IsRunning = true;
    }

    public void Feed(ReadOnlySpan<float> frame)
    {
        if (!IsRunning)
            throw new InvalidOperationException("The voice activity detector is not running.");
        if (frame.IsEmpty) return;

        bool isSpeech = AudioLevelAnalyzer.CalculateRms(frame) > _threshold;
        int frameMilliseconds = (int)(1000.0 * frame.Length / SampleRate);

        if (isSpeech)
        {
            if (!_speechActive)
            {
                _speechActive = true;
                foreach (float[] preRollFrame in _preRoll)
                    _segment.AddRange(preRollFrame);
                _preRoll.Clear();
            }

            _segment.AddRange(frame);
            _speechSampleCount += frame.Length;
            _silenceRunMilliseconds = 0;

            if (_segment.Count >= SampleRate * HardCutSeconds)
                FinalizeSegment();
        }
        else if (_speechActive)
        {
            _segment.AddRange(frame);
            _silenceRunMilliseconds += frameMilliseconds;
            if (_silenceRunMilliseconds >= EffectiveSilenceMilliseconds())
                FinalizeSegment();
        }
        else
        {
            _preRoll.Enqueue(frame.ToArray());
            while (_preRoll.Count > PreRollFrameCount)
                _preRoll.Dequeue();
        }
    }

    public void Flush()
    {
        if (!IsRunning) return;

        if (_speechActive)
            FinalizeSegment();
        _preRoll.Clear();
    }

    public void Stop()
    {
        if (!IsRunning) return;

        IsRunning = false;
        if (_speechActive)
            FinalizeSegment();
        ResetBuffers();
    }

    private int EffectiveSilenceMilliseconds()
    {
        double seconds = _segment.Count / (double)SampleRate;
        return seconds >= _phraseMaxSeconds
            ? Math.Min(_silenceMilliseconds, SoftCutSilenceMilliseconds)
            : _silenceMilliseconds;
    }

    private void FinalizeSegment()
    {
        _speechActive = false;
        _silenceRunMilliseconds = 0;
        float[]? ready = _speechSampleCount > MinimumSegmentSamples ? _segment.ToArray() : null;
        _segment.Clear();
        _speechSampleCount = 0;
        if (ready != null)
            SegmentReady?.Invoke(ready);
    }

    private void ResetBuffers()
    {
        _segment.Clear();
        _preRoll.Clear();
        _speechActive = false;
        _silenceRunMilliseconds = 0;
        _speechSampleCount = 0;
    }
}
