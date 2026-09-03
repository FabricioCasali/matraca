using System.Buffers.Binary;
using System.Text;
using Matraca.Mac.Platform.Audio;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class MacSoundWaveTests
{
    [Theory]
    [InlineData(true, 220)]
    [InlineData(false, 240)]
    public void CreateBuildsPcmWaveWithExpectedDuration(bool start, int expectedMilliseconds)
    {
        byte[] wave = MacSoundWave.Create(start);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(22, 2)));
        Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24, 4)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34, 2)));

        int dataBytes = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40, 4));
        Assert.Equal(wave.Length - 44, dataBytes);
        Assert.Equal(expectedMilliseconds, dataBytes / sizeof(short) * 1000 / 44100);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44, 2)));
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(wave.Length - 2, 2)));
    }

    [Fact]
    public void AudibleFrameCountTrimsSilenceAndPreservesDecayTail()
    {
        var pcm = new byte[200 * sizeof(short)];
        BitConverter.TryWriteBytes(pcm.AsSpan(49 * sizeof(short), sizeof(short)), (short)1000);

        int frames = MacSoundWave.AudibleFrameCount(pcm, channels: 1, sampleRate: 1000);
        byte[] wave = MacSoundWave.CreateFromPcm16(pcm, frames, 1000, 1);

        Assert.Equal(80, frames);
        Assert.Equal(80 * sizeof(short), wave.Length - 44);
    }
}
