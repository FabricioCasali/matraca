using System.Runtime.InteropServices;
using System.Text;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// Ponte string do C# &lt;-&gt; NSString.
///
/// Usa <c>+[NSString stringWithUTF8String:]</c>, que devolve objeto AUTORELEASED: ele vive
/// ate' o pool corrente drenar. Na thread principal o pool e' o da run loop, e isso basta.
/// Em thread de fundo e' preciso um <see cref="AutoreleasePool"/> em volta do trabalho,
/// senao vaza (ou pior: o objeto morre em hora imprevisivel).
///
/// NSString e' toll-free bridged com CFStringRef — o mesmo ponteiro serve as duas APIs, e e'
/// por isso que da' para montar um NSDictionary aqui e passa-lo onde a API pede
/// CFDictionaryRef, sem conversao nenhuma.
/// </summary>
internal static class NSStringRef
{
    /// <summary>C# -> NSString* (autoreleased).</summary>
    public static IntPtr From(string s)
    {
        var utf8 = Encoding.UTF8.GetBytes(s + "\0");
        return ObjC.SendUtf8(ObjCClasses.NSString, ObjCSelectors.StringWithUTF8String, utf8);
    }

    /// <summary>NSString* -> C#.</summary>
    public static string? To(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return null;
        var cstr = ObjC.Send(nsString, ObjCSelectors.UTF8String);
        return cstr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(cstr);
    }
}
