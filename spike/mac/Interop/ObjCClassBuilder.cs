namespace Matraca.MacSpike.Interop;

/// <summary>
/// Registra uma classe Objective-C em runtime, a partir de C#. E' o unico jeito que este
/// projeto usa para ter "delegates" ObjC (metodos que o AppKit chama de volta em nos).
///
/// A receita e' sempre a mesma, nesta ordem:
///   1. objc_allocateClassPair(superclasse, nome, 0)
///   2. class_addMethod(cls, seletor, imp, tiposObjC)  — uma vez por metodo
///   3. objc_registerClassPair(cls)
/// Depois de registrada a classe nao aceita mais metodos novos.
///
/// O <c>imp</c> vem de um metodo static marcado <c>[UnmanagedCallersOnly]</c>, endereçado
/// com <c>&amp;Metodo</c>. Isso e' PREFERIVEL a Marshal.GetFunctionPointerForDelegate: o
/// ponteiro e' estatico, entao nao existe delegate para manter vivo contra o coletor — que
/// e' exatamente a categoria de bug que o HotkeyListener do Windows precisa comentar em
/// linha (o campo _proc existe so' para isso).
///
/// Toda [UnmanagedCallersOnly] recebe <c>IntPtr self, IntPtr _cmd</c> como dois primeiros
/// parametros, tem assinatura blittable, e e' envolvida INTEIRA num try/catch: excecao
/// atravessando essa fronteira derruba o processo sem stack trace util.
///
/// Sobre a string de tipos ("v@:", "B@:"): o runtime a usa para introspeccao e forwarding,
/// nao para a chamada em si. No arm64 usa-se 'B' para retorno booleano.
/// </summary>
internal sealed class ObjCClassBuilder
{
    private readonly string _name;
    private IntPtr _cls;
    private bool _registered;

    private ObjCClassBuilder(string name, IntPtr cls)
    {
        _name = name;
        _cls = cls;
    }

    /// <summary>
    /// Comeca uma classe nova. Se ja' existir uma com esse nome (segunda chamada no mesmo
    /// processo), devolve a existente ja' marcada como registrada — registrar duas vezes
    /// aborta o processo.
    /// </summary>
    public static ObjCClassBuilder Create(string name, IntPtr superclass)
    {
        var existing = ObjC.objc_getClass(name);
        if (existing != IntPtr.Zero)
            return new ObjCClassBuilder(name, existing) { _registered = true };

        var cls = ObjC.objc_allocateClassPair(superclass, name, 0);
        if (cls == IntPtr.Zero)
            throw new InvalidOperationException(
                $"objc_allocateClassPair falhou para \"{name}\" (superclasse nil ou nome em uso).");
        return new ObjCClassBuilder(name, cls);
    }

    public ObjCClassBuilder AddMethod(IntPtr selector, IntPtr imp, string objcTypes)
    {
        if (_registered) return this;   // classe reaproveitada: os metodos ja' estao la'
        if (!ObjC.class_addMethod(_cls, selector, imp, objcTypes))
            throw new InvalidOperationException(
                $"class_addMethod falhou em \"{_name}\" (tipos \"{objcTypes}\").");
        return this;
    }

    public IntPtr Register()
    {
        if (!_registered)
        {
            ObjC.objc_registerClassPair(_cls);
            _registered = true;
            SpikeLog.Info($"classe ObjC registrada em runtime: {_name}");
        }
        return _cls;
    }
}
