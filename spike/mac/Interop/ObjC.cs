using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// A fronteira com o runtime do Objective-C.
///
/// A REGRA CENTRAL DESTE ARQUIVO, e ela nao tem excecao: UMA DllImport POR ASSINATURA
/// NATIVA. O <c>objc_msgSend</c> e' declarado como variadico no cabecalho da Apple, mas
/// NAO E' — o chamador precisa fazer o cast para o tipo exato da funcao antes de chamar.
///
/// O motivo esta' na ABI do arm64 (AAPCS64):
///   * inteiros e ponteiros vao em x0-x7;
///   * float e double vao em v0-v7, um banco de registradores SEPARADO;
///   * struct grande de retorno (uma CGRect sao 4 doubles = 32 bytes) volta por endereco
///     indireto em x8.
///
/// Uma DllImport que declara IntPtr num parametro que na verdade e' double NAO da erro de
/// compilacao nem excecao: passa lixo, silenciosamente, porque o valor foi posto no banco
/// de registradores errado. E' o mesmo formato de falha da lei 5 do projeto — ausencia de
/// comportamento, e nao erro.
///
/// Por isso: sobrecarga por TIPO DE PARAMETRO e' permitida (o C# resolve na compilacao);
/// o que nao pode e' uma assinatura generica de IntPtr servindo a todas.
///
/// SOBRE _stret E _fpret: no arm64 elas NAO EXISTEM. Ha' um unico objc_msgSend, e os
/// cabecalhos da Apple marcam as variantes como indisponiveis nesta arquitetura — a ABI
/// ja' resolve retorno de struct por x8 e retorno de float por v0. Nao escreva codigo
/// condicional de arquitetura para isso. (Se o x86_64 voltar a mesa um dia, ai' sim: em
/// x86_64 uma CGRect de retorno exige objc_msgSend_stret.)
/// </summary>
internal static unsafe class ObjC
{
    private const string Lib = "/usr/lib/libobjc.A.dylib";

    // ---- runtime: classes, seletores, registro de classe em runtime ----

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    public static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [DllImport(Lib)]
    public static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(Lib, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    // ---- objc_msgSend: uma entrada por assinatura ----

    /// <summary>[recv sel] devolvendo id.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr recv, IntPtr sel);

    /// <summary>[recv sel:a] devolvendo id.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr recv, IntPtr sel, IntPtr a);

    /// <summary>[recv sel:a b:b] devolvendo id.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr recv, IntPtr sel, IntPtr a, IntPtr b);

    /// <summary>[recv sel:cstring] — usado por stringWithUTF8String:.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend", CharSet = CharSet.Ansi)]
    public static extern IntPtr SendUtf8(IntPtr recv, IntPtr sel, byte[] utf8);

    /// <summary>[recv sel] sem retorno.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr recv, IntPtr sel);

    /// <summary>[recv sel:a] sem retorno.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr recv, IntPtr sel, IntPtr a);

    /// <summary>[recv sel:a b:b] sem retorno — setValue:forKey:, por exemplo.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr recv, IntPtr sel, IntPtr a, IntPtr b);

    /// <summary>
    /// [recv sel:BOOL]. O BOOL do Objective-C e' 1 BYTE; o bool do C# em P/Invoke e'
    /// marshalado por padrao como BOOL do Win32, que sao 4 BYTES. Sem o
    /// [MarshalAs(UnmanagedType.I1)] o valor chega errado — um setIgnoresMouseEvents: assim
    /// e' bug de meia hora. Regra sem excecao: todo BOOL que atravessa a fronteira leva I1,
    /// ou e' declarado como byte.
    /// </summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr recv, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool a);

    /// <summary>[recv sel:NSInteger] — setLevel:, por exemplo.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendVoidNInt(IntPtr recv, IntPtr sel, nint a);

    /// <summary>[recv sel:NSUInteger] — setCollectionBehavior:, setActivationPolicy:.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendVoidNUInt(IntPtr recv, IntPtr sel, nuint a);

    /// <summary>
    /// [recv sel:BOOL] devolvendo id — +[NSNumber numberWithBool:]. Repare que NAO da' para
    /// reaproveitar uma sobrecarga de nint aqui: o parametro e' BOOL de 1 byte, e a regra do
    /// topo vale tambem quando "funcionaria por acidente".
    /// </summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendWithBool(IntPtr recv, IntPtr sel,
                                             [MarshalAs(UnmanagedType.I1)] bool a);

    /// <summary>
    /// [recv sel:double]. Existe SEPARADA de propósito: um double passado por uma
    /// sobrecarga de IntPtr iria para x0 em vez de v0, e a chamada leria lixo. Ver o
    /// comentario do topo.
    /// </summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendVoidDouble(IntPtr recv, IntPtr sel, double a);

    /// <summary>[recv sel] devolvendo BOOL (1 byte).</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SendBool(IntPtr recv, IntPtr sel);

    /// <summary>[recv sel] devolvendo CGRect (volta por x8 no arm64).</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern CGRect SendRect(IntPtr recv, IntPtr sel);

    /// <summary>[recv sel:CGRect] devolvendo id — initWithFrame:.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendRectArg(IntPtr recv, IntPtr sel, CGRect frame);

    /// <summary>[recv sel:CGRect configuration:id] — WKWebView initWithFrame:configuration:.</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendInitWebView(IntPtr recv, IntPtr sel, CGRect frame, IntPtr config);

    /// <summary>
    /// [NSWindow initWithContentRect:styleMask:backing:defer:]. Quatro tipos diferentes numa
    /// chamada so' — e' o exemplo canonico de por que a assinatura precisa ser exata.
    /// </summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendInitWindow(IntPtr recv, IntPtr sel, CGRect frame,
                                               nuint style, nuint backing,
                                               [MarshalAs(UnmanagedType.I1)] bool defer);

    /// <summary>[recv performSelectorOnMainThread:withObject:waitUntilDone:].</summary>
    [DllImport(Lib, EntryPoint = "objc_msgSend")]
    public static extern void SendPerformOnMain(IntPtr recv, IntPtr sel, IntPtr selToRun,
                                                IntPtr arg,
                                                [MarshalAs(UnmanagedType.I1)] bool waitUntilDone);

    // ---- acucar ----

    /// <summary>Instancia crua: [[Cls alloc] init].</summary>
    public static IntPtr New(IntPtr cls)
        => Send(Send(cls, ObjCSelectors.Alloc), ObjCSelectors.Init);
}
