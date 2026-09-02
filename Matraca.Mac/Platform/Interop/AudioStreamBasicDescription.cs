using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct AudioStreamBasicDescription
{
    private const uint FormatLinearPcm = 0x6C70636D;
    private const uint FlagsPcm16 = 0xC;

    public double SampleRate;
    public uint FormatId;
    public uint FormatFlags;
    public uint BytesPerPacket;
    public uint FramesPerPacket;
    public uint BytesPerFrame;
    public uint ChannelsPerFrame;
    public uint BitsPerChannel;
    public uint Reserved;

    public static AudioStreamBasicDescription Pcm16Mono(double sampleRate) => new()
    {
        SampleRate = sampleRate,
        FormatId = FormatLinearPcm,
        FormatFlags = FlagsPcm16,
        BytesPerPacket = 2,
        FramesPerPacket = 1,
        BytesPerFrame = 2,
        ChannelsPerFrame = 1,
        BitsPerChannel = 16,
    };
}
