using System.Text;

namespace Matraca;

/// <summary>Log simples na pasta gravavel do usuario.</summary>
internal static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _path = ResolvePath();

    private static string ResolvePath()
    {
        try
        {
            var paths = AppPaths.Current();
            Directory.CreateDirectory(paths.DataDirectory);
            return paths.LogFile;
        }
        catch { return Path.Combine(AppContext.BaseDirectory, "matraca.log"); }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERRO", msg);

    public static void Error(string msg, Exception ex) =>
        Write("ERRO", $"{msg} :: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}";
        lock (_lock)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* nunca derrubar o app por causa de log */ }
        }
    }
}
