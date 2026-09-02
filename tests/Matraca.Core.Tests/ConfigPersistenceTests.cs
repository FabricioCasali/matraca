using System.Text.Json;
using Xunit;

namespace Matraca.Core.Tests;

public sealed class ConfigPersistenceTests
{
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
            });

            using var document = JsonDocument.Parse(File.ReadAllText(paths.ConfigFile));
            var json = document.RootElement;
            Assert.Equal("/models/whisper.bin", json.GetProperty("modelPath").GetString());
            Assert.Equal("F15", json.GetProperty("hotkey").GetString());
            Assert.True(json.GetProperty("autoEnter").GetBoolean());
            Assert.False(json.TryGetProperty("pinHotkey", out _));
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
