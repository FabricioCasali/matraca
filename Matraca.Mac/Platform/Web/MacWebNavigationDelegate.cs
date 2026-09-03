using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed unsafe class MacWebNavigationDelegate : IDisposable
{
    private const nint Cancel = 0;
    private const nint Allow = 1;
    private static readonly object CallbackGate = new();
    private static readonly Dictionary<IntPtr, MacWebNavigationDelegate> Instances = [];
    private static readonly IntPtr DelegateClass = CreateDelegateClass();
    private IntPtr _native;

    public MacWebNavigationDelegate()
    {
        MainThread.VerifyAccess();
        _native = ObjC.New(DelegateClass);
        if (_native == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the WKNavigationDelegate.");
        lock (CallbackGate) Instances.Add(_native, this);
    }

    public IntPtr Handle => _native;
    public int BlockedNavigationCount { get; private set; }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_native == IntPtr.Zero) return;

        lock (CallbackGate) Instances.Remove(_native);
        ObjC.SendVoid(_native, ObjCSelectors.Release);
        _native = IntPtr.Zero;
    }

    private static IntPtr CreateDelegateClass()
        => ObjCClassBuilder
            .Create("MatracaWebNavigationDelegate", ObjCClasses.NSObject)
            .AddProtocol("WKNavigationDelegate")
            .AddProtocol("WKUIDelegate")
            .AddMethod(
                ObjCSelectors.DecidePolicyForNavigationAction,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DecidePolicy,
                "v@:@@@?")
            .AddMethod(
                ObjCSelectors.CreateWebViewForNavigationAction,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&CreateWebView,
                "@@:@@@@")
            .Register();

    [UnmanagedCallersOnly]
    private static void DecidePolicy(
        IntPtr self,
        IntPtr command,
        IntPtr webView,
        IntPtr navigationAction,
        IntPtr decisionHandler)
    {
        try
        {
            MacWebNavigationDelegate? instance;
            lock (CallbackGate) Instances.TryGetValue(self, out instance);
            bool hasTarget = ObjC.Send(navigationAction, ObjCSelectors.TargetFrame) != IntPtr.Zero;
            string? url = GetUrl(navigationAction);
            bool allowed = instance != null && hasTarget && IsAllowed(url);
            if (instance != null && !allowed) instance.BlockedNavigationCount++;
            ObjCBlock.InvokeNavigationPolicy(decisionHandler, allowed ? Allow : Cancel);
        }
        catch (Exception exception)
        {
            try
            {
                ObjCBlock.InvokeNavigationPolicy(decisionHandler, Cancel);
                Logger.Error("Falha ao decidir navegacao do WebKit", exception);
            }
            catch { }
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr CreateWebView(
        IntPtr self,
        IntPtr command,
        IntPtr webView,
        IntPtr configuration,
        IntPtr navigationAction,
        IntPtr windowFeatures)
    {
        try { return IntPtr.Zero; }
        catch { return IntPtr.Zero; }
    }

    private static string? GetUrl(IntPtr navigationAction)
    {
        IntPtr request = ObjC.Send(navigationAction, ObjCSelectors.Request);
        IntPtr url = ObjC.Send(request, ObjCSelectors.URL);
        return NSStringRef.To(ObjC.Send(url, ObjCSelectors.AbsoluteString));
    }

    private static bool IsAllowed(string? absoluteUrl)
        => Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Scheme, "matraca", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "app", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.IsDefaultPort;
}
