using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed unsafe class MacSleepWakeMonitor : IDisposable
{
    private const string WillSleepNotification = "NSWorkspaceWillSleepNotification";
    private const string DidWakeNotification = "NSWorkspaceDidWakeNotification";
    private static MacSleepWakeMonitor? _current;

    private readonly Action _willSleep;
    private readonly Action _didWake;
    private IntPtr _notificationCenter;
    private IntPtr _observer;
    private int _disposed;

    public MacSleepWakeMonitor(Action willSleep, Action didWake)
    {
        _willSleep = willSleep ?? throw new ArgumentNullException(nameof(willSleep));
        _didWake = didWake ?? throw new ArgumentNullException(nameof(didWake));
        MainThread.VerifyAccess();

        if (Interlocked.CompareExchange(ref _current, this, null) != null)
            throw new InvalidOperationException("A sleep/wake monitor is already installed.");

        try
        {
            IntPtr cls = ObjCClassBuilder
                .Create("MatracaSleepWakeObserver", ObjCClasses.NSObject)
                .AddMethod(
                    ObjCSelectors.WorkspaceWillSleep,
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&WorkspaceWillSleep,
                    "v@:@")
                .AddMethod(
                    ObjCSelectors.WorkspaceDidWake,
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&WorkspaceDidWake,
                    "v@:@")
                .Register();
            _observer = ObjC.New(cls);
            IntPtr workspace = ObjC.Send(ObjCClasses.NSWorkspace, ObjCSelectors.SharedWorkspace);
            _notificationCenter = ObjC.Send(workspace, ObjCSelectors.NotificationCenter);
            if (_observer == IntPtr.Zero || _notificationCenter == IntPtr.Zero)
                throw new InvalidOperationException("NSWorkspace notification setup failed.");

            AddObserver(ObjCSelectors.WorkspaceWillSleep, WillSleepNotification);
            AddObserver(ObjCSelectors.WorkspaceDidWake, DidWakeNotification);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void AddObserver(IntPtr selector, string notificationName)
        => ObjC.SendVoid(
            _notificationCenter,
            ObjCSelectors.AddObserver,
            _observer,
            selector,
            NSStringRef.From(notificationName),
            IntPtr.Zero);

    [UnmanagedCallersOnly]
    private static void WorkspaceWillSleep(IntPtr self, IntPtr command, IntPtr notification)
        => Publish(self, sleeping: true);

    [UnmanagedCallersOnly]
    private static void WorkspaceDidWake(IntPtr self, IntPtr command, IntPtr notification)
        => Publish(self, sleeping: false);

    private static void Publish(IntPtr observer, bool sleeping)
    {
        try
        {
            MacSleepWakeMonitor? current = Volatile.Read(ref _current);
            if (current == null || current._observer != observer) return;
            if (sleeping) current._willSleep();
            else current._didWake();
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao processar notificacao de energia do macOS", exception); }
            catch { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.CompareExchange(ref _current, null, this);
        if (_notificationCenter != IntPtr.Zero && _observer != IntPtr.Zero)
            ObjC.SendVoid(_notificationCenter, ObjCSelectors.RemoveObserver, _observer);
        if (_observer != IntPtr.Zero) ObjC.SendVoid(_observer, ObjCSelectors.Release);
        _observer = IntPtr.Zero;
        _notificationCenter = IntPtr.Zero;
    }
}
