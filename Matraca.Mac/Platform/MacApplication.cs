using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacApplication
{
    private const nint RegularActivationPolicy = 0;
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

    internal nint ActivationPolicy
        => ObjC.SendNInt(Handle, ObjCSelectors.ActivationPolicy);

    public void ConfigureAsAccessory() => SetActivationPolicy(
        AccessoryActivationPolicy,
        "Accessory");

    public void ConfigureAsRegular() => SetActivationPolicy(
        RegularActivationPolicy,
        "Regular");

    private void SetActivationPolicy(nint policy, string name)
    {
        ObjC.SendBoolNInt(
            Handle,
            ObjCSelectors.SetActivationPolicy,
            policy);
        nint actualPolicy = ActivationPolicy;
        if (actualPolicy != policy)
            throw new InvalidOperationException(
                $"NSApplication activation policy is {actualPolicy}, expected {name} ({policy}).");
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
