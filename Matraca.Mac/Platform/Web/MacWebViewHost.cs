using System.Text.Json;
using Matraca.Mac.Platform.Interop;
using Matraca.Mac.Platform.Overlay;

namespace Matraca.Mac.Platform.Web;

internal sealed class MacWebViewHost : IDisposable
{
    private const nuint Titled = 1 << 0;
    private const nuint Closable = 1 << 1;
    private const nuint Miniaturizable = 1 << 2;
    private const nuint Resizable = 1 << 3;
    private const nuint FullSizeContentView = 1 << 15;
    private const nuint BufferedBackingStore = 2;
    private const nint HiddenTitle = 1;
    private const nint FloatingWindowLevel = 3;
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint Stationary = 1 << 4;
    private const nuint IgnoresCycle = 1 << 6;
    private const nuint FullScreenAuxiliary = 1 << 8;
    private const nuint CanJoinAllApplications = 1 << 18;
    private const nuint WidthSizable = 1 << 1;
    private const nuint HeightSizable = 1 << 4;

    private readonly MacUrlSchemeHandler _schemeHandler;
    private readonly MacScriptMessageHandler _messageHandler;
    private readonly MacWebNavigationDelegate _navigationDelegate;
    private readonly MacWebWindowDelegate _windowDelegate;
    private MacWindowDragView? _dragView;
    private readonly IntPtr _application;
    private readonly bool _nonActivatingOverlay;
    private readonly double _width;
    private readonly double _height;
    private IntPtr _configuration;
    private IntPtr _userContentController;
    private IntPtr _contentView;
    private IntPtr _webView;
    private IntPtr _window;
    private bool _disposed;

    public MacWebViewHost(
        string authorizedAssetRoot,
        string title = "Matraca",
        double width = 1200,
        double height = 820,
        string entryPath = "",
        bool nonActivatingOverlay = false)
    {
        MainThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive.");
        _nonActivatingOverlay = nonActivatingOverlay;
        _width = width;
        _height = height;

        MacUrlSchemeHandler? schemeHandler = null;
        MacScriptMessageHandler? messageHandler = null;
        MacWebNavigationDelegate? navigationDelegate = null;
        MacWebWindowDelegate? windowDelegate = null;
        try
        {
            schemeHandler = new MacUrlSchemeHandler(authorizedAssetRoot);
            messageHandler = new MacScriptMessageHandler();
            navigationDelegate = new MacWebNavigationDelegate();
            windowDelegate = new MacWebWindowDelegate();
            _windowDelegate = windowDelegate;
            _schemeHandler = schemeHandler;
            _messageHandler = messageHandler;
            _navigationDelegate = navigationDelegate;
            _messageHandler.MessageReceived += ForwardMessage;
            _application = ObjC.Send(ObjCClasses.NSApplication, ObjCSelectors.SharedApplication);
            if (_application == IntPtr.Zero)
                throw new InvalidOperationException("NSApplication.sharedApplication returned nil.");

            CreateWebView(width, height);
            CreateWindow(title, width, height);
            LoadRoot(entryPath);
        }
        catch
        {
            try
            {
                DisposeNativeObjects();
            }
            finally
            {
                try { windowDelegate?.Dispose(); }
                finally
                {
                    try { navigationDelegate?.Dispose(); }
                    finally
                    {
                        try { messageHandler?.Dispose(); }
                        finally { schemeHandler?.Dispose(); }
                    }
                }
            }
            throw;
        }
    }

    public event Action<string>? MessageReceived;
    public event Action? WindowWillClose;
    public int ServedAssetCount => _schemeHandler.ServedAssetCount;
    public int BlockedAssetCount => _schemeHandler.BlockedAssetCount;
    public int BlockedNavigationCount => _navigationDelegate.BlockedNavigationCount;
    public string? LastAssetError => _schemeHandler.LastError;
    internal IntPtr WindowHandle => _window;
    internal bool IsVisible => ObjC.SendBool(_window, ObjCSelectors.IsVisible);
    internal bool IsMiniaturized => ObjC.SendBool(_window, ObjCSelectors.IsMiniaturized);
    internal bool CanBecomeKeyWindow => ObjC.SendBool(_window, ObjCSelectors.CanBecomeKeyWindow);
    internal bool CanBecomeMainWindow => ObjC.SendBool(_window, ObjCSelectors.CanBecomeMainWindow);
    internal bool IgnoresMouseEvents => ObjC.SendBool(_window, ObjCSelectors.IgnoresMouseEvents);
    internal bool IsNativeTitleHidden
        => ObjC.SendNInt(_window, ObjCSelectors.TitleVisibility) == HiddenTitle;
    internal CGRect DragRegionFrame => _dragView?.Frame ?? default;
    internal bool DragRegionReceivesTitlebarHit
        => _contentView != IntPtr.Zero
            && _dragView != null
            && ObjC.SendPoint(
                _contentView,
                ObjCSelectors.HitTest,
                new CGPoint(_width / 2, _height - 24)) == _dragView.Handle;

