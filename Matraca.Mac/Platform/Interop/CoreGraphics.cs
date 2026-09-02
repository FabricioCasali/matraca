using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static unsafe class CoreGraphics
{
    private const string CoreGraphicsFramework =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationFramework =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const uint SessionEventTap = 1;
    public const uint HeadInsertEventTap = 0;
    public const uint DefaultEventTap = 0;
    public const uint KeyDown = 10;
    public const uint KeyUp = 11;
    public const uint TapDisabledByTimeout = 0xFFFFFFFE;
    public const uint TapDisabledByUserInput = 0xFFFFFFFF;
    public const uint KeyboardEventAutorepeat = 8;
    public const uint KeyboardEventKeycode = 9;
    public const uint EventSourceUserData = 42;
    public const ulong KeyDownUpMask = (1UL << (int)KeyDown) | (1UL << (int)KeyUp);

    [DllImport(CoreGraphicsFramework)]
    public static extern IntPtr CGEventTapCreate(
        uint tap,
        uint place,
        uint options,
        ulong eventsOfInterest,
        delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> callback,
        IntPtr userInfo);

    [DllImport(CoreGraphicsFramework)]
    public static extern void CGEventTapEnable(
        IntPtr tap,
        [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(CoreGraphicsFramework)]
    public static extern long CGEventGetIntegerValueField(IntPtr @event, uint field);

    [DllImport(CoreGraphicsFramework)]
    public static extern ulong CGEventGetFlags(IntPtr @event);

    [DllImport(CoreFoundationFramework)]
    public static extern IntPtr CFMachPortCreateRunLoopSource(
        IntPtr allocator,
        IntPtr port,
        nint order);

    [DllImport(CoreFoundationFramework)]
    public static extern IntPtr CFRunLoopGetMain();

    [DllImport(CoreFoundationFramework)]
    public static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundationFramework)]
    public static extern void CFRunLoopRemoveSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundationFramework)]
    public static extern void CFRelease(IntPtr value);

    public static IntPtr CommonModes
        => Frameworks.Symbol(Frameworks.CoreFoundationHandle, "kCFRunLoopCommonModes");
}
