using System.Text;

namespace Matraca;

/// <summary>Log simples em arquivo ao lado do executavel (matraca.log).</summary>
internal static class Logger
{
    private static readonly object _lock = new();
    private static readonly string _path = ResolvePath();

    /// <summary>Pasta gravavel do usuario (%LOCALAPPDATA%\Matraca). Necessario porque, sob
    /// uiAccess, o exe roda de Program Files em integridade media e nao escreve la.</summary>
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Matraca");

    private static string ResolvePath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Matraca");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "matraca.log");
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
