namespace WindowsUpdateProbe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Esta prova de instalacao exige Windows.");
            if (args.Length > 1 || args.Any(a => a is not ("--confirm-install" or "--confirm-self-update")))
                throw new ArgumentException("Uso: dotnet run -- [--confirm-install | --confirm-self-update]");
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "WindowsUpdateProbe.csproj")))
                root = root.Parent;
            if (root == null) throw new InvalidOperationException("Execute dentro do projeto do probe.");
            var run = Path.Combine(root.FullName, "artifacts", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            Console.WriteLine($"Artefatos: {run}");
            var selfUpdate = args.Contains("--confirm-self-update");
            var confirm = args.Contains("--confirm-install") || selfUpdate;
            var installer = confirm ? await ProbeInstaller.BuildAsync(root.FullName, run) : null;
            var package = installer == null
                ? System.Text.Encoding.UTF8.GetBytes("MT038 fixture: nao e executavel")
                : await File.ReadAllBytesAsync(installer.NewPackage);
            await ProbeSuite.RunAsync(run, package, installer, selfUpdate);
            Console.WriteLine("PASS: prova concluida.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
