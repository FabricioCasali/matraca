using Xunit;

namespace Matraca.Core.Tests;

public sealed class VoiceActivityDetectorTests
{
    private const float Threshold = 0.1f;

    [Fact]
    public void SilenceProducesNoSegment()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);

        FeedFrames(detector, 100, 0f);
        detector.Stop();

        Assert.Empty(segments);
    }

    [Fact]
    public void ThresholdComparisonIsStrictlyGreaterThan()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);

        FeedFrames(detector, 9, Threshold);
        detector.Stop();
        Assert.Empty(segments);

        detector.Start(Threshold, 450);
        FeedFrames(detector, 9, MathF.BitIncrement(Threshold));
        detector.Stop();
        Assert.Single(segments);
    }

    [Fact]
    public void SpeechStartsWithOnlyTheLastFiveSilentFrames()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);
        for (int i = 1; i <= 7; i++)
            detector.Feed(Frame(i / 100f));

        FeedFrames(detector, 9, 0.2f);
        detector.Stop();

        float[] segment = Assert.Single(segments);
        Assert.Equal(14 * VoiceActivityDetector.FrameSampleCount, segment.Length);
        for (int retained = 0; retained < 5; retained++)
        {
            float expected = (retained + 3) / 100f;
            Assert.All(segment.AsSpan(
                    retained * VoiceActivityDetector.FrameSampleCount,
                    VoiceActivityDetector.FrameSampleCount).ToArray(),
                sample => Assert.Equal(expected, sample));
        }
    }

    [Fact]
    public void ConfiguredPauseFinalizesAtTheFirstFullFrameThatReachesIt()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments, silenceMilliseconds: 450);
        FeedFrames(detector, 9, 0.2f);

        FeedFrames(detector, 14, 0f);
        Assert.Empty(segments);
        detector.Feed(Frame(0f));

        float[] segment = Assert.Single(segments);
        Assert.Equal(24 * VoiceActivityDetector.FrameSampleCount, segment.Length);
    }

    [Fact]
    public void LongPhraseUsesTheSoftTwoHundredFiftyMillisecondCut()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments, silenceMilliseconds: 900, phraseMaxSeconds: 2);
        FeedFrames(detector, 67, 0.2f);

        FeedFrames(detector, 8, 0f);
        Assert.Empty(segments);
        detector.Feed(Frame(0f));

        float[] segment = Assert.Single(segments);
        Assert.Equal(76 * VoiceActivityDetector.FrameSampleCount, segment.Length);
    }

    [Fact]
    public void ContinuousSpeechHardCutsOnTheFrameThatCrossesTwentySeconds()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);

        FeedFrames(detector, 666, 0.2f);
        Assert.Empty(segments);
        detector.Feed(Frame(0.2f));

        float[] segment = Assert.Single(segments);
        Assert.Equal(667 * VoiceActivityDetector.FrameSampleCount, segment.Length);
    }

    [Fact]
    public void BlipsAtOrBelowTwoHundredFiftyMillisecondsAreDiscarded()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);
        FeedFrames(detector, 8, 0.2f);
        detector.Feed(Enumerable.Repeat(0.2f, 160).ToArray());

        detector.Stop();

        Assert.Empty(segments);
    }

    [Fact]
    public void PreRollAndTrailingSilenceDoNotTurnABlipIntoSpeech()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);
        FeedFrames(detector, 5, 0f);
        detector.Feed(Frame(0.2f));
        FeedFrames(detector, 15, 0f);

        detector.Stop();

        Assert.Empty(segments);
    }

    [Fact]
    public void SegmentLongerThanTwoHundredFiftyMillisecondsIsEmitted()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);
        FeedFrames(detector, 8, 0.2f);
        detector.Feed(Enumerable.Repeat(0.2f, 161).ToArray());

        detector.Stop();

        Assert.Equal(4001, Assert.Single(segments).Length);
    }

    [Fact]
    public void FlushEmitsPendingSpeechOnceAndKeepsTheSessionRunning()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);
        FeedFrames(detector, 9, 0.2f);

        detector.Flush();
        detector.Flush();

        Assert.True(detector.IsRunning);
        Assert.Single(segments);
        FeedFrames(detector, 9, 0.3f);
        detector.Stop();
        Assert.Equal(2, segments.Count);
        Assert.False(detector.IsRunning);
    }

    [Fact]
    public void HardCutAndStopPreserveContinuousInputWithoutLossOrDuplication()
    {
        var segments = new List<float[]>();
        var detector = StartDetector(segments);
        var expected = new List<float>();

        for (int i = 0; i < 700; i++)
        {
            float[] frame = Frame(0.2f + i / 10000f);
            expected.AddRange(frame);
            detector.Feed(frame);
        }
        detector.Stop();

        Assert.Equal(2, segments.Count);
        Assert.Equal(expected.ToArray(), segments.SelectMany(segment => segment).ToArray());
    }

    [Fact]
    public void LifecycleRejectsFeedsOutsideAStartedSessionAndDoubleStart()
    {
        var detector = new VoiceActivityDetector();
        Assert.Throws<InvalidOperationException>(() => detector.Feed(Frame(0.2f)));

        detector.Start(Threshold, 450);
        Assert.Throws<InvalidOperationException>(() => detector.Start(Threshold, 450));
        detector.Stop();
        detector.Stop();
        Assert.Throws<InvalidOperationException>(() => detector.Feed(Frame(0.2f)));
    }

    private static VoiceActivityDetector StartDetector(
        List<float[]> segments,
        int silenceMilliseconds = 450,
        int phraseMaxSeconds = 6)
    {
        var detector = new VoiceActivityDetector();
        detector.SegmentReady += segments.Add;
        detector.Start(Threshold, silenceMilliseconds, phraseMaxSeconds);
        return detector;
    }

    private static void FeedFrames(VoiceActivityDetector detector, int count, float value)
    {
        for (int i = 0; i < count; i++)
            detector.Feed(Frame(value));
    }

    private static float[] Frame(float value)
        => Enumerable.Repeat(value, VoiceActivityDetector.FrameSampleCount).ToArray();
}