    internal void PositionOverlayForTarget(CGRect target)
    {
        VerifyUsable();
        if (!_nonActivatingOverlay) return;
        ObjC.SendVoidRectBool(
            _window,
            ObjCSelectors.SetFrameDisplay,
            OverlayFrame(_width, _height, target),
            true);
    }

    public void ShowExplicitly()
    {
        VerifyUsable();
        if (_nonActivatingOverlay)
        {
            ObjC.SendVoid(_window, ObjCSelectors.OrderFrontRegardless);
            return;
        }
        if (IsMiniaturized)
            ObjC.SendVoid(_window, ObjCSelectors.Deminiaturize, IntPtr.Zero);
        ObjC.SendVoidBool(_application, ObjCSelectors.ActivateIgnoringOtherApps, true);
        ObjC.SendVoid(_window, ObjCSelectors.MakeKeyAndOrderFront, IntPtr.Zero);
    }

    public void Hide()
    {
        VerifyUsable();
        ObjC.SendVoid(_window, ObjCSelectors.OrderOut, IntPtr.Zero);
    }

    public void Reload()
    {
        VerifyUsable();
        ObjC.Send(_webView, ObjCSelectors.Reload);
    }

    internal void Miniaturize()
    {
        VerifyUsable();
        ObjC.SendVoid(_window, ObjCSelectors.Miniaturize, IntPtr.Zero);
    }

    internal void Close()
    {
        VerifyUsable();
        ObjC.SendVoid(_window, ObjCSelectors.PerformClose, IntPtr.Zero);
    }

    public void PostJson(string json)
    {
        VerifyUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using (JsonDocument.Parse(json)) { }

        string jsonStringLiteral = JsonSerializer.Serialize(json);
        EvaluateScript($"globalThis.matraca?.onMessage({jsonStringLiteral});");
    }

    internal void EvaluateScript(string script)
    {
        VerifyUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ObjC.SendVoid(
            _webView,
            ObjCSelectors.EvaluateJavaScriptCompletionHandler,
            NSStringRef.From(script),
            IntPtr.Zero);
    }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;

