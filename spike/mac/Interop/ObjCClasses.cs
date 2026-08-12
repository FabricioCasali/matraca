namespace Matraca.MacSpike.Interop;

/// <summary>
/// Classes do Objective-C resolvidas UMA vez e guardadas.
///
/// O construtor estatico chama <see cref="Frameworks.EnsureLoaded"/> antes de qualquer
/// <c>objc_getClass</c>, e <see cref="Get"/> LANCA quando a classe volta zero — porque
/// <c>objc_getClass</c> devolve nil sem erro se o framework nao estiver carregado, e nil
/// silencioso propagando por cinco chamadas ate' estourar num lugar sem relacao e' o pior
/// jeito de gastar uma tarde.
/// </summary>
internal static class ObjCClasses
{
    static ObjCClasses() => Frameworks.EnsureLoaded();

    private static IntPtr Get(string name)
    {
        var cls = ObjC.objc_getClass(name);
        if (cls == IntPtr.Zero)
            throw new TypeLoadException(
                $"objc_getClass(\"{name}\") devolveu nil. O framework que define essa classe "
              + "nao esta' carregado no processo — ver Frameworks.EnsureLoaded().");
        return cls;
    }

    public static readonly IntPtr NSObject = Get("NSObject");
    public static readonly IntPtr NSString = Get("NSString");
    public static readonly IntPtr NSNumber = Get("NSNumber");
    public static readonly IntPtr NSDictionary = Get("NSDictionary");
    public static readonly IntPtr NSAutoreleasePool = Get("NSAutoreleasePool");
    public static readonly IntPtr NSApplication = Get("NSApplication");
    public static readonly IntPtr NSWindow = Get("NSWindow");
    public static readonly IntPtr NSColor = Get("NSColor");
    public static readonly IntPtr NSScreen = Get("NSScreen");
    public static readonly IntPtr NSView = Get("NSView");
    public static readonly IntPtr WKWebView = Get("WKWebView");
    public static readonly IntPtr WKWebViewConfiguration = Get("WKWebViewConfiguration");

    /// <summary>Forca a resolucao de todas as classes agora, p/ falhar cedo e com nome.</summary>
    public static void Warm() => _ = NSObject;
}
