namespace Matraca.Mac.Platform.Interop;

internal static class ObjCClasses
{
    static ObjCClasses() => Frameworks.EnsureLoaded();

    public static readonly IntPtr NSString = Get("NSString");
    public static readonly IntPtr NSAutoreleasePool = Get("NSAutoreleasePool");
    public static readonly IntPtr NSApplication = Get("NSApplication");
    public static readonly IntPtr NSStatusBar = Get("NSStatusBar");
    public static readonly IntPtr NSMenu = Get("NSMenu");
    public static readonly IntPtr NSMenuItem = Get("NSMenuItem");

    public static void Warm() => _ = NSApplication;

    private static IntPtr Get(string name)
    {
        IntPtr cls = ObjC.objc_getClass(name);
        return cls != IntPtr.Zero
            ? cls
            : throw new TypeLoadException($"Objective-C class not found: {name}");
    }
}
