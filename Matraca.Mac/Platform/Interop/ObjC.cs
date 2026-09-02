using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class ObjC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";

    [DllImport(Library, CharSet = CharSet.Ansi)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second,
        IntPtr third);

    [DllImport(Library, EntryPoint = "objc_msgSend", CharSet = CharSet.Ansi)]
    public static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, byte[] utf8);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendDouble(IntPtr receiver, IntPtr selector, double value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern nint SendNInt(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendBoolNInt(IntPtr receiver, IntPtr selector, nint value);

    public static IntPtr New(IntPtr cls)
        => Send(Send(cls, ObjCSelectors.Alloc), ObjCSelectors.Init);
}