        MessageReceived = null;
        WindowWillClose = null;
        _messageHandler.MessageReceived -= ForwardMessage;
        _windowDelegate.WindowWillClose -= ForwardWindowWillClose;
        try
        {
            DisposeNativeObjects();
        }
        finally
        {
            try { _navigationDelegate.Dispose(); }
            finally
            {
                try { _messageHandler.Dispose(); }
                finally
                {
                    try { _schemeHandler.Dispose(); }
                    finally { _windowDelegate.Dispose(); }
                }
            }
        }
    }

    private void CreateWebView(double width, double height)
    {
        _configuration = ObjC.New(ObjCClasses.WKWebViewConfiguration);
        if (_configuration == IntPtr.Zero)
            throw new InvalidOperationException("WKWebViewConfiguration failed to initialize.");

        _userContentController = ObjC.Send(
            _configuration,
            ObjCSelectors.UserContentController);
        if (_userContentController == IntPtr.Zero)
            throw new InvalidOperationException("WKUserContentController is unavailable.");

        ObjC.SendVoid(
            _configuration,
            ObjCSelectors.SetURLSchemeHandlerForURLScheme,
            _schemeHandler.Handle,
            NSStringRef.From("matraca"));
        ObjC.SendVoid(
            _userContentController,
            ObjCSelectors.AddScriptMessageHandlerName,
            _messageHandler.Handle,
            NSStringRef.From(MacScriptMessageHandler.Name));

        _webView = ObjC.SendInitWebView(
            ObjC.Send(ObjCClasses.WKWebView, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithFrameConfiguration,
            new CGRect(0, 0, width, height),
            _configuration);
        if (_webView == IntPtr.Zero)
            throw new InvalidOperationException("WKWebView failed to initialize.");
        if (_nonActivatingOverlay)
        {
            IntPtr clear = ObjC.SendColor(ObjCClasses.NSColor, ObjCSelectors.ColorWithSrgb, 0, 0, 0, 0);
            ObjC.SendVoid(_webView, ObjCSelectors.SetUnderPageBackgroundColor, clear);
        }
        ObjC.SendVoid(
            _webView,
            ObjCSelectors.SetNavigationDelegate,
            _navigationDelegate.Handle);
        ObjC.SendVoid(_webView, ObjCSelectors.SetUIDelegate, _navigationDelegate.Handle);
    }

    private void CreateWindow(string title, double width, double height)
    {
        if (_nonActivatingOverlay)
        {
            CreateOverlayWindow(width, height);
            return;
        }
        _window = ObjC.SendInitWindow(
            ObjC.Send(ObjCClasses.NSWindow, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithContentRect,
            new CGRect(0, 0, width, height),
            Titled | Closable | Miniaturizable | Resizable | FullSizeContentView,
            BufferedBackingStore,
            false);
        if (_window == IntPtr.Zero)
            throw new InvalidOperationException("NSWindow failed to create the WebKit host.");

        ObjC.SendVoidBool(_window, ObjCSelectors.SetReleasedWhenClosed, false);
        ObjC.SendVoidBool(_window, ObjCSelectors.SetTitlebarAppearsTransparent, true);
        ObjC.SendVoid(_window, ObjCSelectors.SetTitle, NSStringRef.From(title));
        ObjC.SendVoidNInt(_window, ObjCSelectors.SetTitleVisibility, HiddenTitle);
        _contentView = ObjC.SendInitView(
            ObjC.Send(ObjCClasses.NSView, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithFrame,
            new CGRect(0, 0, width, height));
        if (_contentView == IntPtr.Zero)
            throw new InvalidOperationException("NSView failed to create the WebKit content container.");
        ObjC.SendVoidNUInt(
            _webView,
            ObjCSelectors.SetAutoresizingMask,
            WidthSizable | HeightSizable);
        ObjC.SendVoid(_contentView, ObjCSelectors.AddSubview, _webView);
        _windowDelegate.WindowWillClose += ForwardWindowWillClose;
        ObjC.SendVoid(_window, ObjCSelectors.SetDelegate, _windowDelegate.Handle);
        _dragView = new MacWindowDragView(new CGRect(80, height - 48, width - 144, 48));
        ObjC.SendVoid(_contentView, ObjCSelectors.AddSubview, _dragView.Handle);
        ObjC.SendVoid(_window, ObjCSelectors.SetContentView, _contentView);
        ObjC.SendVoid(_window, ObjCSelectors.Center);
        IntPtr screen = ObjC.Send(_window, ObjC.sel_registerName("screen"));
        if (screen != IntPtr.Zero)
        {
            // Clamp the outer frame, including native chrome, without touching HUD sizing.
            CGRect visible = ObjC.SendRect(screen, ObjC.sel_registerName("visibleFrame"));
            CGRect frame = ObjC.SendRect(_window, ObjCSelectors.Frame);
            double frameWidth = Math.Min(frame.Size.Width, visible.Size.Width);
            double frameHeight = Math.Min(frame.Size.Height, visible.Size.Height);
            ObjC.SendVoidRectBool(_window, ObjCSelectors.SetFrameDisplay,
                new CGRect(visible.Origin.X + (visible.Size.Width - frameWidth) / 2,
                    visible.Origin.Y + (visible.Size.Height - frameHeight) / 2,
                    frameWidth, frameHeight), true);
        }
    }

    private void CreateOverlayWindow(double width, double height)
    {
        _window = ObjC.SendInitWindow(
            ObjC.Send(MacOverlayWindowClass.Handle, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithContentRect,
            OverlayFrame(width, height),
            0,
            BufferedBackingStore,
            false);
        if (_window == IntPtr.Zero)
            throw new InvalidOperationException("NSWindow failed to create the HUD overlay.");

        IntPtr clear = ObjC.SendColor(ObjCClasses.NSColor, ObjCSelectors.ColorWithSrgb, 0, 0, 0, 0);
        ObjC.SendVoidBool(_window, ObjCSelectors.SetOpaque, false);
        ObjC.SendVoid(_window, ObjCSelectors.SetBackgroundColor, clear);
        ObjC.SendVoidBool(_window, ObjCSelectors.SetHasShadow, false);
        ObjC.SendVoidNInt(_window, ObjCSelectors.SetLevel, FloatingWindowLevel);
        ObjC.SendVoidBool(_window, ObjCSelectors.SetIgnoresMouseEvents, true);
        ObjC.SendVoidBool(_window, ObjCSelectors.SetReleasedWhenClosed, false);
        ObjC.SendVoidNUInt(
            _window,
            ObjCSelectors.SetCollectionBehavior,
            CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary | CanJoinAllApplications);
        ObjC.SendVoid(_window, ObjCSelectors.SetContentView, _webView);
    }

    private static CGRect OverlayFrame(double width, double height, CGRect? target = null)
    {
        IntPtr screens = ObjC.Send(ObjCClasses.NSScreen, ObjCSelectors.Screens);
        nuint count = screens == IntPtr.Zero ? 0 : ObjC.SendNUInt(screens, ObjCSelectors.Count);
        IntPtr selectedScreen = count > 0
            ? ObjC.SendNUInt(screens, ObjCSelectors.ObjectAtIndex, 0)
            : IntPtr.Zero;
        if (target is CGRect targetFrame)
        {
            double centerX = targetFrame.Origin.X + targetFrame.Size.Width / 2;
            double centerY = targetFrame.Origin.Y + targetFrame.Size.Height / 2;
            for (nuint index = 0; index < count; index++)
            {
                CGRect candidate = ObjC.SendRect(
                    ObjC.SendNUInt(screens, ObjCSelectors.ObjectAtIndex, index),
                    ObjCSelectors.Frame);
                if (centerX >= candidate.Origin.X
                    && centerX <= candidate.Origin.X + candidate.Size.Width
                    && centerY >= candidate.Origin.Y
                    && centerY <= candidate.Origin.Y + candidate.Size.Height)
                {
                    selectedScreen = ObjC.SendNUInt(screens, ObjCSelectors.ObjectAtIndex, index);
                    break;
                }
            }
        }
        // visibleFrame is in AppKit coordinates and excludes the Dock and menu bar.
        CGRect screen = selectedScreen != IntPtr.Zero
            ? ObjC.SendRect(selectedScreen, ObjC.sel_registerName("visibleFrame"))
            : new CGRect(0, 0, width, height);
        return new CGRect(
            screen.Origin.X + Math.Max(0, (screen.Size.Width - width) / 2),
            screen.Origin.Y + Math.Max(0, Math.Min(34, screen.Size.Height - height)),
            width,
            height);
    }

    private void LoadRoot(string entryPath)
    {
        if (entryPath.Contains('/') || entryPath.Contains('\\'))
            throw new ArgumentException("Entry path must be a file name.", nameof(entryPath));
        IntPtr url = ObjC.Send(
            ObjCClasses.NSURL,
            ObjCSelectors.URLWithString,
            NSStringRef.From($"matraca://app/{entryPath}"));
        IntPtr request = ObjC.Send(ObjCClasses.NSURLRequest, ObjCSelectors.RequestWithURL, url);
        if (url == IntPtr.Zero || request == IntPtr.Zero)
            throw new InvalidOperationException("Could not create matraca://app/ request.");
        ObjC.Send(_webView, ObjCSelectors.LoadRequest, request);
    }

    private void DisposeNativeObjects()
    {
        if (_webView != IntPtr.Zero)
        {
            ObjC.SendVoid(_webView, ObjCSelectors.StopLoading);
            ObjC.SendVoid(_webView, ObjCSelectors.SetNavigationDelegate, IntPtr.Zero);
            ObjC.SendVoid(_webView, ObjCSelectors.SetUIDelegate, IntPtr.Zero);
        }
        if (_userContentController != IntPtr.Zero)
        {
            ObjC.SendVoid(
                _userContentController,
                ObjCSelectors.RemoveScriptMessageHandlerForName,
                NSStringRef.From(MacScriptMessageHandler.Name));
        }
        if (_window != IntPtr.Zero)
        {
            ObjC.SendVoid(_window, ObjCSelectors.SetDelegate, IntPtr.Zero);
            _dragView?.Dispose();
            _dragView = null;
            ObjC.SendVoid(_window, ObjCSelectors.OrderOut, IntPtr.Zero);
            ObjC.SendVoid(_window, ObjCSelectors.Close);
            ObjC.SendVoid(_window, ObjCSelectors.Release);
            _window = IntPtr.Zero;
        }
        if (_contentView != IntPtr.Zero)
        {
            ObjC.SendVoid(_contentView, ObjCSelectors.Release);
            _contentView = IntPtr.Zero;
        }
        if (_webView != IntPtr.Zero)
        {
            ObjC.SendVoid(_webView, ObjCSelectors.Release);
            _webView = IntPtr.Zero;
        }
        if (_configuration != IntPtr.Zero)
        {
            ObjC.SendVoid(_configuration, ObjCSelectors.Release);
            _configuration = IntPtr.Zero;
        }
        _userContentController = IntPtr.Zero;
    }

    private void ForwardMessage(string json) => MessageReceived?.Invoke(json);
    private void ForwardWindowWillClose() => WindowWillClose?.Invoke();

    private void VerifyUsable()
    {
        MainThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
