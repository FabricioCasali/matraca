using Xunit;

namespace Matraca.Core.Tests;

public sealed class AudioLevelAnalyzerTests
{
    [Fact]
    public void EmptyInputHasZeroLevel()
    {
        Assert.Equal(0f, AudioLevelAnalyzer.CalculateRms(ReadOnlySpan<float>.Empty));
    }

    [Fact]
    public void ConstantSignalHasItsAbsoluteAmplitudeAsRms()
    {
        float[] samples = Enumerable.Repeat(-0.25f, 480).ToArray();

        Assert.Equal(0.25f, AudioLevelAnalyzer.CalculateRms(samples), 6);
    }

    [Fact]
    public void FullScaleSineHasExpectedRms()
    {
        float[] samples = Enumerable.Range(0, 16000)
            .Select(i => (float)Math.Sin(2 * Math.PI * 1000 * i / 16000))
            .ToArray();

        Assert.InRange(AudioLevelAnalyzer.CalculateRms(samples), 0.7070f, 0.7072f);
    }
}
