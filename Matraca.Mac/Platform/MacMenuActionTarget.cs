using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed unsafe class MacMenuActionTarget : IDisposable
{
    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, MacMenuActionTarget> Instances = [];
    private static readonly IntPtr TargetClass = CreateTargetClass();

    private readonly Action _action;
    private IntPtr _native;

    public MacMenuActionTarget(Action action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _native = ObjC.New(TargetClass);
        if (_native == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the status menu action target.");
        lock (Gate) Instances.Add(_native, this);
    }

    public IntPtr Handle => _native;

    public void Dispose()
    {
        if (_native == IntPtr.Zero) return;
        lock (Gate) Instances.Remove(_native);
        ObjC.SendVoid(_native, ObjCSelectors.Release);
        _native = IntPtr.Zero;
    }

    private static IntPtr CreateTargetClass()
        => ObjCClassBuilder
            .Create("MatracaMenuActionTarget", ObjCClasses.NSObject)
            .AddMethod(
                ObjCSelectors.OpenMatraca,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Invoke,
                "v@:@")
            .Register();

    [UnmanagedCallersOnly]
    private static void Invoke(IntPtr self, IntPtr command, IntPtr sender)
    {
        try
        {
            MacMenuActionTarget? target;
            lock (Gate) Instances.TryGetValue(self, out target);
            target?._action();
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao abrir a janela do Matraca", exception); }
            catch { }
        }
    }
}
