namespace Matraca.Core;

public sealed class AppPaths
{
    private AppPaths(string dataDirectory, char separator)
    {
        Separator = separator;
        DataDirectory = dataDirectory;
        ConfigFile = Combine(dataDirectory, separator, "appsettings.json");
        LogFile = Combine(dataDirectory, separator, "matraca.log");
        HistoryFile = Combine(dataDirectory, separator, "history.json");
        AiUsageFile = Combine(dataDirectory, separator, "ai-usage.json");
        ModelsDirectory = Combine(dataDirectory, separator, "models");
    }

    public string DataDirectory { get; }
    public string ConfigFile { get; }
    public string LogFile { get; }
    public string HistoryFile { get; }
    public string AiUsageFile { get; }
    public string ModelsDirectory { get; }

    private char Separator { get; }

    public string ResolvePath(string? configuredPath)
    {
        var value = (configuredPath ?? "").Trim();
        if (value.Length == 0) return "";

        value = value.Replace("%MATRACA_DATA%", DataDirectory, StringComparison.OrdinalIgnoreCase);
        value = Environment.ExpandEnvironmentVariables(value);
        return value.Replace(Separator == '/' ? '\\' : '/', Separator);
    }

    public static AppPaths ForWindows(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        return new AppPaths(Combine(localApplicationData, '\\', "Matraca"), '\\');
    }

    public static AppPaths ForMac(string homeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        var applicationSupport = Combine(homeDirectory, '/', "Library/Application Support");
        return new AppPaths(Combine(applicationSupport, '/', "Matraca"), '/');
    }

    public static AppPaths Current()
    {
        if (OperatingSystem.IsWindows())
            return ForWindows(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (OperatingSystem.IsMacOS())
            return ForMac(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        throw new PlatformNotSupportedException("Matraca supports Windows and macOS paths.");
    }

    private static string Combine(string root, char separator, string child)
        => root.TrimEnd('/', '\\') + separator + child.Replace('/', separator).TrimStart(separator);
}
