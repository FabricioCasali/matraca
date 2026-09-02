namespace Matraca.Mac.Platform.Interop;

internal static class ObjCSelectors
{
    private static IntPtr Register(string name) => ObjC.sel_registerName(name);

    public static readonly IntPtr Alloc = Register("alloc");
    public static readonly IntPtr Init = Register("init");
    public static readonly IntPtr Release = Register("release");
    public static readonly IntPtr Drain = Register("drain");
    public static readonly IntPtr StringWithUTF8String = Register("stringWithUTF8String:");
    public static readonly IntPtr UTF8String = Register("UTF8String");
    public static readonly IntPtr SharedApplication = Register("sharedApplication");
    public static readonly IntPtr SetActivationPolicy = Register("setActivationPolicy:");
    public static readonly IntPtr ActivationPolicy = Register("activationPolicy");
    public static readonly IntPtr Run = Register("run");
    public static readonly IntPtr Terminate = Register("terminate:");
    public static readonly IntPtr SystemStatusBar = Register("systemStatusBar");
    public static readonly IntPtr StatusItemWithLength = Register("statusItemWithLength:");
    public static readonly IntPtr RemoveStatusItem = Register("removeStatusItem:");
    public static readonly IntPtr Button = Register("button");
    public static readonly IntPtr SetTitle = Register("setTitle:");
    public static readonly IntPtr SetMenu = Register("setMenu:");
    public static readonly IntPtr InitWithTitleActionKeyEquivalent =
        Register("initWithTitle:action:keyEquivalent:");
    public static readonly IntPtr SetTarget = Register("setTarget:");
    public static readonly IntPtr AddItem = Register("addItem:");
}
