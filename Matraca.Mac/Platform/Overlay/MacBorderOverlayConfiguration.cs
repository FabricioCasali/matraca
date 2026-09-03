using System.Globalization;

namespace Matraca.Mac.Platform.Overlay;

internal sealed class MacBorderOverlayConfiguration
{
    public MacBorderOverlayConfiguration(string color, int thickness, double opacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        string hex = color.Trim();
        if (hex.Length != 7 || hex[0] != '#'
            || !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
            throw new ArgumentException("Overlay color must use #RRGGBB format.", nameof(color));
        if (thickness < 1)
            throw new ArgumentOutOfRangeException(nameof(thickness), "Overlay thickness must be positive.");
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity), "Overlay opacity must be between 0 and 1.");

        Color = hex.ToUpperInvariant();
        Thickness = thickness;
        Opacity = opacity;
        Red = red / 255d;
        Green = green / 255d;
        Blue = blue / 255d;
    }

    public string Color { get; }
    public int Thickness { get; }
    public double Opacity { get; }
    internal double Red { get; }
    internal double Green { get; }
    internal double Blue { get; }
}
