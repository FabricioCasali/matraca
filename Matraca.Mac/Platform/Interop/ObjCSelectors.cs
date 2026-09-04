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
    public static readonly IntPtr OpenMatraca = Register("openMatraca:");
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
    public static readonly IntPtr IsVisible = Register("isVisible");
    public static readonly IntPtr IsMiniaturized = Register("isMiniaturized");
    public static readonly IntPtr Miniaturize = Register("miniaturize:");
    public static readonly IntPtr Deminiaturize = Register("deminiaturize:");
    public static readonly IntPtr PerformClose = Register("performClose:");
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
    public static readonly IntPtr OpenURL = Register("openURL:");
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
    public static readonly IntPtr SetContentView = Register("setContentView:");
    public static readonly IntPtr InitWithFrame = Register("initWithFrame:");
    public static readonly IntPtr AddSubview = Register("addSubview:");
    public static readonly IntPtr RemoveFromSuperview = Register("removeFromSuperview");
    public static readonly IntPtr SetAutoresizingMask = Register("setAutoresizingMask:");
    public static readonly IntPtr MouseDown = Register("mouseDown:");
    public static readonly IntPtr Window = Register("window");
    public static readonly IntPtr PerformWindowDragWithEvent =
        Register("performWindowDragWithEvent:");
    public static readonly IntPtr WindowWillClose = Register("windowWillClose:");
    public static readonly IntPtr SetTitlebarAppearsTransparent =
        Register("setTitlebarAppearsTransparent:");
    public static readonly IntPtr Center = Register("center");
    public static readonly IntPtr MakeKeyAndOrderFront = Register("makeKeyAndOrderFront:");
    public static readonly IntPtr ActivateIgnoringOtherApps = Register("activateIgnoringOtherApps:");
    public static readonly IntPtr InitWithFrameConfiguration =
        Register("initWithFrame:configuration:");
    public static readonly IntPtr SetURLSchemeHandlerForURLScheme =
        Register("setURLSchemeHandler:forURLScheme:");
    public static readonly IntPtr UserContentController = Register("userContentController");
    public static readonly IntPtr AddScriptMessageHandlerName =
        Register("addScriptMessageHandler:name:");
    public static readonly IntPtr RemoveScriptMessageHandlerForName =
        Register("removeScriptMessageHandlerForName:");
    public static readonly IntPtr SetNavigationDelegate = Register("setNavigationDelegate:");
    public static readonly IntPtr SetUIDelegate = Register("setUIDelegate:");
    public static readonly IntPtr EvaluateJavaScriptCompletionHandler =
        Register("evaluateJavaScript:completionHandler:");
    public static readonly IntPtr StopLoading = Register("stopLoading");
    public static readonly IntPtr Reload = Register("reload");
    public static readonly IntPtr URLWithString = Register("URLWithString:");
    public static readonly IntPtr RequestWithURL = Register("requestWithURL:");
    public static readonly IntPtr LoadRequest = Register("loadRequest:");
    public static readonly IntPtr Request = Register("request");
    public static readonly IntPtr URL = Register("URL");
    public static readonly IntPtr AbsoluteString = Register("absoluteString");
    public static readonly IntPtr TargetFrame = Register("targetFrame");
    public static readonly IntPtr Body = Register("body");
    public static readonly IntPtr IsKindOfClass = Register("isKindOfClass:");
    public static readonly IntPtr DataWithBytesLength = Register("dataWithBytes:length:");
    public static readonly IntPtr InitWithUrlMimeTypeExpectedContentLengthTextEncodingName =
        Register("initWithURL:MIMEType:expectedContentLength:textEncodingName:");
    public static readonly IntPtr ErrorWithDomainCodeUserInfo =
        Register("errorWithDomain:code:userInfo:");
    public static readonly IntPtr DidReceiveResponse = Register("didReceiveResponse:");
    public static readonly IntPtr DidReceiveData = Register("didReceiveData:");
    public static readonly IntPtr DidFinish = Register("didFinish");
    public static readonly IntPtr DidFailWithError = Register("didFailWithError:");
    public static readonly IntPtr StartUrlSchemeTask = Register("webView:startURLSchemeTask:");
    public static readonly IntPtr StopUrlSchemeTask = Register("webView:stopURLSchemeTask:");
    public static readonly IntPtr DidReceiveScriptMessage =
        Register("userContentController:didReceiveScriptMessage:");
    public static readonly IntPtr DecidePolicyForNavigationAction =
        Register("webView:decidePolicyForNavigationAction:decisionHandler:");
    public static readonly IntPtr CreateWebViewForNavigationAction = Register(
        "webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:");
}
