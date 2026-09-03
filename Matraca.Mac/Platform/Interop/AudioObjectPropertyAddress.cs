using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct AudioObjectPropertyAddress
{
    public readonly uint Selector;
    public readonly uint Scope;
    public readonly uint Element;

    public AudioObjectPropertyAddress(uint selector, uint scope, uint element)
    {
        Selector = selector;
        Scope = scope;
        Element = element;
    }
}
