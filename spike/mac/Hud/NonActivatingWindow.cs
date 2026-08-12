using System.Runtime.InteropServices;
using Matraca.MacSpike.Interop;

namespace Matraca.MacSpike.Hud;

/// <summary>
/// Subclasse de NSWindow registrada em runtime cujo <c>-canBecomeKeyWindow</c> devolve NO.
///
/// Uma janela borderless ja' devolve NO por padrao — mas depender do padrao AQUI e' apostar
/// numa das duas metades da lei 4 ("nada rouba foco enquanto o usuario dita"). A maquina de
/// registrar classe ja' existe; usar custa dez linhas e a promessa vira fato.
///
/// A outra metade da lei 4 e' negativa e mora no <see cref="HudWindow"/>:
/// <c>makeKeyAndOrderFront:</c> e' CHAMADA PROIBIDA neste projeto — e' o "roubar foco" numa
/// linha so'. Quem mostra a janela e' <c>orderFrontRegardless</c>.
///
/// Sobre a string de tipos: no arm64 usa-se 'B' para retorno booleano ("B@:"). Se um metodo
/// de delegate com retorno booleano se comportar de forma estranha, trocar para 'c' e'
/// experimento de cinco minutos — anote o resultado no README, nao trate como bloqueio.
/// </summary>
internal static unsafe class NonActivatingWindow
{
    private static IntPtr _cls;

    public static IntPtr Class
    {
        get
        {
            if (_cls != IntPtr.Zero) return _cls;
            _cls = ObjCClassBuilder
                .Create("MatracaNonActivatingWindow", ObjCClasses.NSWindow)
                .AddMethod(ObjCSelectors.CanBecomeKeyWindow,
                           (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&CanBecomeKeyWindow,
                           "B@:")
                .Register();
            return _cls;
        }
    }

    /// <summary>
    /// Devolve byte, e nao bool: o BOOL do ObjC e' 1 byte, e uma
    /// <c>[UnmanagedCallersOnly]</c> so' aceita tipos blittable — <c>bool</c> nao e'.
    /// </summary>
    [UnmanagedCallersOnly]
    private static byte CanBecomeKeyWindow(IntPtr self, IntPtr cmd)
    {
        try { return 0; }
        catch { return 0; }
    }
}
