using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed unsafe class MacWebWindowDelegate : IDisposable
{
    private static readonly object CallbackGate = new();
    private static readonly Dictionary<IntPtr, MacWebWindowDelegate> Instances = [];
    private static readonly IntPtr DelegateClass = CreateDelegateClass();
    private IntPtr _native;

    public MacWebWindowDelegate()
    {
        MainThread.VerifyAccess();
        _native = ObjC.New(DelegateClass);
        if (_native == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the NSWindowDelegate.");
        lock (CallbackGate) Instances.Add(_native, this);
    }

    public event Action? WindowWillClose;
    public IntPtr Handle => _native;

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_native == IntPtr.Zero) return;

        WindowWillClose = null;
        lock (CallbackGate) Instances.Remove(_native);
        ObjC.SendVoid(_native, ObjCSelectors.Release);
        _native = IntPtr.Zero;
    }

    private static IntPtr CreateDelegateClass()
        => ObjCClassBuilder
            .Create("MatracaWebWindowDelegate", ObjCClasses.NSObject)
            .AddProtocol("NSWindowDelegate")
            .AddMethod(
                ObjCSelectors.WindowWillClose,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnWindowWillClose,
                "v@:@")
            .Register();

    [UnmanagedCallersOnly]
    private static void OnWindowWillClose(IntPtr self, IntPtr command, IntPtr notification)
    {
        try
        {
            MacWebWindowDelegate? instance;
            lock (CallbackGate) Instances.TryGetValue(self, out instance);
            instance?.WindowWillClose?.Invoke();
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao processar fechamento da janela web", exception); }
            catch { }
        }
    }
}
