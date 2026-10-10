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
    private readonly Func<bool>? _canInvoke;
    private IntPtr _native;

    public MacMenuActionTarget(Action action, Func<bool>? canInvoke = null)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _canInvoke = canInvoke;
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
            .AddMethod(
                ObjCSelectors.CopyLastTranscription,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&Invoke,
                "v@:@")
            .AddMethod(
                ObjCSelectors.ValidateMenuItem,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&Validate,
                "c@:@")
            .Register();

    [UnmanagedCallersOnly]
    private static byte Validate(IntPtr self, IntPtr command, IntPtr sender)
    {
        try
        {
            MacMenuActionTarget? target;
            lock (Gate) Instances.TryGetValue(self, out target);
            return target != null && (target._canInvoke?.Invoke() ?? true) ? (byte)1 : (byte)0;
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao validar item do menu do Matraca", exception); }
            catch { }
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void Invoke(IntPtr self, IntPtr command, IntPtr sender)
    {
        try
        {
            MacMenuActionTarget? target;
            lock (Gate) Instances.TryGetValue(self, out target);
            if (target != null && (target._canInvoke?.Invoke() ?? true)) target._action();
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao executar item do menu do Matraca", exception); }
            catch { }
        }
    }
}
