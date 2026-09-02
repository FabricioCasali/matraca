using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Config;
using Matraca.Mac.Platform;
using Matraca.Mac.Platform.Audio;
using Matraca.Mac.Platform.Interop;
using Matraca.Mac.Platform.Keyboard;
using Matraca.Mac.Platform.Text;

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

            Frameworks.EnsureLoaded();
            ObjCClasses.Warm();
            using var pool = AutoreleasePool.New();

            var application = MacApplication.Shared();
            application.ConfigureAsAccessory();
            MainThread.Initialize();
            using var statusItem = new MacStatusItem(application);

            Matraca.Core.Config config = MacConfig.Load();
            bool trusted = Accessibility.IsTrusted(prompt: true);
            MacKeyboardHook? keyboard = null;
            if (trusted)
            {
                keyboard = TryStartKeyboard(config, statusItem);
            }
            else
            {
                statusItem.SetState("Matraca !", "Acessibilidade necessaria; conceda e reinicie o Matraca.");
                Logger.Warn("Permissao de Acessibilidade ausente; conceda e reinicie o Matraca.");
            }

            using var configWatcher = new MacConfigWatcher();
            configWatcher.Changed += next => MainThread.Post(() =>
            {
                keyboard?.Dispose();
                keyboard = trusted ? TryStartKeyboard(next, statusItem) : null;
                Logger.Info("Configuracoes do Mac aplicadas sem reiniciar.");
            });

            Logger.Info("Matraca.Mac iniciado como app Accessory.");
            application.Run();
            configWatcher.Dispose();
            keyboard?.Dispose();
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
        if (args.Length != 5
            || !bool.TryParse(args[2], out bool pressEnter)
            || !int.TryParse(args[3], out int delayMs)
            || delayMs < 0)
        {
            Logger.Error("Uso: --delivery-smoke <texto> <true|false> <delay-ms> <resultado>.");
            return 2;
        }

        Frameworks.EnsureLoaded();
        ObjCClasses.Warm();
        using var pool = AutoreleasePool.New();
        if (!Accessibility.IsTrusted(prompt: false))
        {
            File.WriteAllText(args[4], TextDeliveryResult.Failed.ToString());
            Logger.Error("Smoke de entrega sem permissao de Acessibilidade.");
            return 1;
        }

        Thread.Sleep(delayMs);
        using var sink = new MacTextSink();
        TextDeliveryResult result = sink.DeliverAsync(new TextDeliveryRequest(
                args[1],
                pressEnter,
                TextDeliveryMethod.Unicode))
            .GetAwaiter().GetResult();
        File.WriteAllText(args[4], result.ToString());
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
            await capture.StartAsync(null, TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            await Task.Delay(600).ConfigureAwait(false);
            float[] restartSamples = await capture.StopAsync().ConfigureAwait(false);
            bool success = samples.Length > 0
                && firstFrameCount > 0
                && firstStreamedSamples == samples.Length
                && rms > 0
                && restartSamples.Length > 0
                && frameCount > 0
                && streamedSamples == restartSamples.Length
                && !capture.IsCapturing;

            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success,
                sampleRate = IAudioCapture.RequiredSampleRate,
                sampleCount = samples.Length,
                streamedSamples = firstStreamedSamples,
                frameCount = firstFrameCount,
                rms,
                peak,
                restartSampleCount = restartSamples.Length,
                restartStreamedSamples = streamedSamples,
                restartFrameCount = frameCount,
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

    private static MacKeyboardHook? TryStartKeyboard(
        Matraca.Core.Config config,
        MacStatusItem statusItem)
    {
        try
        {
            var keyboard = new MacKeyboardHook(config);
            keyboard.DictationKeyChanged += (_, pressed) =>
                Logger.Info($"Atalho de ditado {(pressed ? "pressionado" : "liberado")}.");
            keyboard.PinToggled += gesture => Logger.Info($"Atalho de pin: {gesture}.");
            keyboard.KeyDiscovered += gesture => Logger.Info($"Tecla detectada: {gesture}.");
            keyboard.Start();
            statusItem.SetState("Matraca", $"Pronto: {config.HotkeyName} ({config.Mode})");
            return keyboard;
        }
        catch (Exception exception)
        {
            Logger.Error("Falha ao iniciar o teclado do Mac", exception);
            statusItem.SetState("Matraca !", "Teclado indisponivel; confira Acessibilidade e config.");
            return null;
        }
    }
}
