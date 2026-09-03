using System.Text.Json;
using System.Diagnostics;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal static class MacIndicatorSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 4
            || (args.Length >= 3
                && (!int.TryParse(args[2], out int delay) || delay < 0))
            || (args.Length == 4
                && (!int.TryParse(args[3], out int duration) || duration is < 1000 or > 120000)))
        {
            Logger.Error("Uso: --indicator-smoke <resultado-json> [delay-ms] [duracao-ms: 1000..120000].");
            return 2;
        }

        string resultPath = args[1];
        int delayMilliseconds = args.Length >= 3 ? int.Parse(args[2]) : 1500;
        int durationMilliseconds = args.Length == 4 ? int.Parse(args[3]) : 15000;
        Frameworks.EnsureLoaded();
        ObjCClasses.Warm();
        using var pool = AutoreleasePool.New();
        if (!Accessibility.IsTrusted(prompt: false))
        {
            WriteResult(resultPath, new
            {
                success = false,
                trusted = false,
                error = "Accessibility permission is not granted.",
            });
            return 1;
        }

        var application = MacApplication.Shared();
        application.ConfigureAsAccessory();
        MainThread.Initialize();
        Thread.Sleep(delayMilliseconds);

        using var targets = new MacTargetWindow();
        TargetToken? target = targets.CaptureActive();
        if (target == null || !targets.TryAcquireLease(target, out MacTargetLease? lease))
        {
            WriteResult(resultPath, new
            {
                success = false,
                trusted = true,
                captured = false,
                error = "No valid external focused window was captured.",
            });
            return 1;
        }

        CGRect initialBounds;
        using (lease)
        {
            if (!MacWindowVisibility.TryGetOnScreenBounds(lease, out initialBounds))
            {
                targets.Release(target);
                WriteResult(resultPath, new
                {
                    success = false,
                    trusted = true,
                    captured = true,
                    error = "The AX target could not be mapped uniquely to the current Space.",
                });
                return 1;
            }
        }

        string title = targets.GetTitle(target);
        targets.ConfigureIndicator(true, "#2ECC71", 4, 0.9);
        targets.ShowIndicator(target, "#2ECC71");
        Logger.Info(
            "Smoke da moldura: mova, redimensione e minimize a janela; troque de Space e monitor. "
            + "A moldura deve acompanhar somente quando o alvo estiver confirmado no Space atual.");

        IntPtr runLoop = ObjC.Send(ObjCClasses.NSRunLoop, ObjCSelectors.CurrentRunLoop);
        var elapsed = Stopwatch.StartNew();
        bool reconfigured = false;
        while (elapsed.ElapsedMilliseconds < durationMilliseconds)
        {
            if (!reconfigured && elapsed.ElapsedMilliseconds >= durationMilliseconds / 2)
            {
                reconfigured = true;
                targets.ConfigureIndicator(true, "#C678DD", 8, 0.65);
                targets.ShowIndicator(target, "#C678DD");
            }

            IntPtr until = ObjC.SendDouble(
                ObjCClasses.NSDate,
                ObjCSelectors.DateWithTimeIntervalSinceNow,
                0.05);
            ObjC.SendVoid(runLoop, ObjCSelectors.RunUntilDate, until);
        }
        targets.HideIndicator();
        targets.Release(target);
        WriteResult(resultPath, new
        {
            success = true,
            trusted = true,
            captured = true,
            title,
            durationMilliseconds,
            initialBounds = new
            {
                x = initialBounds.Origin.X,
                y = initialBounds.Origin.Y,
                width = initialBounds.Size.Width,
                height = initialBounds.Size.Height,
            },
            manualValidationRequired = true,
        });
        return 0;
    }

    private static void WriteResult(string path, object result)
        => File.WriteAllText(path, JsonSerializer.Serialize(result));
}
