using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WindowsWindowClass
{
    public uint Size;
    public uint Style;
    public WindowsWindowProcedure WindowProcedure;
    public int ClassExtraBytes;
    public int WindowExtraBytes;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint BackgroundBrush;
    public nint MenuName;

    [MarshalAs(UnmanagedType.LPWStr)]
    public string ClassName;

    public nint SmallIcon;
}
