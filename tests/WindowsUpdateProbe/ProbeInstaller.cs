using System.Diagnostics;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;

namespace WindowsUpdateProbe;

internal sealed class ProbeInstaller(string run, string oldPackage, string newPackage)
{
    public string NewPackage { get; } = newPackage;
    private string InstallDir => Path.Combine(run, "install");
    private string DataDir => Path.Combine(run, "data");

    public static async Task<ProbeInstaller> BuildAsync(string root, string run)
    {
        var iscc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inno Setup 6", "ISCC.exe");
        if (!File.Exists(iscc)) throw new FileNotFoundException("Inno local ausente; nenhuma ferramenta sera instalada.", iscc);
        var packages = Path.Combine(run, "packages");
        Directory.CreateDirectory(packages);
        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var publish = Path.Combine(run, "payload-" + version);
            await ExecuteAsync("dotnet", root, "publish", Path.Combine(root, "Payload", "Payload.csproj"),
                "-c", "Release", "-r", "win-x64", "--self-contained", "false", "-p:Version=" + version,
                "-o", publish, "--source", "https://api.nuget.org/v3/index.json");
            await ExecuteAsync(iscc, root, "/Q", "/DProbeVersion=" + version,
                "/DRunId=" + Path.GetFileName(run), "/DInstallDir=" + Path.Combine(run, "install"),
                "/DPackageDir=" + packages, "/DPayloadDir=" + publish, Path.Combine(root, "Probe.iss"));
        }
        return new ProbeInstaller(run, Path.Combine(packages, "MT038-Probe-1.0.0.exe"), Path.Combine(packages, "MT038-Probe-2.0.0.exe"));
    }

    public async Task InstallAndVerifyAsync(string downloaded, SparkleUpdater updater, AppCastItem item)
    {
        ProbeSuite.Require(!Directory.Exists(InstallDir), "download automatico nao iniciou instalacao");
        Console.WriteLine("Confirmacao explicita --confirm-install recebida para os dois pacotes isolados.");
        await InstallAsync(oldPackage, "install-v1.log");
        await LaunchAsync("1.0.0.0");
        var sentinelPath = Path.Combine(DataDir, "sentinel.txt");
        // Modifica a fixture como faria o usuario; a versao nova deve preservar byte a byte.
        File.AppendAllText(sentinelPath, "Preferencia editada antes do upgrade: pt-BR / olive.\n");
        var before = File.ReadAllBytes(sentinelPath);
        ProbeSuite.Require(updater.SignatureVerifier.VerifySignatureOfFile(item.DownloadSignature!, downloaded) == ValidationResult.Valid,
            "pacote revalidado pela biblioteca imediatamente antes da instalacao confirmada");
        await InstallAsync(downloaded, "install-v2.log");
        ProbeSuite.Require(File.ReadAllBytes(sentinelPath).SequenceEqual(before), "dados preservados apos Inno 1.0.0 -> 2.0.0");
        await LaunchAsync("2.0.0.0");
        ProbeSuite.Require(File.ReadAllBytes(sentinelPath).SequenceEqual(before), "dados preservados apos reabertura");
        ProbeSuite.Require(File.ReadAllLines(Path.Combine(DataDir, "launches.txt")).SequenceEqual(new[] { "1.0.0.0", "2.0.0.0" }),
            "dois processos reais abriram as versoes antes/depois");
    }

    private Task InstallAsync(string package, string log) => ExecuteAsync(package, run,
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/LOG=" + Path.Combine(run, log));

    public async Task<SelfUpdateResult> ObserveSelfUpdateAsync(string appcastUrl, string publicKey)
    {
        var result = new SelfUpdateResult();
        try
        {
            await InstallAsync(oldPackage, "install-v1.log");
            var exe = Path.Combine(InstallDir, "MT038.Isolated.Payload.exe");
            ProbeSuite.Require(FileVersionInfo.GetVersionInfo(exe).FileVersion == "1.0.0.0", "baseline instalado v1 para autoatualizacao");
            Directory.CreateDirectory(DataDir);
            var sentinel = Path.Combine(DataDir, "sentinel.txt");
            File.WriteAllText(sentinel, "MT038 self-update: preferencia pt-BR; texto completo e preservado.\n");
            var before = File.ReadAllBytes(sentinel);
            var temp = Path.Combine(run, "helper-temp");
            Directory.CreateDirectory(temp);
            var start = new ProcessStartInfo(exe)
            {
                WorkingDirectory = InstallDir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true
            };
            start.ArgumentList.Add("--self-update");
            start.ArgumentList.Add("--confirm-install");
            start.Environment["TEMP"] = temp;
            start.Environment["TMP"] = temp;
            using var original = Process.Start(start) ?? throw new InvalidOperationException("v1 nao iniciou.");
            result.OriginalPid = original.Id;
            await original.StandardInput.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new { appcastUrl, publicKey }));
            original.StandardInput.Close();
            await original.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            result.OriginalExitCode = original.ExitCode;
            result.OriginalExitedAtUtc = original.ExitTime.ToUniversalTime();
            if (File.Exists(Path.Combine(run, "close-veto.json")))
            {
                using var veto = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(run, "close-veto.json")));
                result.CloseVetoVerified = veto.RootElement.GetProperty("processId").GetInt32() == original.Id
                    && veto.RootElement.GetProperty("helperAttempts").GetInt32() == 0
                    && !veto.RootElement.GetProperty("closeCalled").GetBoolean();
            }
            if (File.Exists(Path.Combine(run, "install-signature-recheck.json")))
            {
                using var recheck = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(run, "install-signature-recheck.json")));
                result.InstallSignatureRecheckVerified = recheck.RootElement.GetProperty("processId").GetInt32() == original.Id
                    && recheck.RootElement.GetProperty("failureReason").GetString() == "InvalidSignature"
                    && recheck.RootElement.GetProperty("helperAttempts").GetInt32() == 0;
            }
            result.CloseCallbackObserved = File.Exists(Path.Combine(run, "close-requested.json"));
            if (result.CloseCallbackObserved)
            {
                using var close = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(run, "close-requested.json")));
                result.HelperPid = close.RootElement.GetProperty("helperPid").GetInt32();
                using var gate = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(run, "close-gate.json")));
                result.WaitGateVerified = gate.RootElement.GetProperty("stayedWaiting").GetBoolean();
            }
            if (original.ExitCode != 0)
            {
                result.Failure = File.Exists(Path.Combine(run, "self-update-error.json"))
                    ? File.ReadAllText(Path.Combine(run, "self-update-error.json"))
                    : $"Payload terminou com codigo {original.ExitCode}.";
                return result;
            }
            ProbeSuite.Require(result.InstallSignatureRecheckVerified, "InstallUpdate rejeitou adulteracao posterior ao download sem iniciar helper");
            ProbeSuite.Require(result.CloseVetoVerified, "PreparingToExit vetou a primeira tentativa valida sem iniciar helper");
            ProbeSuite.Require(result.CloseCallbackObserved && result.WaitGateVerified, "helper aguardou gate com v1 vivo");
            var reopenedPath = Path.Combine(run, "reopened.json");
            var wait = Stopwatch.StartNew();
            while (!File.Exists(reopenedPath) && wait.Elapsed < TimeSpan.FromSeconds(100)) await Task.Delay(100);
            if (!File.Exists(reopenedPath)) throw new TimeoutException("Helper nao reabriu v2 em 100 segundos.");
            using var reopened = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reopenedPath));
            result.ReopenedPid = reopened.RootElement.GetProperty("processId").GetInt32();
            result.ReopenedVersion = reopened.RootElement.GetProperty("version").GetString();
            result.ReopenedAtUtc = reopened.RootElement.GetProperty("startedAtUtc").GetDateTimeOffset();
            var installLog = File.ReadAllText(Path.Combine(run, "install-v2.log"));
            var logDate = DateTime.ParseExact(installLog[..23], "yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            result.InstallerStartedAtUtc = DateTime.SpecifyKind(logDate, DateTimeKind.Local).ToUniversalTime();
            result.InstallerStartedAfterOriginalExit = result.InstallerStartedAtUtc >= result.OriginalExitedAtUtc;
            ProbeSuite.Require(result.InstallerStartedAfterOriginalExit && result.ReopenedAtUtc > result.InstallerStartedAtUtc,
                "horarios confirmam saida real v1 -> abertura log Inno -> reabertura v2");
            ProbeSuite.Require(installLog.Contains("Installation process succeeded.") && installLog.Contains("Administrative install mode: No"),
                "Inno do helper reportou sucesso e modo nao administrativo");
            result.DataPreserved = File.ReadAllBytes(sentinel).SequenceEqual(before);
            ProbeSuite.Require(result.ReopenedPid != result.OriginalPid && result.ReopenedVersion == "2.0.0.0", "helper real reabriu outro PID com v2");
            ProbeSuite.Require(FileVersionInfo.GetVersionInfo(exe).FileVersion == "2.0.0.0" && result.DataPreserved, "v2 instalado e dados preservados pelo ciclo do helper");
            ProbeSuite.Require(File.ReadAllLines(Path.Combine(DataDir, "launches.txt")).SequenceEqual(new[] { "1.0.0.0", "2.0.0.0" }), "somente v1 e v2 abriram no ciclo");
            result.Verified = true;
        }
        catch (Exception ex) { result.Failure = $"{ex.GetType().Name}: {ex.Message}"; }
        finally
        {
            File.WriteAllText(Path.Combine(run, "self-update-observation.json"), System.Text.Json.JsonSerializer.Serialize(result,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        }
        return result;
    }

    private async Task LaunchAsync(string expected)
    {
        var exe = Path.Combine(InstallDir, "MT038.Isolated.Payload.exe");
        ProbeSuite.Require(FileVersionInfo.GetVersionInfo(exe).FileVersion == expected, "versao do executavel instalado: " + expected);
        await ExecuteAsync(exe, InstallDir);
        using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "last-launch.json")));
        ProbeSuite.Require(report.RootElement.GetProperty("version").GetString() == expected, "processo reaberto reportou " + expected);
    }

    private static async Task ExecuteAsync(string executable, string workingDirectory, params string[] args)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Processo nao iniciou.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        var output = await stdout;
        var error = await stderr;
        if (!string.IsNullOrWhiteSpace(output)) Console.WriteLine(output.Trim());
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} retornou {process.ExitCode}: {error}");
    }
}
