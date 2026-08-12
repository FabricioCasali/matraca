using System.Runtime.InteropServices;

namespace Matraca.MacSpike.Interop;

/// <summary>
/// CGRect: origem + tamanho, quatro doubles, 32 bytes.
///
/// E' o exemplo canonico do risco de ABI deste projeto: no arm64 um retorno desse tamanho
/// volta por endereco indireto em x8, e um argumento desse tipo ocupa quatro slots do banco
/// de ponto flutuante (v0-v3). Declarar isto errado numa DllImport nao da erro — da' lixo.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;

    public CGRect(double x, double y, double w, double h)
    {
        Origin = new CGPoint(x, y);
        Size = new CGSize(w, h);
    }

    public override string ToString() => $"[{Origin} {Size}]";
}
