using System.Text.Json;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed class MacWebViewHost : IDisposable
{
    private const nuint Titled = 1 << 0;
    private const nuint Closable = 1 << 1;
    private const nuint Miniaturizable = 1 << 2;
    private const nuint Resizable = 1 << 3;
    private const nuint FullSizeContentView = 1 << 15;
    private const nuint BufferedBackingStore = 2;

    private readonly MacUrlSchemeHandler _schemeHandler;
    private readonly MacScriptMessageHandler _messageHandler;
    private readonly MacWebNavigationDelegate _navigationDelegate;
    private readonly IntPtr _application;
    private IntPtr _configuration;
    private IntPtr _userContentController;
    private IntPtr _webView;
    private IntPtr _window;
    private bool _disposed;

    public MacWebViewHost(
        string authorizedAssetRoot,
        string title = "Matraca",
        double width = 1040,
        double height = 720,
        string entryPath = "")
    {
        MainThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Window dimensions must be positive.");

        MacUrlSchemeHandler? schemeHandler = null;
        MacScriptMessageHandler? messageHandler = null;
        MacWebNavigationDelegate? navigationDelegate = null;
        try
        {
            schemeHandler = new MacUrlSchemeHandler(authorizedAssetRoot);
            messageHandler = new MacScriptMessageHandler();
            navigationDelegate = new MacWebNavigationDelegate();
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
                try { navigationDelegate?.Dispose(); }
                finally
                {
                    try { messageHandler?.Dispose(); }
                    finally { schemeHandler?.Dispose(); }
                }
            }
            throw;
        }
    }

    public event Action<string>? MessageReceived;
    public int ServedAssetCount => _schemeHandler.ServedAssetCount;
    public int BlockedAssetCount => _schemeHandler.BlockedAssetCount;
    public int BlockedNavigationCount => _navigationDelegate.BlockedNavigationCount;
    public string? LastAssetError => _schemeHandler.LastError;

    public void ShowExplicitly()
    {
        VerifyUsable();
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

    public void PostJson(string json)
    {
        VerifyUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using (JsonDocument.Parse(json)) { }

        string jsonStringLiteral = JsonSerializer.Serialize(json);
        string script = $"globalThis.matraca?.onMessage({jsonStringLiteral});";
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
        _messageHandler.MessageReceived -= ForwardMessage;
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
                finally { _schemeHandler.Dispose(); }
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
        ObjC.SendVoid(
            _webView,
            ObjCSelectors.SetNavigationDelegate,
            _navigationDelegate.Handle);
        ObjC.SendVoid(_webView, ObjCSelectors.SetUIDelegate, _navigationDelegate.Handle);
    }

    private void CreateWindow(string title, double width, double height)
    {
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
        ObjC.SendVoid(_window, ObjCSelectors.SetContentView, _webView);
        ObjC.SendVoid(_window, ObjCSelectors.Center);
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
            ObjC.SendVoid(_window, ObjCSelectors.OrderOut, IntPtr.Zero);
            ObjC.SendVoid(_window, ObjCSelectors.Close);
            ObjC.SendVoid(_window, ObjCSelectors.Release);
            _window = IntPtr.Zero;
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

    private void VerifyUsable()
    {
        MainThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
