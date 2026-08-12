using Matraca.MacSpike.Interop;

namespace Matraca.MacSpike.Hud;

/// <summary>
/// A janela do HUD: NSWindow borderless, transparente, sempre no topo, que NAO recebe foco e
/// deixa o clique passar — com um WKWebView dentro.
///
/// E' o item de MAIOR risco do cartao e por isso vem por ultimo: e' o unico que pode falhar
/// sem bloquear a Fase 2 (o Mac ainda dita sem HUD).
///
/// A TRANSPARENCIA E' UMA PILHA DE TRES, e as tres precisam valer juntas:
///   1. [webView setValue:@NO forKey:@"drawsBackground"] — via KVC, porque o WKWebView NAO
///      expoe drawsBackground publico. E' o item que sustenta tudo e o menos apoiado: nao e'
///      API publica, e nao ha' como verificar de antemao se ainda vale.
///   2. CSS: html, body { background: transparent; }  (em hud.html)
///   3. A janela: setOpaque:NO + backgroundColor = clearColor.
///
/// PLANO B, se (1) falhar: HUD opaco com cantos arredondados — mantem opaque=NO + clearColor
/// na janela (p/ os cantos serem realmente transparentes) e poe o WKWebView dentro de um
/// NSView contêiner com wantsLayer=YES, layer.cornerRadius e masksToBounds=YES. Perde-se o
/// HUD "flutuando" sem caixa; nao bloqueia nada.
/// PLANO C (desenhar o HUD em Core Graphics, sem web view) fura a premissa de UI unica nas
/// duas plataformas: e' regressao de arquitetura, e a decisao volta ao Fabricio.
/// </summary>
internal static class HudWindow
{
    // ---- constantes do AppKit ----
    private const nuint NSWindowStyleMaskBorderless = 0;
    private const nuint NSBackingStoreBuffered = 2;
    private const nint NSFloatingWindowLevel = 3;
    private const nuint NSApplicationActivationPolicyAccessory = 1;

    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint Stationary = 1 << 4;
    /// <summary>
    /// O detalhe que todo mundo esquece, e que aparece como "o HUD sumiu quando entrei em
    /// tela cheia no Claude Code" — que e' justamente o caso de uso numero um.
    /// </summary>
    private const nuint FullScreenAuxiliary = 1 << 8;

    private static IntPtr _window;

    /// <summary>
    /// Monta e mostra o HUD. PRECISA rodar na thread principal, e depois disso quem chama
    /// deve entrar em <see cref="RunApp"/>.
    /// </summary>
    public static void Show(double width = 300, double height = 90)
    {
        Frameworks.EnsureLoaded();

        // App de bandeja: sem icone no Dock, sem menu bar propria.
        var app = ObjC.Send(ObjCClasses.NSApplication, ObjCSelectors.SharedApplication);
        ObjC.SendVoidNUInt(app, ObjCSelectors.SetActivationPolicy,
                           NSApplicationActivationPolicyAccessory);

        // canto inferior centro da tela principal
        var screen = ObjC.Send(ObjCClasses.NSScreen, ObjCSelectors.MainScreen);
        var vis = ObjC.SendRect(screen, ObjCSelectors.Frame);
        double x = vis.Origin.X + (vis.Size.Width - width) / 2;
        double y = vis.Origin.Y + 120;
        var frame = new CGRect(x, y, width, height);
        SpikeLog.Info($"tela principal {vis}; HUD em {frame}");

        var alloc = ObjC.Send(NonActivatingWindow.Class, ObjCSelectors.Alloc);
        _window = ObjC.SendInitWindow(alloc, ObjCSelectors.InitWithContentRect, frame,
                                      NSWindowStyleMaskBorderless, NSBackingStoreBuffered,
                                      defer: false);
        if (_window == IntPtr.Zero)
            throw new InvalidOperationException("initWithContentRect: devolveu nil");

        var clear = ObjC.Send(ObjCClasses.NSColor, ObjCSelectors.ClearColor);
        ObjC.SendVoid(_window, ObjCSelectors.SetOpaque, false);              // BOOL: I1
        ObjC.SendVoid(_window, ObjCSelectors.SetBackgroundColor, clear);
        ObjC.SendVoid(_window, ObjCSelectors.SetHasShadow, false);           // senao sombra no vazio
        ObjC.SendVoidNInt(_window, ObjCSelectors.SetLevel, NSFloatingWindowLevel);
        ObjC.SendVoid(_window, ObjCSelectors.SetIgnoresMouseEvents, true);   // clique atravessa
        ObjC.SendVoidNUInt(_window, ObjCSelectors.SetCollectionBehavior,
                           CanJoinAllSpaces | Stationary | FullScreenAuxiliary);

        // ---- o WKWebView ----
        var config = ObjC.New(ObjCClasses.WKWebViewConfiguration);
        var webAlloc = ObjC.Send(ObjCClasses.WKWebView, ObjCSelectors.Alloc);
        var web = ObjC.SendInitWebView(webAlloc, ObjCSelectors.InitWithFrameConfiguration,
                                       new CGRect(0, 0, width, height), config);
        if (web == IntPtr.Zero)
            throw new InvalidOperationException("WKWebView initWithFrame:configuration: devolveu nil");

        // Camada 1 da pilha de transparencia — a que nao e' API publica.
        var no = ObjC.SendWithBool(ObjCClasses.NSNumber, ObjCSelectors.NumberWithBool, false);
        ObjC.SendVoid(web, ObjCSelectors.SetValueForKey, no, NSStringRef.From("drawsBackground"));
        SpikeLog.Info("KVC drawsBackground=NO aplicado ao WKWebView (nao e' API publica: se a "
                    + "transparencia falhar, e' o primeiro suspeito)");

        var html = LoadHtml();
        ObjC.SendVoid(web, ObjCSelectors.LoadHTMLStringBaseURL, NSStringRef.From(html), IntPtr.Zero);

        ObjC.SendVoid(_window, ObjCSelectors.SetContentView, web);

        // orderFrontRegardless mostra SEM ativar. makeKeyAndOrderFront: seria a violacao
        // da lei 4 numa linha — nao existe neste projeto.
        ObjC.SendVoid(_window, ObjCSelectors.OrderFrontRegardless);
        SpikeLog.Info("HUD na tela (orderFrontRegardless — sem ativar, sem roubar foco)");
    }

    private static string LoadHtml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "hud.html");
        if (File.Exists(path)) return File.ReadAllText(path);

        SpikeLog.Warn($"hud.html nao encontrado em {path}; usando o HTML de emergencia.");
        return "<html><body style=\"background:transparent;color:#fff;font:14px sans-serif\">"
             + "Matraca — hud.html nao foi copiado para o bundle</body></html>";
    }

    /// <summary>[NSApp run] — nao retorna.</summary>
    public static void RunApp()
    {
        var app = ObjC.Send(ObjCClasses.NSApplication, ObjCSelectors.SharedApplication);
        ObjC.SendVoid(app, ObjCSelectors.Run);
    }
}
