using System.Numerics;

namespace Matraca.Core;

public sealed class SpectrumAnalyzer
{
    public const int BandCount = 48;

    private const int SampleRate = 16000;
    private const int FftSize = 2048;
    private const float MinimumFrequency = 50f;
    private const float MaximumCenterFrequency = 7600f;
    private const float NyquistFrequency = SampleRate / 2f;

    private readonly float[] _centerFrequencies = BuildCenterFrequencies();
    private readonly float[] _bandEdges = BuildBandEdges();

    public ReadOnlyMemory<float> CenterFrequencies => _centerFrequencies;

    public float[] Analyze(ReadOnlySpan<float> samples)
    {
        var bands = new float[BandCount];
        if (samples.IsEmpty) return bands;

        int sampleCount = Math.Min(samples.Length, FftSize);
        int sourceOffset = samples.Length - sampleCount;
        var spectrum = new Complex[FftSize];
        double windowSum = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            double window = sampleCount == 1
                ? 1.0
                : 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (sampleCount - 1));
            float sample = samples[sourceOffset + i];
            if (!float.IsFinite(sample)) sample = 0f;
            spectrum[i] = new Complex(sample * window, 0);
            windowSum += window;
        }

        Transform(spectrum);
        if (windowSum <= 0) return bands;

        for (int band = 0; band < BandCount; band++)
        {
            int firstBin = Math.Max(1, (int)Math.Ceiling(_bandEdges[band] * FftSize / SampleRate));
            int lastBin = Math.Min(FftSize / 2,
                (int)Math.Floor(_bandEdges[band + 1] * FftSize / SampleRate));
            if (lastBin < firstBin)
            {
                int nearest = Math.Clamp(
                    (int)Math.Round(_centerFrequencies[band] * FftSize / SampleRate),
                    1,
                    FftSize / 2);
                firstBin = nearest;
                lastBin = nearest;
            }

            double maximum = 0;
            for (int bin = firstBin; bin <= lastBin; bin++)
                maximum = Math.Max(maximum, 2.0 * spectrum[bin].Magnitude / windowSum);

            bands[band] = double.IsFinite(maximum) ? (float)maximum : 0f;
        }

        return bands;
    }

    private static float[] BuildCenterFrequencies()
    {
        var frequencies = new float[BandCount];
        double ratio = Math.Pow(MaximumCenterFrequency / MinimumFrequency, 1.0 / (BandCount - 1));
        for (int i = 0; i < frequencies.Length; i++)
            frequencies[i] = (float)(MinimumFrequency * Math.Pow(ratio, i));
        return frequencies;
    }

    private static float[] BuildBandEdges()
    {
        float[] centers = BuildCenterFrequencies();
        var edges = new float[BandCount + 1];
        edges[0] = MinimumFrequency;
        for (int i = 1; i < BandCount; i++)
            edges[i] = (float)Math.Sqrt(centers[i - 1] * centers[i]);
        edges[BandCount] = NyquistFrequency;
        return edges;
    }

    private static void Transform(Complex[] values)
    {
        int j = 0;
        for (int i = 1; i < values.Length; i++)
        {
            int bit = values.Length >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j ^= bit;
            if (i < j)
                (values[i], values[j]) = (values[j], values[i]);
        }

        for (int length = 2; length <= values.Length; length <<= 1)
        {
            Complex step = Complex.FromPolarCoordinates(1, -2.0 * Math.PI / length);
            for (int start = 0; start < values.Length; start += length)
            {
                Complex factor = Complex.One;
                int half = length / 2;
                for (int offset = 0; offset < half; offset++)
                {
                    Complex even = values[start + offset];
                    Complex odd = values[start + offset + half] * factor;
                    values[start + offset] = even + odd;
                    values[start + offset + half] = even - odd;
                    factor *= step;
                }
            }
        }
    }
}
