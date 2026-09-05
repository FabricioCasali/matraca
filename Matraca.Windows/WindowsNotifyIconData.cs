using System.Runtime.InteropServices;

namespace Matraca;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WindowsNotifyIconData
{
    public uint Size;
    public nint Window;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public nint Icon;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Tip;

    public uint State;
    public uint StateMask;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Info;

    public uint VersionOrTimeout;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string InfoTitle;

    public uint InfoFlags;
    public Guid ItemGuid;
    public nint BalloonIcon;
}
