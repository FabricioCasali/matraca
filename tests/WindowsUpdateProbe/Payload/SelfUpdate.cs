using System.ComponentModel;
using System.Text.Json;
using NetSparkleUpdater;
using NetSparkleUpdater.AssemblyAccessors;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;

namespace WindowsUpdateProbe.Payload;

internal static class SelfUpdate
{
    public static void WriteReport(string root, string name, object report)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }

    public static async Task<int> RunAsync(string root)
    {
        try
        {
            // IPC de fixture publica; a chave privada permanece no controlador.
            using var config = JsonDocument.Parse(await Console.In.ReadLineAsync() ?? throw new InvalidOperationException("Fixture ausente no stdin."));
            var uri = new Uri(config.RootElement.GetProperty("appcastUrl").GetString()!);
            if (uri.Scheme != "http" || uri.Host != "127.0.0.1") throw new InvalidOperationException("Somente loopback permitido.");
            var publicKey = config.RootElement.GetProperty("publicKey").GetString()!;
            using var updater = new SparkleUpdater(uri.AbsoluteUri, new Ed25519Checker(SecurityMode.Strict, publicKey, publicKeyFile: null))
            {
                UIFactory = null,
                Configuration = new DefaultConfiguration(new AssemblyDiagnosticsAccessor(typeof(Program).Assembly.Location)),
                UserInteractionMode = UserInteractionMode.DownloadNoInstall,
                TmpDownloadFilePath = Path.Combine(root, "self-download"),
                CheckServerFileName = false,
                ShouldKillParentProcessWhenStartingInstaller = true,
                ProcessIDToKillBeforeInstallerRuns = Environment.ProcessId.ToString(),
                RelaunchAfterUpdate = true,
                RestartExecutablePath = AppContext.BaseDirectory,
                RestartExecutableName = "MT038.Isolated.Payload.exe",
                RelaunchAfterUpdateCommandSuffix = "--reopened",
                CustomInstallerArguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"{Path.Combine(root, "install-v2.log")}\""
            };
            var info = await updater.CheckForUpdatesQuietly().WaitAsync(TimeSpan.FromSeconds(30));
            if (info.Status != UpdateStatus.UpdateAvailable || info.Updates.Count != 1 || info.Updates[0].Version != "2.0.0")
                throw new InvalidOperationException("O v1 instalado nao encontrou o v2.");
            var item = info.Updates[0];
            var downloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            updater.DownloadFinished += (_, path) => downloaded.TrySetResult(path);
            updater.DownloadHadError += (_, _, error) => downloaded.TrySetException(error);
            updater.DownloadCanceled += (_, _) => downloaded.TrySetCanceled();
            await updater.InitAndBeginDownload(item);
            var path = await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            WriteReport(root, "self-download-validated.json", new { processId = Environment.ProcessId, version = updater.Configuration.InstalledVersion, path, validatedAtUtc = DateTimeOffset.UtcNow });

            bool mayClose = false;
            bool closeCalled = false;
            int helperAttempts = 0;
            string? failureReason = null;
            updater.PreparingToExit += (_, e) => e.Cancel = !mayClose;
            updater.InstallerProcessAboutToStart += (process, _) =>
            {
                helperAttempts++;
                // Observacao apenas: nao altera StartInfo nem o script da biblioteca.
                File.Copy(process.StartInfo.FileName, Path.Combine(root, "helper-original.cmd"), true);
                WriteReport(root, "helper-start-info.json", new
                {
                    process.StartInfo.FileName, process.StartInfo.UseShellExecute,
                    process.StartInfo.CreateNoWindow, process.StartInfo.WindowStyle,
                    parentPid = Environment.ProcessId, helperAttempts
                });
                return true;
            };
            updater.InstallUpdateFailed += (reason, failedPath) =>
            {
                failureReason = reason.ToString();
                WriteReport(root, "install-update-failed.json", new { reason = reason.ToString(), path = failedPath });
                return false; // Retorno ignorado pela API 3.1.0 (documentado no XML NuGet).
            };
            updater.CloseApplicationAsync += async () =>
            {
                closeCalled = true;
                WriteReport(root, "close-requested.json", new { processId = Environment.ProcessId, helperPid = updater.InstallerProcess!.Id, atUtc = DateTimeOffset.UtcNow });
                // Gate sintetico e limitado: mantem ESTE processo vivo para observar a espera.
                // Nao representa drenagem de audio ou trabalho real.
                await Task.Delay(TimeSpan.FromSeconds(2));
                bool stayedWaiting = !File.Exists(Path.Combine(root, "install-v2.log"));
                WriteReport(root, "close-gate.json", new { processId = Environment.ProcessId, stayedWaiting, atUtc = DateTimeOffset.UtcNow });
            };

            // Mudanca real do arquivo depois do DownloadFinished: InstallUpdate deve revalidar.
            var tamperedPath = Path.Combine(root, "self-download", "tampered.exe");
            var tampered = File.ReadAllBytes(path);
            tampered[0] ^= 1;
            File.WriteAllBytes(tamperedPath, tampered);
            mayClose = true;
            await updater.InstallUpdate(item, tamperedPath);
            if (failureReason != nameof(InstallUpdateFailureReason.InvalidSignature) || helperAttempts != 0 || closeCalled)
                throw new InvalidOperationException("InstallUpdate nao rejeitou pacote adulterado apos download.");
            WriteReport(root, "install-signature-recheck.json", new { processId = Environment.ProcessId, failureReason, helperAttempts, closeCalled });
            mayClose = false;

            // Veto real do PreparingToExit: o helper nem deve ser construido/iniciado.
            await updater.InstallUpdate(item, path);
            if (helperAttempts != 0 || closeCalled) throw new InvalidOperationException("Veto de encerramento ignorado.");
            WriteReport(root, "close-veto.json", new { processId = Environment.ProcessId, helperAttempts, closeCalled });
            mayClose = true; // Consentimento CLI ja recebido; gate sintetico liberado.
            await updater.InstallUpdate(item, path);
            if (!closeCalled) throw new InvalidOperationException("InstallUpdate nao solicitou encerramento; consultar eventos.");
            WriteReport(root, "self-exiting.json", new { processId = Environment.ProcessId, atUtc = DateTimeOffset.UtcNow });
            return 0; // Retorno normal de Main encerra somente este payload; nenhum Kill.
        }
        catch (Exception ex)
        {
            WriteReport(root, "self-update-error.json", new
            {
                processId = Environment.ProcessId, type = ex.GetType().FullName, ex.Message,
                ex.HResult, nativeErrorCode = (ex as Win32Exception)?.NativeErrorCode, atUtc = DateTimeOffset.UtcNow
            });
            return 2;
        }
    }
}
