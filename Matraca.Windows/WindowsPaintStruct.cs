using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsPaintStruct
{
    public nint DeviceContext;
    public int Erase;
    public WindowsRectangle Paint;
    public int Restore;
    public int IncrementalUpdate;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
    public uint Reserved3;
    public uint Reserved4;
    public uint Reserved5;
    public uint Reserved6;
    public uint Reserved7;
}
