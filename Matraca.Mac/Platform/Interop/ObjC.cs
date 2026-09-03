using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class ObjC
{
    private const string Library = "/usr/lib/libobjc.A.dylib";

    [DllImport(Library, CharSet = CharSet.Ansi)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    public static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [DllImport(Library)]
    public static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(Library, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, string types);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);

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
    public static extern nuint SendNUInt(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendBool(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendBoolNInt(IntPtr receiver, IntPtr selector, nint value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendNUInt(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendWithBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoidBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoidNInt(IntPtr receiver, IntPtr selector, nint value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoidNUInt(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendColor(
        IntPtr receiver,
        IntPtr selector,
        double red,
        double green,
        double blue,
        double alpha);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern CGRect SendRect(IntPtr receiver, IntPtr selector);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendInitWindow(
        IntPtr receiver,
        IntPtr selector,
        CGRect frame,
        nuint styleMask,
        nuint backing,
        [MarshalAs(UnmanagedType.I1)] bool defer);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoidRectBool(
        IntPtr receiver,
        IntPtr selector,
        CGRect frame,
        [MarshalAs(UnmanagedType.I1)] bool display);

    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendPerformOnMain(
        IntPtr receiver,
        IntPtr selector,
        IntPtr selectorToRun,
        IntPtr argument,
        [MarshalAs(UnmanagedType.I1)] bool waitUntilDone);

    public static IntPtr New(IntPtr cls)
        => Send(Send(cls, ObjCSelectors.Alloc), ObjCSelectors.Init);
}
