using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsPoint
{
    public int X;
    public int Y;
}
