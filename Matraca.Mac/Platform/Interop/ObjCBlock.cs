using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static unsafe class ObjCBlock
{
    private const int InvokePointerOffset = 16;

    public static void InvokeNavigationPolicy(IntPtr block, nint policy)
    {
        if (block == IntPtr.Zero) return;

        IntPtr invoke = Marshal.ReadIntPtr(block, InvokePointerOffset);
        if (invoke == IntPtr.Zero)
            throw new InvalidOperationException("Objective-C block has no invoke function.");
        ((delegate* unmanaged<IntPtr, nint, void>)invoke)(block, policy);
    }

    public static void InvokeBoolean(IntPtr block, bool value)
    {
        if (block == IntPtr.Zero) return;

        IntPtr invoke = Marshal.ReadIntPtr(block, InvokePointerOffset);
        if (invoke == IntPtr.Zero)
            throw new InvalidOperationException("Objective-C block has no invoke function.");
        ((delegate* unmanaged<IntPtr, byte, void>)invoke)(block, value ? (byte)1 : (byte)0);
    }
}
