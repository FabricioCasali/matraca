using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>CGSize (dois CGFloat = dois doubles).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CGSize
{
    public double Width;
    public double Height;

    public CGSize(double w, double h) { Width = w; Height = h; }

    public override string ToString() => $"{Width}x{Height}";
}
