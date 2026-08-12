using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// CGPoint. O CGFloat e' <c>double</c> em toda plataforma de 64 bits — nao existe variante
/// de 32 bits que nos interesse, entao nada de #if de arquitetura aqui.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X;
    public double Y;

    public CGPoint(double x, double y) { X = x; Y = y; }

    public override string ToString() => $"({X}, {Y})";
}
