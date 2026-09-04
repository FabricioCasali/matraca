using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class LightThemeContrastTests
{
    [Fact]
    public void ManualAndAutomaticLightThemesShareAccessibleTokens()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "styles.css"));
        Dictionary<string, string> manual = ReadTokens(css, @":root\[data-theme=""light""\]");
        Dictionary<string, string> automatic = ReadTokens(css, @":root:not\(\[data-theme\]\)");
        string[] names =
        [
            "bg", "surface", "surface-strong", "line", "text", "muted", "faint",
            "violet", "cyan", "green", "red", "shadow",
        ];

        foreach (string name in names)
            Assert.Equal(manual[name], automatic[name]);

        var background = ParseHex(manual["bg"]);
        var surface = Composite(ParseRgba(manual["surface"]), background);
        var strongSurface = Composite(ParseRgba(manual["surface-strong"]), background);

        foreach (string name in new[] { "text", "muted", "faint", "violet", "cyan", "green", "red" })
        {
            var foreground = ParseHex(manual[name]);
            Assert.True(Contrast(foreground, background) >= 4.5, $"{name} sobre bg");
            Assert.True(Contrast(foreground, surface) >= 4.5, $"{name} sobre surface");
            Assert.True(Contrast(foreground, strongSurface) >= 4.5, $"{name} sobre surface-strong");
        }

        var line = ParseRgba(manual["line"]);
        Assert.True(Contrast(Composite(line, background), background) >= 3, "line sobre bg");
        Assert.True(Contrast(Composite(line, surface), surface) >= 3, "line sobre surface");
    }

    private static Dictionary<string, string> ReadTokens(string css, string selector)
    {
        Match block = Regex.Match(css, selector + @"\s*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, $"Bloco CSS ausente: {selector}");
        return Regex.Matches(block.Groups["body"].Value, @"--(?<name>[\w-]+):\s*(?<value>[^;]+);")
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => match.Groups["value"].Value.Trim(),
                StringComparer.Ordinal);
    }

    private static (double R, double G, double B) ParseHex(string value)
        => (
            Convert.ToInt32(value.Substring(1, 2), 16) / 255d,
            Convert.ToInt32(value.Substring(3, 2), 16) / 255d,
            Convert.ToInt32(value.Substring(5, 2), 16) / 255d);

    private static (double R, double G, double B, double A) ParseRgba(string value)
    {
        Match match = Regex.Match(
            value,
            @"rgba\((?<r>\d+),(?<g>\d+),(?<b>\d+),(?<a>\.\d+|\d+(?:\.\d+)?)\)");
        Assert.True(match.Success, $"Cor rgba invalida: {value}");
        return (
            Parse(match, "r") / 255d,
            Parse(match, "g") / 255d,
            Parse(match, "b") / 255d,
            Parse(match, "a"));
    }

    private static double Parse(Match match, string group)
        => double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static (double R, double G, double B) Composite(
        (double R, double G, double B, double A) foreground,
        (double R, double G, double B) background)
        => (
            foreground.R * foreground.A + background.R * (1 - foreground.A),
            foreground.G * foreground.A + background.G * (1 - foreground.A),
            foreground.B * foreground.A + background.B * (1 - foreground.A));

    private static double Contrast(
        (double R, double G, double B) first,
        (double R, double G, double B) second)
    {
        double lighter = Math.Max(Luminance(first), Luminance(second));
        double darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance((double R, double G, double B) color)
        => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(double value)
        => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
}
