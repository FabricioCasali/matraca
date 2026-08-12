using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// CGEvent, CGEventTap, CFRunLoop e CFRelease.
///
/// Tudo aqui e' Core Foundation puro: nada de ObjC, nada de autorelease pool. Em compensacao
/// vale a regra do +1 — <c>CGEventCreateKeyboardEvent</c>, <c>CGEventSourceCreate</c>,
/// <c>CGEventTapCreate</c> e <c>CFMachPortCreateRunLoopSource</c> devolvem objeto com
/// contagem 1 e EXIGEM <see cref="CFRelease"/>. Num ditado longo sao centenas de CGEvent:
/// esquecer o release e' vazamento visivel no Activity Monitor.
///
/// As constantes abaixo foram conferidas contra os cabecalhos; qualquer uma que nao bater
/// com a realidade deve ser corrigida AQUI e registrada no README do spike.
/// </summary>
internal static unsafe class CoreGraphics
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // ---- CGEventTapLocation ----
    public const uint kCGHIDEventTap = 0;
    public const uint kCGSessionEventTap = 1;
    public const uint kCGAnnotatedSessionEventTap = 2;

    // ---- CGEventTapPlacement ----
    public const uint kCGHeadInsertEventTap = 0;

    // ---- CGEventTapOptions ----
    /// <summary>Escuta E modifica (pode engolir o evento devolvendo NULL).</summary>
    public const uint kCGEventTapOptionDefault = 0;
    /// <summary>So' escuta; o retorno do callback e' ignorado.</summary>
    public const uint kCGEventTapOptionListenOnly = 1;

    // ---- CGEventType ----
    public const uint kCGEventKeyDown = 10;
    public const uint kCGEventKeyUp = 11;
    public const uint kCGEventFlagsChanged = 12;

    /// <summary>Mascara de keyDown|keyUp = 0xC00.</summary>
    public const ulong MaskKeyDownUp = (1UL << (int)kCGEventKeyDown) | (1UL << (int)kCGEventKeyUp);

    /// <summary>
    /// Os dois tipos de "o tap morreu". ATENCAO: eles chegam ao callback INDEPENDENTEMENTE
    /// da mascara — por isso o tipo do evento tem de ser testado antes de qualquer outra
    /// coisa que suponha um evento de teclado.
    /// </summary>
    public const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;
    public const uint kCGEventTapDisabledByUserInput = 0xFFFFFFFF;

    // ---- CGEventField ----
    public const uint kCGKeyboardEventKeycode = 9;
    /// <summary>O dwExtraInfo deles: onde a marca da fonte e' escrita e lida.</summary>
    public const uint kCGEventSourceUserData = 42;

    // ---- CGEventSourceStateID ----
    public const int kCGEventSourceStateHIDSystemState = 1;

    /// <summary>
    /// kVK_F13. O valor consta assim no Carbon HIToolbox, mas NAO se confia nele de memoria:
    /// o spike loga o keycode de TODA tecla justamente para confirmar empiricamente, e essa
    /// tabela e' a semente dos nomes canonicos de tecla da Fase 2 (lei 6).
    /// </summary>
    public const ushort kVK_F13 = 0x69;

    /// <summary>kVK_F14 — a tecla de depuracao que forca o timeout do tap.</summary>
    public const ushort kVK_F14 = 0x6B;

    // ---- CGEventTap ----

    /// <summary>
    /// A assinatura do callback do tap:
    /// <c>CGEventRef (*)(CGEventTapProxy, CGEventType, CGEventRef, void*)</c>.
    /// Devolver o proprio evento deixa passar; devolver NULL (IntPtr.Zero) ENGOLE.
    /// </summary>
    [DllImport(CG)]
    public static extern IntPtr CGEventTapCreate(
        uint tap, uint place, uint options, ulong eventsOfInterest,
        delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> callback, IntPtr userInfo);

    [DllImport(CG)]
    public static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(CG)]
    public static extern long CGEventGetIntegerValueField(IntPtr @event, uint field);

    [DllImport(CG)]
    public static extern void CGEventSetIntegerValueField(IntPtr @event, uint field, long value);

    [DllImport(CG)]
    public static extern uint CGEventGetType(IntPtr @event);

    // ---- CGEvent: criacao e postagem ----

    [DllImport(CG)]
    public static extern IntPtr CGEventSourceCreate(int stateID);

    [DllImport(CG)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey,
                                                           [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CG)]
    public static extern void CGEventKeyboardSetUnicodeString(IntPtr @event, nuint stringLength,
                                                              ushort* unicodeString);

    [DllImport(CG)]
    public static extern void CGEventPost(uint tap, IntPtr @event);

    // ---- CFRunLoop / CFMachPort ----

    [DllImport(CF)]
    public static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);

    [DllImport(CF)]
    public static extern IntPtr CFRunLoopGetMain();

    [DllImport(CF)]
    public static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CF)]
    public static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(CF)]
    public static extern void CFRunLoopRun();

    [DllImport(CF)]
    public static extern void CFRunLoopStop(IntPtr rl);

    [DllImport(CF)]
    public static extern void CFRelease(IntPtr cf);

    /// <summary>
    /// kCFRunLoopCommonModes. E' um CFStringRef global exportado, e nao uma funcao: precisa
    /// vir por dlsym + leitura do ponteiro guardado no endereco (ver Frameworks.Symbol).
    /// </summary>
    public static IntPtr CommonModes
        => Frameworks.Symbol(Frameworks.CoreFoundationHandle, "kCFRunLoopCommonModes");
}
