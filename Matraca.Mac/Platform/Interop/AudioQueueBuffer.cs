using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct AudioQueueBuffer
{
    public uint AudioDataBytesCapacity;
    public IntPtr AudioData;
    public uint AudioDataByteSize;
    public IntPtr UserData;
    public uint PacketDescriptionCapacity;
    public IntPtr PacketDescriptions;
    public uint PacketDescriptionCount;
}
