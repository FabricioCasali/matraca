using Matraca.Core;
using Matraca.Mac.Config;
using Matraca.Mac.Platform;
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
