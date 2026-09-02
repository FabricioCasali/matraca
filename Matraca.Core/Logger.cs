using System.Text;

namespace Matraca.Core;

public static class Logger
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(paths.DataDirectory);
                _path = paths.LogFile;
            }
            catch
            {
                _path = Path.Combine(AppContext.BaseDirectory, "matraca.log");
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERRO", message);

    public static void Error(string message, Exception exception) =>
        Write("ERRO", $"{message} :: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                if (_path != null)
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Logging must never bring down the app.
            }
        }
    }
}
