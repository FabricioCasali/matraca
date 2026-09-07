using System.Text.Json;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class ConfigPersistenceTests
{
    [Fact]
    public void AppearanceRoundTripsWithoutChangingExistingSettingsOrExposingCredentials()
    {
        var root = NewTemporaryRoot();
        try
        {
            var paths = AppPaths.ForMac(root);
            var raw = new RawConfig
            {
                hotkey = "Ctrl+Alt+D", mode = "hold", themeMode = "dark", palette = "teal",
                focusBorderColor = "#123456", postProcessModel = "local-model",
                postProcessProvider = "openai-compatible",
                postProcessApiKey = "test-anthropic", postProcessOpenAiApiKey = "test-openai",
                postProcessDeepSeekApiKey = "test-deepseek",
                micSensitivity = new() { ["Studio Mic"] = 0.03f },
            };
            Config.SaveRaw(paths, raw);
            var loaded = Config.LoadRaw(paths, "missing.json");
            Assert.Equal(JsonSerializer.Serialize(raw), JsonSerializer.Serialize(loaded));
            var config = Config.Load(paths, "missing.json");
            Assert.Equal("dark", config.ThemeMode);
            Assert.Equal("teal", config.Palette);
            var snapshot = ConfigSnapshot.Create(loaded);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
            Assert.Equal("dark", json.RootElement.GetProperty("themeMode").GetString());
            Assert.Equal("teal", json.RootElement.GetProperty("palette").GetString());
            Assert.True(json.RootElement.GetProperty("postProcessApiKeyConfigured").GetBoolean());
            Assert.DoesNotContain("test-", json.RootElement.GetRawText());
            Assert.Equal("#123456", json.RootElement.GetProperty("focusBorderColor").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LegacyConfigKeepsOptionalFieldsAbsentButSnapshotHasAppearanceDefaults()
    {
        var raw = JsonSerializer.Deserialize<RawConfig>("""{"mode":"push","history":false}""")!;
        var snapshot = ConfigSnapshot.Create(raw);
        Assert.Null(raw.themeMode);
        Assert.Null(raw.palette);
        Assert.Equal("system", snapshot["themeMode"]);
        Assert.Equal("olive", snapshot["palette"]);
        Assert.Equal("push", Config.FromRaw(raw).Mode);
        Assert.False(Config.FromRaw(raw).History);
    }

    [Fact]
    public void CurrentPackagedJsonLoadsAsAuthoritativeFirstRunConfiguration()
    {
        var root = NewTemporaryRoot();
        try
        {
            var paths = AppPaths.ForMac(root);
            var packaged = Path.Combine(AppContext.BaseDirectory, "packaged-appsettings.json");

            var config = Config.Load(paths, packaged);

            Assert.Equal("F15", config.HotkeyName);
            Assert.Equal("live", config.Mode);
            Assert.Equal("pt", config.Language);
            Assert.Equal("unicode", config.PasteMethod);
            Assert.Equal(450, config.SilenceMs);
            Assert.Equal(6, config.PhraseMaxSeconds);
            Assert.True(config.History);
            Assert.Equal(100, config.HistoryMaxItems);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void UserJsonTakesPrecedenceOverPackagedJson()
    {
        var root = NewTemporaryRoot();
        try
        {
            var paths = AppPaths.ForMac(root);
            Config.SaveRaw(paths, new RawConfig { hotkey = "Ctrl+Alt+D", mode = "hold" });

            var config = Config.Load(paths, Path.Combine(AppContext.BaseDirectory, "packaged-appsettings.json"));

            Assert.Equal("Ctrl+Alt+D", config.HotkeyName);
            Assert.Equal("hold", config.Mode);
            Assert.Equal("", config.ModelPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SavePreservesCompatibleCamelCaseJsonAndOmitsNulls()
    {
        var root = NewTemporaryRoot();
        try
        {
            var paths = AppPaths.ForMac(root);
            Config.SaveRaw(paths, new RawConfig
            {
                modelPath = "/models/whisper.bin",
                hotkey = "F15",
                pinHotkey = null,
                autoEnter = true,
                postProcessProvider = "openai-compatible",
                postProcessEndpoint = "http://localhost:11434/v1/chat/completions",
                postProcessOpenAiApiKey = "openai-secret",
            });

            using var document = JsonDocument.Parse(File.ReadAllText(paths.ConfigFile));
            var json = document.RootElement;
            Assert.Equal("/models/whisper.bin", json.GetProperty("modelPath").GetString());
            Assert.Equal("F15", json.GetProperty("hotkey").GetString());
            Assert.True(json.GetProperty("autoEnter").GetBoolean());
            Assert.Equal("openai-compatible", json.GetProperty("postProcessProvider").GetString());
            Assert.Equal(
                "http://localhost:11434/v1/chat/completions",
                json.GetProperty("postProcessEndpoint").GetString());
            Assert.Equal("openai-secret", json.GetProperty("postProcessOpenAiApiKey").GetString());
            Assert.False(json.TryGetProperty("pinHotkey", out _));
            Assert.Empty(Directory.EnumerateFiles(
                paths.DataDirectory,
                $".{Path.GetFileName(paths.ConfigFile)}.*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadIsCaseInsensitiveAndHandlesMalformedJson()
    {
        var root = NewTemporaryRoot();
        try
        {
            var paths = AppPaths.ForMac(root);
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(paths.ConfigFile, "{ \"HOTKEY\": \"F16\", \"MODE\": \"toggle\" }");
            var loaded = Config.Load(paths, "missing.json");
            Assert.Equal("F16", loaded.HotkeyName);
            Assert.Equal("toggle", loaded.Mode);

            File.WriteAllText(paths.ConfigFile, "{ invalid");
            var warnings = new List<string>();
            var fallback = Config.Load(paths, "missing.json", warning: warnings.Add);
            Assert.Equal("F15", fallback.HotkeyName);
            Assert.Equal("live", fallback.Mode);
            Assert.Single(warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "matraca-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
