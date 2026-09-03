using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed unsafe class MacUrlSchemeHandler : IDisposable
{
    private const string Scheme = "matraca";
    private const string Host = "app";
    private const string ErrorDomain = "Matraca.WebAssets";
    private static readonly object CallbackGate = new();
    private static readonly Dictionary<IntPtr, MacUrlSchemeHandler> Instances = [];
    private static readonly IntPtr HandlerClass = CreateHandlerClass();

    private readonly string _root;
    private readonly string _rootPrefix;
    private IntPtr _native;

    public MacUrlSchemeHandler(string authorizedRoot)
    {
        MainThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizedRoot);
        if (!Directory.Exists(authorizedRoot))
            throw new DirectoryNotFoundException($"Web asset root not found: {authorizedRoot}");

        _root = NativePath.Resolve(Path.GetFullPath(authorizedRoot));
        _rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        _native = ObjC.New(HandlerClass);
        if (_native == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the WKURLSchemeHandler.");
        lock (CallbackGate) Instances.Add(_native, this);
    }

    public IntPtr Handle => _native;
    public int ServedAssetCount { get; private set; }
    public int BlockedAssetCount { get; private set; }
    public string? LastError { get; private set; }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_native == IntPtr.Zero) return;

        lock (CallbackGate) Instances.Remove(_native);
        ObjC.SendVoid(_native, ObjCSelectors.Release);
        _native = IntPtr.Zero;
    }

    private static IntPtr CreateHandlerClass()
        => ObjCClassBuilder
            .Create("MatracaUrlSchemeHandler", ObjCClasses.NSObject)
            .AddProtocol("WKURLSchemeHandler")
            .AddMethod(
                ObjCSelectors.StartUrlSchemeTask,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&StartTask,
                "v@:@@")
            .AddMethod(
                ObjCSelectors.StopUrlSchemeTask,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&StopTask,
                "v@:@@")
            .Register();

    [UnmanagedCallersOnly]
    private static void StartTask(
        IntPtr self,
        IntPtr command,
        IntPtr webView,
        IntPtr schemeTask)
    {
        try
        {
            MacUrlSchemeHandler? handler;
            lock (CallbackGate) Instances.TryGetValue(self, out handler);
            if (handler == null) return;
            handler.Serve(schemeTask);
        }
        catch (Exception exception)
        {
            try
            {
                FailTask(schemeTask, 3);
                Logger.Error("Falha ao servir asset do WebKit", exception);
            }
            catch { }
        }
    }

    [UnmanagedCallersOnly]
    private static void StopTask(
        IntPtr self,
        IntPtr command,
        IntPtr webView,
        IntPtr schemeTask)
    {
        try
        {
            // Reads complete synchronously, so there is no outstanding operation to cancel.
        }
        catch { }
    }

    private void Serve(IntPtr schemeTask)
    {
        IntPtr request = ObjC.Send(schemeTask, ObjCSelectors.Request);
        IntPtr url = ObjC.Send(request, ObjCSelectors.URL);
        string? absoluteUrl = NSStringRef.To(ObjC.Send(url, ObjCSelectors.AbsoluteString));
        if (!TryResolveAsset(absoluteUrl, out string? path))
        {
            BlockedAssetCount++;
            LastError = "Blocked invalid or out-of-root asset URL.";
            FailTask(schemeTask, 1);
            return;
        }

        if (!File.Exists(path))
        {
            LastError = $"Asset not found: {absoluteUrl}";
            FailTask(schemeTask, 2);
            return;
        }

        byte[] content = File.ReadAllBytes(path);
        IntPtr response = ObjC.SendInitUrlResponse(
            ObjC.Send(ObjCClasses.NSURLResponse, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithUrlMimeTypeExpectedContentLengthTextEncodingName,
            url,
            NSStringRef.From(GetMimeType(path)),
            content.Length,
            NSStringRef.From("utf-8"));
        if (response == IntPtr.Zero)
            throw new InvalidOperationException("NSURLResponse failed to initialize.");

        try
        {
            IntPtr data = ObjC.SendBytesNUInt(
                ObjCClasses.NSData,
                ObjCSelectors.DataWithBytesLength,
                content,
                (nuint)content.Length);
            if (data == IntPtr.Zero)
                throw new InvalidOperationException("NSData failed to copy a web asset.");

            ObjC.SendVoid(schemeTask, ObjCSelectors.DidReceiveResponse, response);
            ObjC.SendVoid(schemeTask, ObjCSelectors.DidReceiveData, data);
            ObjC.SendVoid(schemeTask, ObjCSelectors.DidFinish);
            ServedAssetCount++;
        }
        finally
        {
            ObjC.SendVoid(response, ObjCSelectors.Release);
        }
    }

    private bool TryResolveAsset(string? absoluteUrl, out string? path)
    {
        path = null;
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort)
            return false;

        string relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (relative.Length == 0) relative = "index.html";
        if (relative.Contains('\0') || relative.Contains('\\')) return false;

        string candidate = Path.GetFullPath(Path.Combine(_root, relative));
        if (!candidate.StartsWith(_rootPrefix, StringComparison.Ordinal)) return false;
        if (!File.Exists(candidate))
        {
            path = candidate;
            return true;
        }

        string resolved = NativePath.Resolve(candidate);
        if (!resolved.StartsWith(_rootPrefix, StringComparison.Ordinal)) return false;
        path = resolved;
        return true;
    }

    private static void FailTask(IntPtr schemeTask, nint code)
    {
        if (schemeTask == IntPtr.Zero) return;
        IntPtr error = ObjC.SendNIntPtr(
            ObjCClasses.NSError,
            ObjCSelectors.ErrorWithDomainCodeUserInfo,
            NSStringRef.From(ErrorDomain),
            code,
            IntPtr.Zero);
        if (error != IntPtr.Zero)
            ObjC.SendVoid(schemeTask, ObjCSelectors.DidFailWithError, error);
    }

    private static string GetMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" or ".mjs" => "text/javascript",
            ".json" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream",
        };
}
