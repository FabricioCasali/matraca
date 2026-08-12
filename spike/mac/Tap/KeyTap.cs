using System.Runtime.InteropServices;
using Matraca.MacSpike.Interop;

namespace Matraca.MacSpike.Tap;

/// <summary>
/// O event tap de teclado — o gemeo macOS do WH_KEYBOARD_LL do <c>HotkeyListener</c>.
///
/// Roda na run loop principal. O callback faz TRES coisas, nesta ordem, e a ordem e' o
/// contrato:
///
///  1. Le a marca da fonte (kCGEventSourceUserData) e, se for a nossa, devolve o evento sem
///     nenhum trabalho. Isto vem PRIMEIRO porque cada caractere que injetamos passa por
///     aqui, e um ditado longo sao centenas deles. E' pela MARCA, e nunca por "o evento e'
///     sintetico": remapeadores como o AutoHotkey (e o Karabiner, deste lado) tambem
///     injetam, e o atalho precisa continuar valendo para eles (lei 5).
///  2. Trata kCGEventTapDisabledByTimeout e kCGEventTapDisabledByUserInput. Esses dois
///     chegam ao callback INDEPENDENTEMENTE da mascara, entao o tipo e' testado antes de
///     qualquer coisa que suponha um evento de teclado.
///  3. Decide e enfileira. Nada bloqueante, nunca — segurar a run loop principal aqui e'
///     o que faz o sistema DESABILITAR o tap (o equivalente macOS do
///     LowLevelHooksTimeout do Windows; la' o sintoma e' caractere descartado, aqui e' o
///     app ficar mudo).
/// </summary>
internal static unsafe class KeyTap
{
    /// <summary>
    /// A marca da fonte. MESMO valor do <c>TextInjector.InjectionTag</c> do Windows —
    /// 0x4D545243, "MTRC". Um numero, dois sistemas: quem le o log de um reconhece o outro.
    /// </summary>
    public const long InjectionTag = 0x4D54_5243;

    // Estado estatico porque o callback e' [UnmanagedCallersOnly]: nao ha' "this" para
    // carregar. O userInfo do tap poderia levar um handle, mas com um unico tap por
    // processo isso so' adicionaria uma indirecao e um GCHandle para manter vivo.
    private static IntPtr _tap;
    private static IntPtr _source;

    /// <summary>Quantos eventos com a NOSSA marca o tap viu e deixou passar.</summary>
    public static long OwnEventsSeen;

    /// <summary>Quantas vezes o tap foi religado depois de o sistema desliga-lo.</summary>
    public static int Rearms;

    /// <summary>Disparado (na thread do tap!) quando a tecla de ditado e' pressionada.</summary>
    public static Action? OnHotkey;

    /// <summary>
    /// Quando true, o F14 faz o callback DORMIR 3 s por dentro, de proposito, para forcar o
    /// kCGEventTapDisabledByTimeout e provar que o religamento funciona. Sem esse gatilho
    /// deliberado o tratamento do timeout e' codigo nunca exercitado, e a Fase 2 herdaria
    /// uma promessa em vez de um fato.
    /// </summary>
    public static bool DebugTimeoutKeyEnabled = true;

    public static bool IsRunning => _tap != IntPtr.Zero;

    /// <summary>
    /// Cria o tap e anexa a run loop principal. Devolve false se o CGEventTapCreate falhou —
    /// e a causa numero um disso e' permissao de Acessibilidade ausente.
    /// </summary>
    public static bool Start()
    {
        Frameworks.EnsureLoaded();

        _tap = CoreGraphics.CGEventTapCreate(
            CoreGraphics.kCGSessionEventTap,
            CoreGraphics.kCGHeadInsertEventTap,
            CoreGraphics.kCGEventTapOptionDefault,
            CoreGraphics.MaskKeyDownUp,
            &Callback,
            IntPtr.Zero);

        if (_tap == IntPtr.Zero)
        {
            SpikeLog.Error("CGEventTapCreate devolveu NULL. Quase sempre e' permissao de "
                         + "Acessibilidade: Ajustes do Sistema -> Privacidade e Seguranca -> "
                         + "Acessibilidade. Com assinatura ad-hoc, remova a entrada antiga do "
                         + "Matraca e adicione de novo — o cdhash mudou no ultimo build.");
            return false;
        }

        _source = CoreGraphics.CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
        CoreGraphics.CFRunLoopAddSource(CoreGraphics.CFRunLoopGetMain(), _source,
                                        CoreGraphics.CommonModes);
        CoreGraphics.CGEventTapEnable(_tap, true);

        SpikeLog.Info($"event tap ativo (mascara 0x{CoreGraphics.MaskKeyDownUp:X}, "
                    + $"F13=0x{CoreGraphics.kVK_F13:X2}, F14=0x{CoreGraphics.kVK_F14:X2} = "
                    + "tecla de depuracao que forca o timeout)");
        return true;
    }

