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
    public const uint HidEventTap = 0;
    public const uint WindowListOptionOnScreenOnly = 1;
    public const uint WindowListExcludeDesktopElements = 1 << 4;
    public const uint NullWindow = 0;
    public const int HidSystemState = 1;
    public const ulong MaskCommand = 1UL << 20;
    public const ushort CommandKey = 0x37;
    public const ushort VKey = 0x09;
    public const ushort ReturnKey = 0x24;
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

    [DllImport(CoreGraphicsFramework)]
    public static extern ulong CGEventSourceFlagsState(int stateId);

    [DllImport(CoreGraphicsFramework)]
    public static extern IntPtr CGEventSourceCreate(int stateId);

    [DllImport(CoreGraphicsFramework)]
    public static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CoreGraphicsFramework)]
    public static extern void CGEventKeyboardSetUnicodeString(
        IntPtr @event,
        nuint stringLength,
        ushort* unicodeString);

    [DllImport(CoreGraphicsFramework)]
    public static extern void CGEventSetIntegerValueField(IntPtr @event, uint field, long value);

    [DllImport(CoreGraphicsFramework)]
    public static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport(CoreGraphicsFramework)]
    public static extern void CGEventPost(uint tap, IntPtr @event);

    [DllImport(CoreGraphicsFramework)]
    public static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGRectMakeWithDictionaryRepresentation(
        IntPtr dictionary,
        out CGRect rectangle);

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

    public static IntPtr WindowOwnerProcessIdKey
        => Frameworks.Symbol(Frameworks.CoreGraphicsHandle, "kCGWindowOwnerPID");

    public static IntPtr WindowIsOnscreenKey
        => Frameworks.Symbol(Frameworks.CoreGraphicsHandle, "kCGWindowIsOnscreen");

    public static IntPtr WindowBoundsKey
        => Frameworks.Symbol(Frameworks.CoreGraphicsHandle, "kCGWindowBounds");

    public static IntPtr WindowNameKey
        => Frameworks.Symbol(Frameworks.CoreGraphicsHandle, "kCGWindowName");
}
