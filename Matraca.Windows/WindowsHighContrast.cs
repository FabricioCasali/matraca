using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsHighContrast
{
    public uint Size;
    public uint Flags;
    public nint DefaultScheme;
}
