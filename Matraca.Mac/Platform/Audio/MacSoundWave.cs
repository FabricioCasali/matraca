using System.Text;

namespace Matraca.Mac.Platform.Audio;

internal static class MacSoundWave
{
    private const int SampleRate = 44100;

    public static byte[] Create(bool start)
    {
        (int Frequency, int Milliseconds)[] notes = start
            ? [(660, 90), (990, 130)]
            : [(990, 90), (590, 150)];
        int sampleCount = notes.Sum(note => SampleRate * note.Milliseconds / 1000);
        var pcm = new byte[sampleCount * sizeof(short)];
        int sampleOffset = 0;

        foreach ((int frequency, int milliseconds) in notes)
        {
            int noteSamples = SampleRate * milliseconds / 1000;
            int fadeSamples = Math.Min(noteSamples / 2, SampleRate * 5 / 1000);
            for (int index = 0; index < noteSamples; index++)
            {
                double envelope = 1;
                if (index < fadeSamples) envelope = (double)index / fadeSamples;
                else if (index >= noteSamples - fadeSamples)
                    envelope = (double)(noteSamples - index - 1) / fadeSamples;
                double sample = Math.Sin(2 * Math.PI * frequency * index / SampleRate) * envelope;
                BitConverter.TryWriteBytes(
                    pcm.AsSpan(sampleOffset, sizeof(short)),
                    (short)(sample * short.MaxValue));
                sampleOffset += sizeof(short);
            }
        }

        return CreateFromPcm16(pcm, sampleCount, SampleRate, 1);
    }

    public static int AudibleFrameCount(
        ReadOnlySpan<byte> pcm,
        int channels,
        int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        int totalFrames = pcm.Length / sizeof(short) / channels;
        if (totalFrames == 0) return 0;

        int peak = 0;
        for (int offset = 0; offset + 1 < pcm.Length; offset += sizeof(short))
        {
            short sample = BitConverter.ToInt16(pcm.Slice(offset, sizeof(short)));
            int absolute = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);
            peak = Math.Max(peak, absolute);
        }
        if (peak == 0) return 0;

        int floor = Math.Max(peak / 100, 16);
        int lastFrame = totalFrames - 1;
        while (lastFrame >= 0)
        {
            bool audible = false;
            for (int channel = 0; channel < channels; channel++)
            {
                int offset = (lastFrame * channels + channel) * sizeof(short);
                short sample = BitConverter.ToInt16(pcm.Slice(offset, sizeof(short)));
                int absolute = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);
                if (absolute < floor) continue;
                audible = true;
                break;
            }
            if (audible) break;
            lastFrame--;
        }
        if (lastFrame < 0) return 0;

        int decayFrames = sampleRate * 30 / 1000;
        return Math.Min(totalFrames, lastFrame + 1 + decayFrames);
    }

    public static byte[] CreateFromPcm16(
        ReadOnlySpan<byte> pcm,
        int frameCount,
        int sampleRate,
        int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        int dataBytes = checked(frameCount * channels * sizeof(short));
        if (dataBytes > pcm.Length)
            throw new ArgumentException("PCM data is shorter than the declared frame count.", nameof(pcm));

        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(checked(sampleRate * channels * sizeof(short)));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        writer.Write(pcm[..dataBytes]);
        writer.Flush();
        return stream.ToArray();
    }
}
