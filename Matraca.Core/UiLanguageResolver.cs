using System.Globalization;

namespace Matraca.Core;

public static class UiLanguageResolver
{
    public const string System = "system";
    public const string PortugueseBrazil = "pt-BR";
    public const string EnglishUnitedStates = "en-US";

    public static string Normalize(string? value)
        => (value ?? "").Trim().ToLowerInvariant() switch
        {
            System => System,
            "pt-br" => PortugueseBrazil,
            "en-us" => EnglishUnitedStates,
            _ => PortugueseBrazil,
        };

    public static string ResolveEffective(string? uiLanguage, string? systemLanguage = null)
    {
        var normalized = Normalize(uiLanguage);
        if (normalized != System) return normalized;

        var detected = (systemLanguage ?? CultureInfo.CurrentUICulture.Name).Trim();
        return detected.StartsWith("pt-", StringComparison.OrdinalIgnoreCase)
            ? PortugueseBrazil
            : EnglishUnitedStates;
    }
}
