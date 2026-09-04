using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Config;
using Matraca.Mac.Platform;
using Matraca.Mac.Platform.Audio;
using Matraca.Mac.Platform.Interop;
using Matraca.Mac.Platform.Keyboard;
using Matraca.Mac.Platform.Speech;
using Matraca.Mac.Platform.Text;
using Matraca.Mac.Platform.Web;

namespace Matraca.Mac;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Matraca.Mac requires macOS.");

        Logger.Initialize(AppPaths.Current());
        try
        {
            if (args.Length > 0 && args[0] == "--delivery-smoke")
                return RunDeliverySmoke(args);
            if (args.Length > 0 && args[0] == "--audio-smoke")
                return RunAudioSmoke(args).GetAwaiter().GetResult();
            if (args.Length > 0 && args[0] == "--whisper-smoke")
                return RunWhisperSmoke(args).GetAwaiter().GetResult();
            if (args.Length > 0 && args[0] == "--target-smoke")
                return MacTargetSmoke.Run(args);
            if (args.Length > 0 && args[0] == "--indicator-smoke")
                return MacIndicatorSmoke.Run(args);
            if (args.Length > 0 && args[0] == "--webview-smoke")
                return MacWebViewSmoke.Run(args);
            if (args.Length > 0 && args[0] == "--window-smoke")
                return MacWebWindowSmoke.Run(args);
            if (args.Length > 0 && args[0] == "--keyboard-capture-smoke")
                return MacKeyboardCaptureSmoke.Run(args);

            using MacSingleInstance? singleInstance = MacSingleInstance.TryAcquire(AppPaths.Current());
            if (singleInstance == null)
            {
                Logger.Info("Outra instancia do Matraca ja esta ativa para este usuario.");
                return 0;
            }

            Frameworks.EnsureLoaded();
            ObjCClasses.Warm();
            using var pool = AutoreleasePool.New();

            var application = MacApplication.Shared();
            application.ConfigureAsAccessory();
            MainThread.Initialize();
            using var statusItem = new MacStatusItem(application);
            Matraca.Core.Config config = MacConfig.Load();
            bool trusted = Accessibility.IsTrusted(prompt: true);
            using MacTrayApp? trayApp = trusted ? new MacTrayApp(config, statusItem) : null;
            using var bridge = new MacWebBridge(trayApp);
            using var webWindow = new MacWebWindowController(
                application,
                statusItem,
                WebAssetRoot.Resolve(),
                bridge);
            using MacHudWindowController? hudWindow = trayApp == null
                ? null
                : new MacHudWindowController(trayApp, WebAssetRoot.Resolve());
            using var termination = new MacTerminationHandshake(
                application,
                async cancellation =>
                {
                    await bridge.ShutdownAsync(cancellation).ConfigureAwait(false);
                    if (trayApp != null)
                        await trayApp.ShutdownAsync(cancellation).ConfigureAwait(false);
                });
            if (trayApp != null)
            {
                trayApp.Start();
                Logger.Info("Matraca.Mac iniciado como app Accessory.");
            }
            else
            {
                statusItem.SetState("Matraca !", "Acessibilidade necessaria; conceda e reinicie o Matraca.");
                Logger.Warn("Permissao de Acessibilidade ausente; conceda e reinicie o Matraca.");
            }
            application.Run();
            termination.RequestShutdownAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception exception)
        {
            Logger.Error("Falha fatal no Matraca.Mac", exception);
            return 1;
        }
    }

    private static int RunDeliverySmoke(string[] args)
    {
        TextDeliveryMethod method = args.Length > 1 && args[1] == "clipboard"
            ? TextDeliveryMethod.Clipboard
            : TextDeliveryMethod.Unicode;
        if (args.Length != 6
            || args[1] is not ("unicode" or "clipboard")
            || !bool.TryParse(args[3], out bool pressEnter)
            || !int.TryParse(args[4], out int delayMs)
            || delayMs < 0)
        {
            Logger.Error(
                "Uso: --delivery-smoke <unicode|clipboard> <texto> <true|false> <delay-ms> <resultado>.");
            return 2;
        }

        Frameworks.EnsureLoaded();
        ObjCClasses.Warm();
        using var pool = AutoreleasePool.New();
        if (!Accessibility.IsTrusted(prompt: false))
        {
            File.WriteAllText(args[5], TextDeliveryResult.Failed.ToString());
            Logger.Error("Smoke de entrega sem permissao de Acessibilidade.");
            return 1;
        }

        Thread.Sleep(delayMs);
        using var sink = new MacTextSink();
        TextDeliveryResult result = sink.DeliverAsync(new TextDeliveryRequest(
                args[2],
                pressEnter,
                method))
            .GetAwaiter().GetResult();
        File.WriteAllText(args[5], result.ToString());
        return result == TextDeliveryResult.Delivered ? 0 : 1;
    }

    private static async Task<int> RunAudioSmoke(string[] args)
    {
        if (args.Length != 3
            || !int.TryParse(args[1], out int durationMilliseconds)
            || durationMilliseconds is < 250 or > 10000)
        {
            Logger.Error("Uso: --audio-smoke <duracao-ms: 250..10000> <resultado-json>.");
            return 2;
        }

        string resultPath = args[2];
        try
        {
            Frameworks.EnsureLoaded();
            using var capture = new MacAudioCapture();
            IReadOnlyList<string> devices = capture.ListDevices();
            string configuredDevice = devices.FirstOrDefault()
                ?? throw new InvalidOperationException("CoreAudio nao enumerou nenhum microfone.");
            int frameCount = 0;
            long streamedSamples = 0;
            capture.FrameCaptured += frame =>
            {
                Interlocked.Increment(ref frameCount);
                Interlocked.Add(ref streamedSamples, frame.Length);
            };

            await capture.StartAsync(null, TimeSpan.Zero).ConfigureAwait(false);
            await Task.Delay(durationMilliseconds).ConfigureAwait(false);
            float[] samples = await capture.StopAsync().ConfigureAwait(false);
            float rms = AudioLevelAnalyzer.CalculateRms(samples);
            float peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
            int firstFrameCount = frameCount;
            long firstStreamedSamples = streamedSamples;

            Interlocked.Exchange(ref frameCount, 0);
            Interlocked.Exchange(ref streamedSamples, 0);
            await capture.StartAsync(configuredDevice, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            await Task.Delay(600).ConfigureAwait(false);
            float[] restartSamples = await capture.StopAsync().ConfigureAwait(false);
            int restartFrameCount = frameCount;
            long restartStreamedSamples = streamedSamples;

            Interlocked.Exchange(ref frameCount, 0);
            Interlocked.Exchange(ref streamedSamples, 0);
            const string missingDevice = "Matraca smoke: disconnected microphone";
            await capture.StartAsync(missingDevice, TimeSpan.Zero).ConfigureAwait(false);
            await Task.Delay(600).ConfigureAwait(false);
            float[] fallbackSamples = await capture.StopAsync().ConfigureAwait(false);
            bool success = samples.Length > 0
                && firstFrameCount > 0
                && firstStreamedSamples == samples.Length
                && rms > 0
                && restartSamples.Length > 0
                && restartFrameCount > 0
                && restartStreamedSamples == restartSamples.Length
                && fallbackSamples.Length > 0
                && streamedSamples == fallbackSamples.Length
                && !capture.IsCapturing;

            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success,
                sampleRate = IAudioCapture.RequiredSampleRate,
                devices,
                configuredDeviceOnRestart = configuredDevice,
                sampleCount = samples.Length,
                streamedSamples = firstStreamedSamples,
                frameCount = firstFrameCount,
                rms,
                peak,
                restartSampleCount = restartSamples.Length,
                restartStreamedSamples,
                restartFrameCount,
                fallbackDevice = missingDevice,
                fallbackSampleCount = fallbackSamples.Length,
                fallbackStreamedSamples = streamedSamples,
                fallbackFrameCount = frameCount,
                isCapturingAfterStop = capture.IsCapturing,
            }));
            return success ? 0 : 1;
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success = false,
                error = exception.GetType().Name,
                exception.Message,
            }));
            Logger.Error("Smoke de audio falhou", exception);
            return 1;
        }
    }

    private static async Task<int> RunWhisperSmoke(string[] args)
    {
        if (args.Length != 3 || !File.Exists(args[1]))
        {
            Logger.Error("Uso: --whisper-smoke <modelo> <resultado-json>.");
            return 2;
        }

        string resultPath = args[2];
        try
        {
            MacWhisperNative.ResetCounters();
            var config = new Matraca.Core.Config
            {
                ModelPath = args[1],
                Language = "pt",
                IdleUnloadMinutes = 0,
            };
            var transcriber = new MacWhisperTranscriber(config);
            int initializationsAfterLoad = MacWhisperNative.BackendInitializations;
            await transcriber.TranscribeAsync(new float[16000]).ConfigureAwait(false);
            int releasesAfterFirst = MacWhisperNative.BackendReleases;
            await transcriber.TranscribeAsync(new float[16000]).ConfigureAwait(false);
            ulong runCount = transcriber.RunCount;
            int releasesAfterSecond = MacWhisperNative.BackendReleases;
            transcriber.Dispose();
            int releasesAfterDispose = MacWhisperNative.BackendReleases;
            bool success = initializationsAfterLoad == 1
                && releasesAfterFirst == 0
                && releasesAfterSecond == 0
                && releasesAfterDispose == 1
                && runCount == 2;
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success,
                initializationsAfterLoad,
                releasesAfterFirst,
                releasesAfterSecond,
                releasesAfterDispose,
                runCount,
            }));
            return success ? 0 : 1;
        }
        catch (Exception exception)
        {
            Logger.Error("Whisper smoke falhou", exception);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success = false,
                error = exception.Message,
            }));
            return 1;
        }
    }
}
