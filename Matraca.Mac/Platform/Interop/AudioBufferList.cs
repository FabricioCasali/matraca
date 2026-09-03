using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct AudioBufferList
{
    public uint NumberBuffers;
    public AudioBuffer Buffer;
}
