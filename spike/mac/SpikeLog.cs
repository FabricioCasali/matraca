using System.Diagnostics;

namespace Matraca.MacSpike;

/// <summary>
/// Log do spike: carimbo de tempo, thread, e saida dupla (stdout + arquivo).
///
/// A saida dupla existe porque as duas formas de rodar o .app precisam ser depuraveis:
/// direto pelo binario (stdout aparece no terminal) ou via <c>open -a</c> (stdout some, e
/// o arquivo e' a unica testemunha). O relogio comeca no carregamento da classe, e todo
/// registro traz o "+Nms" desde ali — e' assim que se mede a latencia de cada etapa do
/// pipeline sem instrumentar nada.
///
/// NAO USAR DENTRO DO CALLBACK DO EVENT TAP em caminho quente: escrever em arquivo aloca e
/// pode bloquear, e o callback do tap roda na run loop principal (lei 5). No spike o
/// volume e' baixo o bastante p/ o log direto valer a pena; na Fase 2 isso vira fila.
/// </summary>
internal static class SpikeLog
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static readonly string LogPath = ResolveLogPath();

    /// <summary>~/Library/Application Support/Matraca — o AppPaths que a Fase 1 formaliza.</summary>
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Matraca");

    private static string ResolveLogPath()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            return Path.Combine(DataDir, "spike.log");
        }
        catch { return Path.Combine(Path.GetTempPath(), "matraca-spike.log"); }
    }

    /// <summary>Milissegundos desde o inicio do processo — a regua de latencia do spike.</summary>
    public static long ElapsedMs => Clock.ElapsedMilliseconds;

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERRO", msg);

    public static void Error(string msg, Exception ex)
        => Write("ERRO", msg + " :: " + ex);

    private static void Write(string level, string msg)
    {
        int tid = Environment.CurrentManagedThreadId;
        string line = $"{DateTime.Now:HH:mm:ss.fff} +{Clock.ElapsedMilliseconds,6}ms [{level}] (t{tid}) {msg}";
        lock (Gate)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
            try { File.AppendAllText(LogPath, line + Environment.NewLine); }
            catch { /* o stdout ja' saiu; perder o arquivo nao justifica derrubar o spike */ }
        }
    }

    public static void Banner(string what)
    {
        Info(new string('=', 60));
        Info($"Matraca spike — {what}");
        Info($"log em: {LogPath}");
        Info(new string('=', 60));
    }
}