    public static void Stop()
    {
        if (_tap == IntPtr.Zero) return;
        CoreGraphics.CGEventTapEnable(_tap, false);
        // Regra do +1: CFMachPortCreateRunLoopSource e CGEventTapCreate devolvem retidos.
        if (_source != IntPtr.Zero) { CoreGraphics.CFRelease(_source); _source = IntPtr.Zero; }
        CoreGraphics.CFRelease(_tap);
        _tap = IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    private static IntPtr Callback(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo)
    {
        try
        {
            // 1. Nosso proprio texto: sai na frente, antes de qualquer outro trabalho.
            if (CoreGraphics.CGEventGetIntegerValueField(@event,
                    CoreGraphics.kCGEventSourceUserData) == InjectionTag)
            {
                OwnEventsSeen++;
                return @event;
            }

            // 2. O tap morreu? Chega aqui mesmo fora da mascara.
            if (type == CoreGraphics.kCGEventTapDisabledByTimeout
             || type == CoreGraphics.kCGEventTapDisabledByUserInput)
            {
                Rearms++;
                string causa = type == CoreGraphics.kCGEventTapDisabledByTimeout
                    ? "TIMEOUT (o callback demorou demais)"
                    : "ENTRADA DO USUARIO";
                SpikeLog.Warn($"o sistema DESABILITOU o tap por {causa}. Religando... "
                            + $"(religamento #{Rearms})");
                CoreGraphics.CGEventTapEnable(_tap, true);
                return @event;
            }

            // 3. Decidir e enfileirar.
            if (type == CoreGraphics.kCGEventKeyDown)
            {
                var keycode = (ushort)CoreGraphics.CGEventGetIntegerValueField(
                    @event, CoreGraphics.kCGKeyboardEventKeycode);

                // O spike loga o keycode de TODA tecla: e' assim que o F13 se confirma na
                // maquina do Fabricio sem depender de memoria de cabecalho, e e' a semente
                // da tabela de nomes canonicos de tecla da Fase 2 (lei 6).
                SpikeLog.Info($"keyDown keycode={keycode} (0x{keycode:X2})");

                if (keycode == CoreGraphics.kVK_F14 && DebugTimeoutKeyEnabled)
                {
                    SpikeLog.Warn("F14: dormindo 3 s DENTRO do callback de proposito, p/ "
                                + "forcar o kCGEventTapDisabledByTimeout. A proxima tecla "
                                + "que voce apertar tem de aparecer no log.");
                    Thread.Sleep(3000);
                    return IntPtr.Zero;
                }

                if (keycode == CoreGraphics.kVK_F13)
                {
                    SpikeLog.Info("F13 -> disparo. O evento e' ENGOLIDO (nao chega ao app da frente).");
                    var handler = OnHotkey;
                    if (handler != null) ThreadPool.UnsafeQueueUserWorkItem(_ => Run(handler), null);
                    return IntPtr.Zero;   // engole
                }
            }
        }
        catch (Exception ex)
        {
            // Excecao gerenciada atravessando esta fronteira derruba o processo sem stack
            // trace util. Engolir aqui e' a unica opcao segura.
            try { SpikeLog.Error("callback do tap estourou", ex); } catch { }
        }

        return @event;
    }

    private static void Run(Action handler)
    {
        try { handler(); }
        catch (Exception ex) { SpikeLog.Error("handler do atalho estourou", ex); }
    }
}
