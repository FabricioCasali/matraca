using Xunit;

namespace Matraca.Core.Tests;

public sealed class SpectrumAnalyzerTests
{
    [Fact]
    public void SilenceProducesFortyEightFiniteZeroBands()
    {
        var analyzer = new SpectrumAnalyzer();

        float[] bands = analyzer.Analyze(new float[VoiceActivityDetector.FrameSampleCount]);

        Assert.Equal(SpectrumAnalyzer.BandCount, bands.Length);
        Assert.All(bands, value =>
        {
            Assert.True(float.IsFinite(value));
            Assert.Equal(0f, value);
        });
    }

    [Fact]
    public void KnownSinePeaksInTheExpectedLogarithmicBand()
    {
        const float frequency = 1000f;
        float[] samples = Enumerable.Range(0, VoiceActivityDetector.FrameSampleCount)
            .Select(i => (float)Math.Sin(2 * Math.PI * frequency * i / VoiceActivityDetector.SampleRate))
            .ToArray();
        var analyzer = new SpectrumAnalyzer();

        float[] bands = analyzer.Analyze(samples);
        int peakBand = Array.IndexOf(bands, bands.Max());
        float peakFrequency = analyzer.CenterFrequencies.Span[peakBand];

        Assert.InRange(peakFrequency, 900f, 1100f);
        Assert.InRange(bands[peakBand], 0.8f, 1.05f);
        Assert.All(bands, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void NonFiniteSamplesNeverProduceNonFiniteBands()
    {
        float[] samples = Enumerable.Repeat(0.25f, VoiceActivityDetector.FrameSampleCount).ToArray();
        samples[10] = float.NaN;
        samples[20] = float.PositiveInfinity;
        samples[30] = float.NegativeInfinity;

        float[] bands = new SpectrumAnalyzer().Analyze(samples);

        Assert.All(bands, value => Assert.True(float.IsFinite(value)));
    }
}
