using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class Accessibility
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    public static bool IsTrusted(bool prompt)
    {
        IntPtr key = NSStringRef.From("AXTrustedCheckOptionPrompt");
        IntPtr value = ObjC.SendWithBool(
            ObjCClasses.NSNumber,
            ObjCSelectors.NumberWithBool,
            prompt);
        IntPtr options = ObjC.Send(
            ObjCClasses.NSDictionary,
            ObjCSelectors.DictionaryWithObjectForKey,
            value,
            key);
        return AXIsProcessTrustedWithOptions(options);
    }
}
