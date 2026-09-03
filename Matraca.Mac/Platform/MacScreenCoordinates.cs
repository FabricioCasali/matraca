using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal static class MacScreenCoordinates
{
    public static bool TryQuartzToAppKit(CGRect quartzBounds, out CGRect appKitBounds)
    {
        MainThread.VerifyAccess();
        IntPtr screens = ObjC.Send(ObjCClasses.NSScreen, ObjCSelectors.Screens);
        if (screens == IntPtr.Zero || ObjC.SendNUInt(screens, ObjCSelectors.Count) == 0)
        {
            appKitBounds = default;
            return false;
        }

        IntPtr primaryScreen = ObjC.SendNUInt(screens, ObjCSelectors.ObjectAtIndex, 0);
        CGRect primaryFrame = ObjC.SendRect(primaryScreen, ObjCSelectors.Frame);
        double primaryTop = primaryFrame.Origin.Y + primaryFrame.Size.Height;
        appKitBounds = new CGRect(
            quartzBounds.Origin.X,
            primaryTop - quartzBounds.Origin.Y - quartzBounds.Size.Height,
            quartzBounds.Size.Width,
            quartzBounds.Size.Height);
        return true;
    }
}
