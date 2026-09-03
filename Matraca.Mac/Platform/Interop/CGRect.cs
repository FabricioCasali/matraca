using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

/// <summary>A rectangle in AppKit's global, bottom-left-origin screen coordinate space.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGRect
{
    public CGRect(double x, double y, double width, double height)
    {
        Origin = new CGPoint(x, y);
        Size = new CGSize(width, height);
    }

    public readonly CGPoint Origin;
    public readonly CGSize Size;
}
