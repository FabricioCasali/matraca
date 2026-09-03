using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal static class MacTargetSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length is < 2 or > 3
            || (args.Length == 3
                && (!int.TryParse(args[2], out int parsedDelay) || parsedDelay < 0)))
        {
            Logger.Error("Uso: --target-smoke <resultado-json> [delay-ms].");
            return 2;
        }

        string resultPath = args[1];
        int delayMilliseconds = args.Length == 3 ? int.Parse(args[2]) : 0;
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

        Thread.Sleep(delayMilliseconds);
        using var targets = new MacTargetWindow();
        TargetToken? token = targets.CaptureActive();
        if (token == null)
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

        string title = targets.GetTitle(token);
        bool alive = targets.IsAlive(token);
        bool unknownRejected = !targets.TryAcquireLease(TargetToken.Create(), out _);
        if (!targets.TryAcquireLease(token, out MacTargetLease? lease))
        {
            WriteResult(resultPath, new
            {
                success = false,
                trusted = true,
                captured = true,
                title,
                alive,
                unknownRejected,
                error = "The captured token could not be leased.",
            });
            return 1;
        }

        using (lease)
        {
            int processId = lease.ProcessId;
            bool handlesPresent = lease.Application != IntPtr.Zero && lease.Window != IntPtr.Zero;
            targets.Release(token);
            bool releasedTokenRejected = !targets.TryAcquireLease(token, out _);
            bool retainedAfterRelease = Accessibility.IsTargetAlive(
                lease.Application,
                lease.Window,
                processId);
            targets.Dispose();
            bool retainedAfterRegistryDispose = Accessibility.IsTargetAlive(
                lease.Application,
                lease.Window,
                processId);
            bool success = alive
                && unknownRejected
                && handlesPresent
                && releasedTokenRejected
                && retainedAfterRelease
                && retainedAfterRegistryDispose;

            WriteResult(resultPath, new
            {
                success,
                trusted = true,
                captured = true,
                processId,
                title,
                alive,
                unknownRejected,
                handlesPresent,
                releasedTokenRejected,
                retainedAfterRelease,
                retainedAfterRegistryDispose,
            });
            return success ? 0 : 1;
        }
    }

    private static void WriteResult(string path, object result)
        => File.WriteAllText(path, JsonSerializer.Serialize(result));
}
