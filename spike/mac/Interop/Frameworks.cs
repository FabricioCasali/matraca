using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// Carrega os frameworks do sistema no processo. PRECISA rodar antes de qualquer
/// <c>objc_getClass</c>.
///
/// A armadilha, e ela vem primeiro: um app .NET de console NAO carrega o AppKit sozinho, e
/// <c>objc_getClass("NSApplication")</c> devolve <c>IntPtr.Zero</c> — SEM ERRO — enquanto o
/// framework nao estiver no processo. O nil silencioso entao se propaga por cinco chamadas
/// de <c>objc_msgSend</c> (que aceitam receptor nil e devolvem nil de volta, calados) ate'
/// estourar em algum lugar sem relacao nenhuma com a causa.
///
/// Por isso duas coisas: isto roda primeiro, e <see cref="ObjCClasses"/> LANCA quando uma
/// classe volta zero, com o nome dela na mensagem.
/// </summary>
internal static class Frameworks
{
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string WebKit = "/System/Library/Frameworks/WebKit.framework/WebKit";
    private const string CoreGraphicsFw = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string AudioToolboxFw = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    // Nao estava na lista do desenho, mas e' necessario: e' daqui que sai o simbolo
    // kCFRunLoopCommonModes, que precisa ser lido por dlsym (e' um CFStringRef global, nao
    // uma funcao). O CoreFoundation ja' vem carregado como dependencia do Foundation; o
    // Load explicito existe p/ termos o handle na mao.
    private const string CoreFoundationFw = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static bool _loaded;
    private static readonly object Gate = new();

    /// <summary>Handle do CoreFoundation, p/ ler simbolos globais (ver <see cref="Symbol"/>).</summary>
    public static IntPtr CoreFoundationHandle { get; private set; }

    public static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;

            CoreFoundationHandle = Load(CoreFoundationFw);
            Load(Foundation);
            Load(AppKit);
            Load(WebKit);
            Load(CoreGraphicsFw);
            Load(AudioToolboxFw);

            _loaded = true;
            SpikeLog.Info("frameworks carregados: CoreFoundation, Foundation, AppKit, WebKit, "
                        + "CoreGraphics, AudioToolbox");
        }
    }

    private static IntPtr Load(string path)
    {
        if (!NativeLibrary.TryLoad(path, out var handle))
            throw new DllNotFoundException($"Nao consegui carregar o framework: {path}");
        return handle;
    }

    /// <summary>
    /// Le um ponteiro global exportado por um framework (ex.: <c>kCFRunLoopCommonModes</c>,
    /// que e' um CFStringRef, e nao uma funcao). O dlsym devolve o ENDERECO da variavel;
    /// o valor util e' o que esta' guardado nesse endereco — dai o ReadIntPtr. Trocar os
    /// dois e' um bug classico e silencioso.
    /// </summary>
    public static IntPtr Symbol(IntPtr library, string name)
    {
        EnsureLoaded();
        if (!NativeLibrary.TryGetExport(library, name, out var addr) || addr == IntPtr.Zero)
            throw new EntryPointNotFoundException($"simbolo nao encontrado: {name}");
        return Marshal.ReadIntPtr(addr);
    }
}
