using System.Diagnostics;
using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal static class MacWebWindowSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length != 3)
        {
            Logger.Error("Uso: --window-smoke <raiz-assets> <resultado-json>.");
            return 2;
        }

        string resultPath = args[2];
        try
        {
            Frameworks.EnsureLoaded();
            ObjCClasses.Warm();
            using var pool = AutoreleasePool.New();
            var application = MacApplication.Shared();
            application.ConfigureAsAccessory();
            MainThread.Initialize();
            using var statusItem = new MacStatusItem(
                application,
                UiLanguageResolver.ResolveEffective(UiLanguageResolver.System));
            using var controller = new MacWebWindowController(application, statusItem, args[1]);

            bool startsAccessory = application.ActivationPolicy == 1;
            controller.Open();
            PumpUntil(() => controller.Host?.IsVisible == true);
            MacWebViewHost host = controller.Host
                ?? throw new InvalidOperationException("Window host was not created.");
            IntPtr originalWindow = host.WindowHandle;
            CGRect dragFrame = host.DragRegionFrame;
            bool opensRegular = application.ActivationPolicy == 0 && host.IsVisible;
            bool nativeTitleHidden = host.IsNativeTitleHidden;
            bool dragRegionReceivesTitlebarHit = host.DragRegionReceivesTitlebarHit;
            bool dragRegionValid = dragFrame.Origin.X == 80
                && dragFrame.Origin.Y == 672
                && dragFrame.Size.Width == 896
                && dragFrame.Size.Height == 48;

            int closeCount = 0;
            host.WindowWillClose += () => closeCount++;
            host.Miniaturize();
            PumpUntil(() => host.IsMiniaturized);
            bool minimizeKeepsRegular = host.IsMiniaturized
                && application.ActivationPolicy == 0
                && closeCount == 0;

            controller.Open();
            PumpUntil(() => !host.IsMiniaturized && host.IsVisible);
            bool menuRestoresMinimized = !host.IsMiniaturized && host.IsVisible;

            host.Close();
            PumpUntil(() => closeCount == 1);
            bool closeRestoresAccessory = closeCount == 1
                && !host.IsVisible
                && application.ActivationPolicy == 1;

            controller.Open();
            PumpUntil(() => host.IsVisible && application.ActivationPolicy == 0);
            bool reopensSameWindow = host.IsVisible
                && host.WindowHandle == originalWindow
                && application.ActivationPolicy == 0;

            host.Close();
            PumpUntil(() => closeCount == 2);
            bool secondCloseRestoresAccessory = closeCount == 2
                && !host.IsVisible
                && application.ActivationPolicy == 1;

            bool success = startsAccessory
                && opensRegular
                && nativeTitleHidden
                && dragRegionValid
                && dragRegionReceivesTitlebarHit
                && minimizeKeepsRegular
                && menuRestoresMinimized
                && closeRestoresAccessory
                && reopensSameWindow
                && secondCloseRestoresAccessory;
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success,
                startsAccessory,
                opensRegular,
                nativeTitleHidden,
                dragRegionValid,
                dragRegionReceivesTitlebarHit,
                minimizeKeepsRegular,
                menuRestoresMinimized,
                closeRestoresAccessory,
                reopensSameWindow,
                secondCloseRestoresAccessory,
                closeCount,
                manualDragValidationRequired = true,
            }));
            return success ? 0 : 1;
        }
        catch (Exception exception)
        {
            Exception rootCause = exception.GetBaseException();
            Logger.Error("Smoke da janela WebKit falhou", rootCause);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success = false,
                error = rootCause.GetType().Name,
                rootCause.Message,
            }));
            return 1;
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        IntPtr runLoop = ObjC.Send(ObjCClasses.NSRunLoop, ObjCSelectors.CurrentRunLoop);
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.ElapsedMilliseconds < 3000)
        {
            IntPtr until = ObjC.SendDouble(
                ObjCClasses.NSDate,
                ObjCSelectors.DateWithTimeIntervalSinceNow,
                0.05);
            ObjC.SendVoid(runLoop, ObjCSelectors.RunUntilDate, until);
        }
    }
}
