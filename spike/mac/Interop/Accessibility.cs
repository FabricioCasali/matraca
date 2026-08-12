using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// A permissao de Acessibilidade — a que o event tap exige.
///
/// NAO EXISTE chave de Info.plist para ela (nao ha' NSAccessibilityUsageDescription). Ela e'
/// concedida a mao em Ajustes do Sistema -> Privacidade e Seguranca -> Acessibilidade. O que
/// o app pode fazer e' chamar <c>AXIsProcessTrustedWithOptions</c> com a opcao de prompt,
/// que abre o dialogo pedindo ao usuario para conceder.
///
/// O dicionario de opcoes e' montado com NSDictionary/NSNumber e passado onde a API pede um
/// CFDictionaryRef: NSDictionary e CFDictionary sao toll-free bridged, e' o mesmo ponteiro.
/// A chave kAXTrustedCheckOptionPrompt e' o CFString "AXTrustedCheckOptionPrompt".
/// </summary>
internal static class Accessibility
{
    private const string AS = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(AS)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    /// <param name="prompt">true abre o dialogo do sistema se ainda nao houver permissao.</param>
    public static bool IsTrusted(bool prompt)
    {
        Frameworks.EnsureLoaded();

        var key = NSStringRef.From("AXTrustedCheckOptionPrompt");
        var value = ObjC.SendWithBool(ObjCClasses.NSNumber, ObjCSelectors.NumberWithBool, prompt);
        var options = ObjC.Send(ObjCClasses.NSDictionary,
                                ObjCSelectors.DictionaryWithObjectForKey, value, key);
        return AXIsProcessTrustedWithOptions(options);
    }
}
