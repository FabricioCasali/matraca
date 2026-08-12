namespace Matraca.MacSpike.Interop;

/// <summary>
/// Seletores resolvidos UMA vez e guardados.
///
/// Nao e' micro-otimizacao: <c>sel_registerName</c> aloca, e o callback do event tap roda na
/// run loop principal, onde alocar e' proibido (lei 5) — segurar a run loop derruba o tap.
/// O mesmo vale para <see cref="ObjCClasses"/>.
/// </summary>
internal static class ObjCSelectors
{
    private static IntPtr S(string name) => ObjC.sel_registerName(name);

    // NSObject
    public static readonly IntPtr Alloc = S("alloc");
    public static readonly IntPtr Init = S("init");
    public static readonly IntPtr Release = S("release");
    public static readonly IntPtr Drain = S("drain");
    public static readonly IntPtr SetValueForKey = S("setValue:forKey:");
    public static readonly IntPtr PerformOnMainThread =
        S("performSelectorOnMainThread:withObject:waitUntilDone:");

    // a nossa classe registrada em runtime
    public static readonly IntPtr Pump = S("pump");

    // NSString / NSNumber / NSDictionary
    public static readonly IntPtr StringWithUTF8String = S("stringWithUTF8String:");
    public static readonly IntPtr UTF8String = S("UTF8String");
    public static readonly IntPtr NumberWithBool = S("numberWithBool:");
    public static readonly IntPtr DictionaryWithObjectForKey = S("dictionaryWithObject:forKey:");

    // NSApplication
    public static readonly IntPtr SharedApplication = S("sharedApplication");
    public static readonly IntPtr SetActivationPolicy = S("setActivationPolicy:");
    public static readonly IntPtr Run = S("run");

    // NSWindow
    public static readonly IntPtr InitWithContentRect =
        S("initWithContentRect:styleMask:backing:defer:");
    public static readonly IntPtr SetOpaque = S("setOpaque:");
    public static readonly IntPtr SetBackgroundColor = S("setBackgroundColor:");
    public static readonly IntPtr SetHasShadow = S("setHasShadow:");
    public static readonly IntPtr SetLevel = S("setLevel:");
    public static readonly IntPtr SetIgnoresMouseEvents = S("setIgnoresMouseEvents:");
    public static readonly IntPtr SetCollectionBehavior = S("setCollectionBehavior:");
    public static readonly IntPtr OrderFrontRegardless = S("orderFrontRegardless");
    public static readonly IntPtr SetContentView = S("setContentView:");
    public static readonly IntPtr ContentView = S("contentView");
    public static readonly IntPtr CanBecomeKeyWindow = S("canBecomeKeyWindow");

    // NSColor / NSScreen / NSView
    public static readonly IntPtr ClearColor = S("clearColor");
    public static readonly IntPtr MainScreen = S("mainScreen");
    public static readonly IntPtr Frame = S("frame");
    public static readonly IntPtr InitWithFrame = S("initWithFrame:");
    public static readonly IntPtr AddSubview = S("addSubview:");
    public static readonly IntPtr SetWantsLayer = S("setWantsLayer:");
    public static readonly IntPtr Layer = S("layer");
    public static readonly IntPtr SetCornerRadius = S("setCornerRadius:");
    public static readonly IntPtr SetMasksToBounds = S("setMasksToBounds:");

    // WKWebView
    public static readonly IntPtr InitWithFrameConfiguration = S("initWithFrame:configuration:");
    public static readonly IntPtr LoadHTMLStringBaseURL = S("loadHTMLString:baseURL:");
}
