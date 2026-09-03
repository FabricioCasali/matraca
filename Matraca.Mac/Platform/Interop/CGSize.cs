using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGSize
{
    public CGSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public readonly double Width;
    public readonly double Height;
}
