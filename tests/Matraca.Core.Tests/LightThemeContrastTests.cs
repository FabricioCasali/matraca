using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class LightThemeContrastTests
{
    [Theory]
    [InlineData("light", 125)] [InlineData("dark", 125)]
    [InlineData("light", 85)] [InlineData("dark", 85)]
    [InlineData("light", 35)] [InlineData("dark", 35)]
    [InlineData("light", 325)] [InlineData("dark", 325)]
    [InlineData("light", 175)] [InlineData("dark", 175)]
    public void DesignThemesKeepSemanticPairsAccessible(string mode, double hue)
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design-tokens.css"));
        Dictionary<string, string> tokens = ReadTokens(css, @":root");
        if (mode == "dark")
            foreach (var pair in ReadTokens(css, @":root\[data-theme=""dark""\]")) tokens[pair.Key] = pair.Value;

        foreach (string surface in new[] { "bg", "surface", "soft" })
        {
            Check("fg", surface, 4.5);
            Check("subtle", surface, 4.5);
        }
        Check("on-accent", "action-bg", 4.5);
        Check("on-accent", "accent-strong", 4.5);
        Check("brand-ink", "brand-soft", 4.5);
        Check("success", "success-bg", 4.5);
        Check("danger", "danger-bg", 4.5);
        Check("control-border", "surface", 3);

        void Check(string foreground, string background, double minimum)
        {
            double a = Luminance(Color(tokens, foreground, hue));
            double b = Luminance(Color(tokens, background, hue));
            double ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            Assert.True(ratio >= minimum, $"{mode}/{hue}: {foreground} on {background}: {ratio:F3}");
        }
    }

    [Fact]
    public void AutomaticDarkModeUsesTheSameTokensAsExplicitDarkMode()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "design-tokens.css"));
        var explicitTokens = ReadTokens(css, @":root\[data-theme=""dark""\]");
        var automaticTokens = ReadTokens(css, @":root:not\(\[data-theme\]\)");
        Assert.Equal(explicitTokens.OrderBy(pair => pair.Key), automaticTokens.OrderBy(pair => pair.Key));
    }

    private static Dictionary<string, string> ReadTokens(string css, string selector)
    {
        Match block = Regex.Match(css, selector + @"\s*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, $"Missing selector: {selector}");
        return Regex.Matches(block.Groups["body"].Value, @"--(?<name>[\w-]+):\s*(?<value>[^;]+);")
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value.Trim());
    }

    private static (double R, double G, double B) Color(Dictionary<string, string> tokens, string name, double hue)
    {
        string value = tokens[name];
        if (value.StartsWith("var(--")) return Color(tokens, value[6..^1], hue);
        value = value.Replace("var(--palette-hue)", hue.ToString(CultureInfo.InvariantCulture));
        Match match = Regex.Match(value, @"oklch\((?<l>[\d.]+)% (?<c>[\d.]+) (?<h>[\d.]+)\)");
        Assert.True(match.Success, $"Unsupported color: {value}");
        double l = double.Parse(match.Groups["l"].Value, CultureInfo.InvariantCulture) / 100;
        double c = double.Parse(match.Groups["c"].Value, CultureInfo.InvariantCulture);
        double h = double.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) * Math.PI / 180;
        double a = c * Math.Cos(h), b = c * Math.Sin(h);
        double ll = Math.Pow(l + .3963377774 * a + .2158037573 * b, 3);
        double mm = Math.Pow(l - .1055613458 * a - .0638541728 * b, 3);
        double ss = Math.Pow(l - .0894841775 * a - 1.291485548 * b, 3);
        // Convert OKLCH into clipped, quantized sRGB, then measure the actual display pair.
        return (Srgb(4.0767416621 * ll - 3.3077115913 * mm + .2309699292 * ss),
            Srgb(-1.2684380046 * ll + 2.6097574011 * mm - .3413193965 * ss),
            Srgb(-.0041960863 * ll - .7034186147 * mm + 1.707614701 * ss));
    }

    private static double Srgb(double value)
    {
        value = value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
        return Math.Round(Math.Clamp(value, 0, 1) * 255) / 255;
    }
    private static double Linear(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    private static double Luminance((double R, double G, double B) color)
        => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
}
