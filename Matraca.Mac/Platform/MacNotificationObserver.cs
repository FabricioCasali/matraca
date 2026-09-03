using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed unsafe class MacNotificationObserver : IDisposable
{
    private static readonly object CallbackGate = new();
    private static readonly Dictionary<IntPtr, Action> Callbacks = [];
    private static readonly IntPtr ObserverClass = CreateObserverClass();

    private readonly IntPtr _center;
    private IntPtr _observer;

    public MacNotificationObserver(IntPtr center, string notificationName, Action callback)
    {
        MainThread.VerifyAccess();
        if (center == IntPtr.Zero)
            throw new ArgumentException("Notification center cannot be null.", nameof(center));
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationName);
        ArgumentNullException.ThrowIfNull(callback);

        _center = center;
        _observer = ObjC.New(ObserverClass);
        if (_observer == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the macOS notification observer.");

        lock (CallbackGate) Callbacks.Add(_observer, callback);
        try
        {
            ObjC.Send(
                _center,
                ObjCSelectors.AddObserver,
                _observer,
                ObjCSelectors.EnvironmentChanged,
                NSStringRef.From(notificationName),
                IntPtr.Zero);
        }
        catch
        {
            lock (CallbackGate) Callbacks.Remove(_observer);
            ObjC.SendVoid(_observer, ObjCSelectors.Release);
            _observer = IntPtr.Zero;
            throw;
        }
    }

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_observer == IntPtr.Zero) return;
        ObjC.SendVoid(_center, ObjCSelectors.RemoveObserver, _observer);
        lock (CallbackGate) Callbacks.Remove(_observer);
        ObjC.SendVoid(_observer, ObjCSelectors.Release);
        _observer = IntPtr.Zero;
    }

    private static IntPtr CreateObserverClass()
        => ObjCClassBuilder
            .Create("MatracaEnvironmentObserver", ObjCClasses.NSObject)
            .AddMethod(
                ObjCSelectors.EnvironmentChanged,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnEnvironmentChanged,
                "v@:@")
            .Register();

    [UnmanagedCallersOnly]
    private static void OnEnvironmentChanged(IntPtr self, IntPtr command, IntPtr notification)
    {
        try
        {
            Action? callback;
            lock (CallbackGate) Callbacks.TryGetValue(self, out callback);
            callback?.Invoke();
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao reagir a mudanca de Space ou monitor", exception); }
            catch { }
        }
    }
}
