using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacApplication
{
    private const nint AccessoryActivationPolicy = 1;

    private MacApplication(IntPtr handle) => Handle = handle;

    public IntPtr Handle { get; }

    public static MacApplication Shared()
    {
        IntPtr handle = ObjC.Send(
            ObjCClasses.NSApplication,
            ObjCSelectors.SharedApplication);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("NSApplication.sharedApplication returned nil.");
        return new MacApplication(handle);
    }

    public void ConfigureAsAccessory()
    {
        ObjC.SendBoolNInt(
            Handle,
            ObjCSelectors.SetActivationPolicy,
            AccessoryActivationPolicy);
        nint actualPolicy = ObjC.SendNInt(Handle, ObjCSelectors.ActivationPolicy);
        if (actualPolicy != AccessoryActivationPolicy)
            throw new InvalidOperationException(
                $"NSApplication activation policy is {actualPolicy}, expected Accessory (1).");
    }

    public void Run() => ObjC.SendVoid(Handle, ObjCSelectors.Run);

    public void SetDelegate(IntPtr applicationDelegate)
        => ObjC.SendVoid(Handle, ObjCSelectors.SetDelegate, applicationDelegate);

    public void ReplyToTermination(bool terminate)
        => ObjC.SendVoidBool(
            Handle,
            ObjCSelectors.ReplyToApplicationShouldTerminate,
            terminate);
}
