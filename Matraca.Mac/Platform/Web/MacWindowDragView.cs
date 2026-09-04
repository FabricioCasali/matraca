using System.Runtime.InteropServices;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal sealed unsafe class MacWindowDragView : IDisposable
{
    private const nuint WidthSizable = 1 << 1;
    private const nuint MinYMargin = 1 << 3;
    private static readonly IntPtr DragViewClass = CreateDragViewClass();
    private IntPtr _native;

    public MacWindowDragView(CGRect frame)
    {
        MainThread.VerifyAccess();
        _native = ObjC.SendInitView(
            ObjC.Send(DragViewClass, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithFrame,
            frame);
        if (_native == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the native window drag region.");
        ObjC.SendVoidNUInt(
            _native,
            ObjCSelectors.SetAutoresizingMask,
            WidthSizable | MinYMargin);
    }

    public IntPtr Handle => _native;
    public CGRect Frame => ObjC.SendRect(_native, ObjCSelectors.Frame);

    public void Dispose()
    {
        MainThread.VerifyAccess();
        if (_native == IntPtr.Zero) return;

        ObjC.SendVoid(_native, ObjCSelectors.RemoveFromSuperview);
        ObjC.SendVoid(_native, ObjCSelectors.Release);
        _native = IntPtr.Zero;
    }

    private static IntPtr CreateDragViewClass()
        => ObjCClassBuilder
            .Create("MatracaWindowDragView", ObjCClasses.NSView)
            .AddMethod(
                ObjCSelectors.MouseDown,
                (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnMouseDown,
                "v@:@")
            .Register();

    [UnmanagedCallersOnly]
    private static void OnMouseDown(IntPtr self, IntPtr command, IntPtr mouseEvent)
    {
        try
        {
            IntPtr window = ObjC.Send(self, ObjCSelectors.Window);
            if (window != IntPtr.Zero)
                ObjC.SendVoid(window, ObjCSelectors.PerformWindowDragWithEvent, mouseEvent);
        }
        catch (Exception exception)
        {
            try { Logger.Error("Falha ao mover a janela web", exception); }
            catch { }
        }
    }
}
