using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsMonitorInfo
{
    public uint Size;
    public WindowsRectangle Monitor;
    public WindowsRectangle WorkArea;
    public uint Flags;
}
