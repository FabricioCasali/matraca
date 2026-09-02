using System.Text.Json;
using Matraca.Core;

namespace Matraca.Mac.Config;

internal static class MacConfig
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static AppPaths Paths { get; } = AppPaths.Current();

    private static string PackagedConfigFile
        => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static Matraca.Core.Config Load()
    {
        EnsureUserConfig();
        string json = File.ReadAllText(Paths.ConfigFile);
        RawConfig raw = JsonSerializer.Deserialize<RawConfig>(json, ReadOptions)
            ?? throw new JsonException("appsettings.json is empty.");
        return Matraca.Core.Config.FromRaw(raw, warning: Logger.Warn, paths: Paths);
    }

    private static void EnsureUserConfig()
    {
        if (File.Exists(Paths.ConfigFile)) return;
        Directory.CreateDirectory(Paths.DataDirectory);
        if (File.Exists(PackagedConfigFile))
            File.Copy(PackagedConfigFile, Paths.ConfigFile, overwrite: false);
        else
            Matraca.Core.Config.SaveRaw(Paths, new RawConfig());
    }
}
