using System.Reflection;
using System.Text.Json;

namespace WindowsUpdateProbe.Payload;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Dados exclusivos do pacote, ao lado de install/, nunca dados do Matraca.
        var root = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        var sentinel = Path.Combine(data, "sentinel.txt");
        if (!File.Exists(sentinel)) File.WriteAllText(sentinel, "MT038 fixture: acao, voz e texto integral.\n");
        var version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
        File.AppendAllText(Path.Combine(data, "launches.txt"), version + "\n");
        var launch = new
        {
            version,
            processId = Environment.ProcessId,
            executable = Environment.ProcessPath,
            startedAtUtc = DateTimeOffset.UtcNow,
            sentinel = File.ReadAllText(sentinel)
        };
        File.WriteAllText(Path.Combine(data, "last-launch.json"), JsonSerializer.Serialize(launch));
        if (args.SequenceEqual(new[] { "--reopened" }) && version == "2.0.0.0")
            SelfUpdate.WriteReport(root, "reopened.json", launch);
        else if (args.SequenceEqual(new[] { "--self-update", "--confirm-install" }) && version == "1.0.0.0")
            return await SelfUpdate.RunAsync(root);
        else if (args.Length != 0) return 2;
        return 0;
    }
}
