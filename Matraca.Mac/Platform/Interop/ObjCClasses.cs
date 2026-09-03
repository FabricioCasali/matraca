namespace Matraca.Mac.Platform.Interop;

internal static class ObjCClasses
{
    static ObjCClasses() => Frameworks.EnsureLoaded();

    public static readonly IntPtr NSString = Get("NSString");
    public static readonly IntPtr NSNumber = Get("NSNumber");
    public static readonly IntPtr NSDictionary = Get("NSDictionary");
    public static readonly IntPtr NSObject = Get("NSObject");
    public static readonly IntPtr NSAutoreleasePool = Get("NSAutoreleasePool");
    public static readonly IntPtr NSApplication = Get("NSApplication");
    public static readonly IntPtr NSStatusBar = Get("NSStatusBar");
    public static readonly IntPtr NSMenu = Get("NSMenu");
    public static readonly IntPtr NSMenuItem = Get("NSMenuItem");
    public static readonly IntPtr NSColor = Get("NSColor");
    public static readonly IntPtr NSScreen = Get("NSScreen");
    public static readonly IntPtr NSThread = Get("NSThread");
    public static readonly IntPtr NSWindow = Get("NSWindow");
    public static readonly IntPtr NSRunningApplication = Get("NSRunningApplication");

    public static void Warm() => _ = NSApplication;

    private static IntPtr Get(string name)
    {
        IntPtr cls = ObjC.objc_getClass(name);
        return cls != IntPtr.Zero
            ? cls
            : throw new TypeLoadException($"Objective-C class not found: {name}");
    }
}
