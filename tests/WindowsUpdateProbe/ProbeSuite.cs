using NetSparkleUpdater;
using NetSparkleUpdater.AssemblyAccessors;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;

namespace WindowsUpdateProbe;

internal static class ProbeSuite
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync(string run, byte[] package, ProbeInstaller? installer, bool selfUpdate = false)
    {
        using var key = new TestSigningKey();
        await using var server = await FixtureServer.StartAsync();
        server.AddFeed("valid", package, key);
        server.AddFeed("bad-package", package, key, corruptPackage: true);
        server.AddFeed("bad-feed", package, key, corruptFeed: true);
        server.AddFeed("current", package, key, version: "1.0.0");
        server.AddFeed("cancel", new byte[2 * 1024 * 1024], key, slow: true);
        server.SavePublicFixtures(run, key.PublicKey);
        string cancellationOutcome;

        using (var bad = Create(server, "bad-feed", run, key))
        {
            var info = await bad.CheckForUpdatesQuietly().WaitAsync(TimeSpan.FromSeconds(30));
            Require(info.Status == UpdateStatus.CouldNotDetermine, "appcast adulterado rejeitado em Strict");
            Require(server.Requests("/bad-feed/package.exe") == 0, "appcast invalido nao iniciou download");
        }
        using (var current = Create(server, "current", run, key))
        {
            var info = await current.CheckForUpdatesQuietly().WaitAsync(TimeSpan.FromSeconds(30));
            Console.WriteLine($"Current: installed={current.Configuration.InstalledVersion}; status={info.Status}");
            Require(info.Status == UpdateStatus.UpdateNotAvailable, "versao igual nao oferece atualizacao");
        }
        using (var bad = Create(server, "bad-package", run, key))
        {
            var item = await FindUpdateAsync(bad);
            var result = await DownloadAsync(bad, item, false);
            Require(result.Outcome == "corrupt", "pacote adulterado rejeitado pelo Ed25519Checker real");
            // A lib pode conservar o arquivo invalido. Jamais tratamos existencia como autorizacao.
        }
        using (var cancel = Create(server, "cancel", run, key))
        {
            var result = await DownloadAsync(cancel, await FindUpdateAsync(cancel), true);
            cancellationOutcome = result.Outcome;
            Console.WriteLine($"CancelFileDownload: evento terminal observado = {result.Outcome}");
            Require(result.Outcome is "canceled" or "corrupt" or "error", "cancelamento durante transferencia nao liberou pacote");
            Require(!File.Exists(result.Path), "download parcial removido apos cancelamento");
        }
        using var valid = Create(server, "valid", run, key);
        var update = await FindUpdateAsync(valid);
        var downloaded = await DownloadAsync(valid, update, false);
        Require(downloaded.Outcome == "finished", "download valido sinalizado por DownloadFinished");
        Require(File.ReadAllBytes(downloaded.Path).SequenceEqual(package), "bytes baixados iguais ao pacote assinado");
        Require(server.Requests("/valid/appcast.xml.signature") > 0, "assinatura do appcast consultada via HTTP local");
        SelfUpdateResult? selfUpdateResult = null;
        if (installer == null)
        {
            Console.WriteLine("Instalacao nao autorizada: nenhum processo de instalador iniciado.");
        }
        else if (selfUpdate)
            selfUpdateResult = await installer.ObserveSelfUpdateAsync(server.BaseUrl + "/valid/appcast.xml", key.PublicKey);
        else await installer.InstallAndVerifyAsync(downloaded.Path, valid, update);
        File.WriteAllText(Path.Combine(run, "result.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            completedAtUtc = DateTimeOffset.UtcNow,
            netSparklePackage = "3.1.0",
            assertionsPassed = selfUpdateResult?.Verified ?? true,
            downloadAssertionsPassed = true,
            cancellationOutcome,
            cancellationEventContractSatisfied = cancellationOutcome == "canceled",
            innoInstallAndReopenVerified = installer != null && (!selfUpdate || selfUpdateResult!.Verified),
            versions = installer == null || selfUpdateResult?.Verified == false ? Array.Empty<string>() : new[] { "1.0.0.0", "2.0.0.0" },
            selfUpdateHelperVerified = selfUpdateResult?.Verified ?? false,
            selfUpdate = selfUpdateResult,
            macVerified = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        if (selfUpdateResult?.Verified == false) throw new InvalidOperationException("Helper real nao comprovado. Consulte self-update-observation.json e result.json.");
    }

    private static SparkleUpdater Create(FixtureServer server, string name, string run, TestSigningKey key) =>
        new(server.BaseUrl + $"/{name}/appcast.xml", new Ed25519Checker(SecurityMode.Strict, key.PublicKey, publicKeyFile: null))
        {
            UIFactory = null,
            Configuration = new DefaultConfiguration(new AssemblyDiagnosticsAccessor(typeof(Program).Assembly.Location)),
            UserInteractionMode = UserInteractionMode.DownloadNoInstall,
            TmpDownloadFilePath = Path.Combine(run, "downloads", name),
            CheckServerFileName = false,
            RelaunchAfterUpdate = false
        };

    private static async Task<AppCastItem> FindUpdateAsync(SparkleUpdater updater)
    {
        var info = await updater.CheckForUpdatesQuietly().WaitAsync(TimeSpan.FromSeconds(30));
        Require(info.Status == UpdateStatus.UpdateAvailable && info.Updates.Count == 1 && info.Updates[0].Version == "2.0.0",
            "NetSparkle selecionou 2.0.0 sobre assembly 1.0.0");
        return info.Updates[0];
    }

    private static async Task<(string Outcome, string Path)> DownloadAsync(SparkleUpdater updater, AppCastItem item, bool cancel)
    {
        var done = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        int accepted = 0;
        updater.DownloadFinished += (_, path) => { Interlocked.Increment(ref accepted); done.TrySetResult(("finished", path)); };
        updater.DownloadedFileIsCorrupt += (_, path) => done.TrySetResult(("corrupt", path));
        updater.DownloadCanceled += (_, path) => done.TrySetResult(("canceled", path));
        updater.DownloadHadError += (_, path, error) =>
        {
            if (cancel && path != null) done.TrySetResult(("error", path));
            else done.TrySetException(error);
        };
        int requested = 0;
        if (cancel)
        {
            updater.DownloadMadeProgress += (_, _, progress) =>
            {
                if (progress.BytesReceived > 0 && Interlocked.Exchange(ref requested, 1) == 0)
                    updater.CancelFileDownload();
            };
        }
        await updater.InitAndBeginDownload(item).WaitAsync(TimeSpan.FromSeconds(30));
        var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        // Aguarda o termino do callback antes de descartar o updater e confere eventos tardios.
        await Task.Delay(200);
        if (cancel) Require(requested == 1, "CancelFileDownload chamado apos receber bytes reais");
        Require(result.Item1 == "finished" ? accepted == 1 : accepted == 0, "somente download validado produz DownloadFinished");
        return result;
    }
}
