using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Matraca.Mac.Platform.Interop;

internal static class Accessibility
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const int Success = 0;
    private const int CannotComplete = -25204;

    public const string FocusedApplicationAttribute = "AXFocusedApplication";
    public const string FocusedWindowAttribute = "AXFocusedWindow";
    public const string RoleAttribute = "AXRole";
    public const string TitleAttribute = "AXTitle";
    public const string ApplicationRole = "AXApplication";
    public const string WindowRole = "AXWindow";

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(ApplicationServices)]
    private static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementCopyAttributeValue(
        IntPtr element,
        IntPtr attribute,
        out IntPtr value);

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementGetPid(IntPtr element, out int processId);

    [DllImport(ApplicationServices)]
    private static extern nuint AXUIElementGetTypeID();

    public static bool IsTrusted(bool prompt)
    {
        IntPtr key = NSStringRef.From("AXTrustedCheckOptionPrompt");
        IntPtr value = ObjC.SendWithBool(
            ObjCClasses.NSNumber,
            ObjCSelectors.NumberWithBool,
            prompt);
        IntPtr options = ObjC.Send(
            ObjCClasses.NSDictionary,
            ObjCSelectors.DictionaryWithObjectForKey,
            value,
            key);
        return AXIsProcessTrustedWithOptions(options);
    }

    public static IntPtr CreateSystemWideElement()
        => AXUIElementCreateSystemWide();

    public static bool TryCopyElementAttribute(
        IntPtr element,
        string attribute,
        out IntPtr value)
    {
        int error = CopyAttributeValue(element, attribute, out value);
        if (error == Success && CoreFoundation.IsType(value, AXUIElementGetTypeID())) return true;

        CoreFoundation.Release(value);
        value = IntPtr.Zero;
        return false;
    }

    public static bool TryGetProcessId(IntPtr element, out int processId)
        => AXUIElementGetPid(element, out processId) == Success;

    public static string GetStringAttribute(IntPtr element, string attribute)
    {
        int error = CopyAttributeValue(element, attribute, out IntPtr value);
        if (error != Success) return string.Empty;

        try { return CoreFoundation.GetString(value) ?? string.Empty; }
        finally { CoreFoundation.Release(value); }
    }

    public static bool IsTargetAlive(IntPtr application, IntPtr window, int expectedProcessId)
    {
        if (expectedProcessId <= 0
            || !IsProcessRunning(expectedProcessId)
            || !TryGetProcessId(application, out int applicationProcessId)
            || !TryGetProcessId(window, out int windowProcessId)
            || applicationProcessId != expectedProcessId
            || windowProcessId != expectedProcessId)
            return false;

        return HasExpectedRole(application, ApplicationRole)
            && HasExpectedRole(window, WindowRole)
            && IsProcessRunning(expectedProcessId);
    }

    private static bool HasExpectedRole(IntPtr element, string expectedRole)
    {
        int error = CopyAttributeValue(element, RoleAttribute, out IntPtr value);
        if (error == CannotComplete) return true;
        if (error != Success) return false;

        try
        {
            return string.Equals(
                CoreFoundation.GetString(value),
                expectedRole,
                StringComparison.Ordinal);
        }
        finally
        {
            CoreFoundation.Release(value);
        }
    }

    private static int CopyAttributeValue(
        IntPtr element,
        string attribute,
        out IntPtr value)
    {
        value = IntPtr.Zero;
        if (!CoreFoundation.IsType(element, AXUIElementGetTypeID())) return -25202;

        IntPtr attributeName = CoreFoundation.CreateString(attribute);
        try
        {
            int error = AXUIElementCopyAttributeValue(element, attributeName, out value);
            if (error == Success) return error;

            CoreFoundation.Release(value);
            value = IntPtr.Zero;
            return error;
        }
        finally { CoreFoundation.Release(attributeName); }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
