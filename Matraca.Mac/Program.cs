using Matraca.Core;
using Matraca.Mac.Config;
using Matraca.Mac.Platform;
using Matraca.Mac.Platform.Interop;
using Matraca.Mac.Platform.Keyboard;
using Matraca.Mac.Platform.Web;

namespace Matraca.Mac;

internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Matraca.Mac requires macOS.");

        Logger.Initialize(AppPaths.Current());
        try
        {
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
            Matraca.Core.Config config = MacConfig.Load();
            using var statusItem = new MacStatusItem(application, config.EffectiveUiLanguage);
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
                    if (trayApp != null)
                        await trayApp.ShutdownAsync(cancellation).ConfigureAwait(false);
                    await bridge.ShutdownAsync(cancellation).ConfigureAwait(false);
                });
            if (trayApp != null)
            {
                trayApp.Start();
                Logger.Info("Matraca.Mac iniciado como app Accessory.");
            }
            else
            {
                MacUiText ui = new(config.EffectiveUiLanguage);
                statusItem.SetState("Matraca !", ui.AccessibilityRequiredMessage);
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

}
