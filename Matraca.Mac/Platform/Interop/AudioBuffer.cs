using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct AudioBuffer
{
    public uint NumberChannels;
    public uint DataByteSize;
    public IntPtr Data;
}
