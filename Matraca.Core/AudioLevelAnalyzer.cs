namespace Matraca.Core;

public static class AudioLevelAnalyzer
{
    public static float CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return 0f;

        double sumSquares = 0;
        foreach (float sample in samples)
            sumSquares += (double)sample * sample;

        return (float)Math.Sqrt(sumSquares / samples.Length);
    }
}
