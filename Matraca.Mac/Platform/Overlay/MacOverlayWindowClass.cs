using System.Runtime.InteropServices;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Overlay;

internal static unsafe class MacOverlayWindowClass
{
    private static IntPtr _class;

    public static IntPtr Handle
    {
        get
        {
            if (_class != IntPtr.Zero) return _class;
            _class = ObjCClassBuilder
                .Create("MatracaBorderOverlayWindow", ObjCClasses.NSWindow)
                .AddMethod(
                    ObjCSelectors.CanBecomeKeyWindow,
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&CannotBecomeWindow,
                    "B@:")
                .AddMethod(
                    ObjCSelectors.CanBecomeMainWindow,
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&CannotBecomeWindow,
                    "B@:")
                .Register();
            return _class;
        }
    }

    [UnmanagedCallersOnly]
    private static byte CannotBecomeWindow(IntPtr self, IntPtr command) => 0;
}
