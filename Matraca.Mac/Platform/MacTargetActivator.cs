using System.Diagnostics;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal static class MacTargetActivator
{
    private const nuint ActivateAllWindows = 1;
    private const nuint ActivateIgnoringOtherApps = 2;
    private const int FocusTimeoutMilliseconds = 1000;
    private const int FocusPollMilliseconds = 20;

    public static bool ActivateRaiseAndVerify(
        MacTargetLease target,
        CancellationToken cancellationToken = default)
    {
        if (!Accessibility.IsTargetAlive(target.Application, target.Window, target.ProcessId))
            return false;

        using var pool = AutoreleasePool.New();
        IntPtr runningApplication = ObjC.Send(
            ObjCClasses.NSRunningApplication,
            ObjCSelectors.RunningApplicationWithProcessIdentifier,
            (IntPtr)target.ProcessId);
        if (runningApplication == IntPtr.Zero) return false;

        Accessibility.SetElementAttribute(
            target.Application,
            Accessibility.FocusedWindowAttribute,
            target.Window);
        Accessibility.PerformAction(target.Window, Accessibility.RaiseAction);
        ObjC.SendBoolNUInt(
            runningApplication,
            ObjCSelectors.ActivateWithOptions,
            ActivateAllWindows | ActivateIgnoringOtherApps);

        var timeout = Stopwatch.StartNew();
        while (timeout.ElapsedMilliseconds < FocusTimeoutMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Accessibility.SetElementAttribute(
                target.Application,
                Accessibility.FocusedWindowAttribute,
                target.Window);
            Accessibility.PerformAction(target.Window, Accessibility.RaiseAction);
            if (Accessibility.IsFocusedTarget(target.Application, target.Window, target.ProcessId))
                return true;
            Thread.Sleep(FocusPollMilliseconds);
        }
        return false;
    }
}
