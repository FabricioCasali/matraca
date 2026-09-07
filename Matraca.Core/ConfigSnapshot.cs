using System.Text.Json;

namespace Matraca.Core;

public static class ConfigSnapshot
{
    // Preserve raw settings while keeping credentials out of the public bridge payload.
    public static Dictionary<string, object?> Create(RawConfig raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var result = new Dictionary<string, object?>();
        foreach (JsonProperty property in JsonSerializer.SerializeToElement(raw).EnumerateObject())
        {
            if (property.Name is nameof(RawConfig.postProcessApiKey)
                or nameof(RawConfig.postProcessOpenAiApiKey)
                or nameof(RawConfig.postProcessDeepSeekApiKey)) continue;
            result[property.Name] = property.Value.Clone();
        }

        string provider = Config.NormalizePostProcessProvider(raw.postProcessProvider);
        result["postProcessProvider"] = provider;
        result["themeMode"] = Config.NormalizeThemeMode(raw.themeMode);
        result["palette"] = Config.NormalizePalette(raw.palette);
        result["postProcessApiKeyConfigured"] = !string.IsNullOrWhiteSpace(provider switch
        {
            "openai-compatible" => raw.postProcessOpenAiApiKey,
            "deepseek" => raw.postProcessDeepSeekApiKey,
            "anthropic" => raw.postProcessApiKey,
            _ => null,
        });
        return result;
    }
}
