namespace Matraca.Mac.Platform.Interop;

internal static class ObjCSelectors
{
    private static IntPtr Register(string name) => ObjC.sel_registerName(name);

    public static readonly IntPtr Alloc = Register("alloc");
    public static readonly IntPtr Init = Register("init");
    public static readonly IntPtr Release = Register("release");
    public static readonly IntPtr Drain = Register("drain");
    public static readonly IntPtr PerformOnMainThread =
        Register("performSelectorOnMainThread:withObject:waitUntilDone:");
    public static readonly IntPtr Pump = Register("pump");
    public static readonly IntPtr StringWithUTF8String = Register("stringWithUTF8String:");
    public static readonly IntPtr UTF8String = Register("UTF8String");
    public static readonly IntPtr NumberWithBool = Register("numberWithBool:");
    public static readonly IntPtr DictionaryWithObjectForKey =
        Register("dictionaryWithObject:forKey:");
    public static readonly IntPtr SharedApplication = Register("sharedApplication");
    public static readonly IntPtr SetActivationPolicy = Register("setActivationPolicy:");
    public static readonly IntPtr ActivationPolicy = Register("activationPolicy");
    public static readonly IntPtr Run = Register("run");
    public static readonly IntPtr Terminate = Register("terminate:");
    public static readonly IntPtr SetDelegate = Register("setDelegate:");
    public static readonly IntPtr ApplicationShouldTerminate = Register("applicationShouldTerminate:");
    public static readonly IntPtr ReplyToApplicationShouldTerminate =
        Register("replyToApplicationShouldTerminate:");
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
    public static readonly IntPtr SeparatorItem = Register("separatorItem");
    public static readonly IntPtr SetEnabled = Register("setEnabled:");
    public static readonly IntPtr IsMainThread = Register("isMainThread");
    public static readonly IntPtr Screens = Register("screens");
    public static readonly IntPtr Count = Register("count");
    public static readonly IntPtr ObjectAtIndex = Register("objectAtIndex:");
    public static readonly IntPtr Frame = Register("frame");
    public static readonly IntPtr InitWithContentRect =
        Register("initWithContentRect:styleMask:backing:defer:");
    public static readonly IntPtr SetFrameDisplay = Register("setFrame:display:");
    public static readonly IntPtr SetOpaque = Register("setOpaque:");
    public static readonly IntPtr SetBackgroundColor = Register("setBackgroundColor:");
    public static readonly IntPtr SetHasShadow = Register("setHasShadow:");
    public static readonly IntPtr SetLevel = Register("setLevel:");
    public static readonly IntPtr SetIgnoresMouseEvents = Register("setIgnoresMouseEvents:");
    public static readonly IntPtr SetCollectionBehavior = Register("setCollectionBehavior:");
    public static readonly IntPtr SetReleasedWhenClosed = Register("setReleasedWhenClosed:");
    public static readonly IntPtr OrderFrontRegardless = Register("orderFrontRegardless");
    public static readonly IntPtr OrderOut = Register("orderOut:");
    public static readonly IntPtr Close = Register("close");
    public static readonly IntPtr CanBecomeKeyWindow = Register("canBecomeKeyWindow");
    public static readonly IntPtr CanBecomeMainWindow = Register("canBecomeMainWindow");
    public static readonly IntPtr ColorWithSrgb =
        Register("colorWithSRGBRed:green:blue:alpha:");
    public static readonly IntPtr RunningApplicationWithProcessIdentifier =
        Register("runningApplicationWithProcessIdentifier:");
    public static readonly IntPtr ActivateWithOptions = Register("activateWithOptions:");
    public static readonly IntPtr SharedWorkspace = Register("sharedWorkspace");
    public static readonly IntPtr NotificationCenter = Register("notificationCenter");
    public static readonly IntPtr DefaultCenter = Register("defaultCenter");
    public static readonly IntPtr AddObserver = Register("addObserver:selector:name:object:");
    public static readonly IntPtr RemoveObserver = Register("removeObserver:");
    public static readonly IntPtr EnvironmentChanged = Register("environmentChanged:");
    public static readonly IntPtr CurrentRunLoop = Register("currentRunLoop");
    public static readonly IntPtr DateWithTimeIntervalSinceNow =
        Register("dateWithTimeIntervalSinceNow:");
    public static readonly IntPtr RunUntilDate = Register("runUntilDate:");
    public static readonly IntPtr WorkspaceWillSleep = Register("workspaceWillSleep:");
    public static readonly IntPtr WorkspaceDidWake = Register("workspaceDidWake:");
}
