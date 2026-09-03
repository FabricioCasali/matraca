using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGPoint
{
    public CGPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public readonly double X;
    public readonly double Y;
}
