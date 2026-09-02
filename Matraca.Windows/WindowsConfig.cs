namespace Matraca;

internal static class WindowsConfig
{
    public static AppPaths Paths { get; } = AppPaths.Current();

    private static string PackagedConfigFile => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static Config Load()
    {
        MigrateLegacyConfig();
        return Config.Load(Paths, PackagedConfigFile, WindowsHotkeyTranslator.ParseCompatibility, Logger.Warn);
    }

    public static RawConfig LoadRaw()
        => Config.LoadRaw(Paths, PackagedConfigFile, Logger.Warn);

    public static void SaveRaw(RawConfig raw)
    {
        Config.SaveRaw(Paths, raw);
        Logger.Info($"Config salva em {Paths.ConfigFile}");
    }

    private static void MigrateLegacyConfig()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile)) return;
            var parent = Path.GetDirectoryName(Paths.DataDirectory)!;
            var legacy = Path.Combine(parent, "Ditador", "appsettings.json");
            if (!File.Exists(legacy)) return;

            Directory.CreateDirectory(Paths.DataDirectory);
            File.Copy(legacy, Paths.ConfigFile);
            Logger.Info($"Config migrada da instalacao antiga (Ditador): {legacy} -> {Paths.ConfigFile}");
        }
        catch (Exception ex)
        {
            Logger.Warn("Falha ao migrar config antiga: " + ex.Message);
        }
    }
}
