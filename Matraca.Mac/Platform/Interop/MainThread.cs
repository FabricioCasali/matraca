using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Matraca.Core;

namespace Matraca.Mac.Platform.Interop;

internal static unsafe class MainThread
{
    private static readonly ConcurrentQueue<Action> Queue = new();
    private static IntPtr _dispatcher;

    public static void Initialize()
    {
        if (_dispatcher != IntPtr.Zero) return;
        IntPtr cls = ObjCClassBuilder
            .Create("MatracaDispatcher", ObjCClasses.NSObject)
            .AddMethod(
                ObjCSelectors.Pump,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&Pump,
                "v@:")
            .Register();
        _dispatcher = ObjC.New(cls);
    }

    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Queue.Enqueue(action);
        ObjC.SendPerformOnMain(
            _dispatcher,
            ObjCSelectors.PerformOnMainThread,
            ObjCSelectors.Pump,
            IntPtr.Zero,
            waitUntilDone: false);
    }

    [UnmanagedCallersOnly]
    private static void Pump(IntPtr self, IntPtr command)
    {
        try
        {
            while (Queue.TryDequeue(out Action? action))
            {
                try { action(); }
                catch (Exception exception) { Logger.Error("Falha na thread principal", exception); }
            }
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha no dispatcher principal", exception); } catch { }
        }
    }
}
