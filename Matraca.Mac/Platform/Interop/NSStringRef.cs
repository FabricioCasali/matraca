using System.Runtime.InteropServices;
using System.Text;

namespace Matraca.Mac.Platform.Interop;

internal static class NSStringRef
{
    public static IntPtr From(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value + "\0");
        return ObjC.SendUtf8(
            ObjCClasses.NSString,
            ObjCSelectors.StringWithUTF8String,
            utf8);
    }

    public static string? To(IntPtr value)
    {
        if (value == IntPtr.Zero) return null;
        IntPtr utf8 = ObjC.Send(value, ObjCSelectors.UTF8String);
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }
}
