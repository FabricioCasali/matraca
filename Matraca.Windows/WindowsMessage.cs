using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsMessage
{
    public nint Window;
    public uint Id;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public WindowsPoint Point;
    public uint Private;
}
