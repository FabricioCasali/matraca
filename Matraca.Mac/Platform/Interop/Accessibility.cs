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
    public const string FocusedUiElementAttribute = "AXFocusedUIElement";
    public const string WindowAttribute = "AXWindow";
    public const string SelectedTextAttribute = "AXSelectedText";
    public const string RoleAttribute = "AXRole";
    public const string TitleAttribute = "AXTitle";
    public const string RaiseAction = "AXRaise";
    public const string PositionAttribute = "AXPosition";
    public const string SizeAttribute = "AXSize";
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

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementSetAttributeValue(
        IntPtr element,
        IntPtr attribute,
        IntPtr value);

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementIsAttributeSettable(
        IntPtr element,
        IntPtr attribute,
        [MarshalAs(UnmanagedType.I1)] out bool settable);

    [DllImport(ApplicationServices)]
    private static extern int AXUIElementPerformAction(IntPtr element, IntPtr action);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXValueGetValue(IntPtr value, int type, out CGPoint point);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXValueGetValue(IntPtr value, int type, out CGSize size);

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

    public static bool IsFocusedTarget(IntPtr application, IntPtr window, int expectedProcessId)
    {
        IntPtr system = IntPtr.Zero;
        IntPtr focusedApplication = IntPtr.Zero;
        IntPtr focusedWindow = IntPtr.Zero;
        try
        {
            system = CreateSystemWideElement();
            return system != IntPtr.Zero
                && TryCopyElementAttribute(system, FocusedApplicationAttribute, out focusedApplication)
                && TryGetProcessId(focusedApplication, out int processId)
                && processId == expectedProcessId
                && CoreFoundation.AreEqual(focusedApplication, application)
                && TryCopyElementAttribute(focusedApplication, FocusedWindowAttribute, out focusedWindow)
                && CoreFoundation.AreEqual(focusedWindow, window);
        }
        finally
        {
            CoreFoundation.Release(focusedWindow);
            CoreFoundation.Release(focusedApplication);
            CoreFoundation.Release(system);
        }
    }

    public static bool HasFocusedWindow(IntPtr application, IntPtr window)
    {
        if (!TryCopyElementAttribute(application, FocusedWindowAttribute, out IntPtr focusedWindow))
            return false;
        try { return CoreFoundation.AreEqual(focusedWindow, window); }
        finally { CoreFoundation.Release(focusedWindow); }
    }

    public static bool TryGetFocusedInsertionElement(
        IntPtr application,
        IntPtr window,
        int expectedProcessId,
        out IntPtr element)
    {
        element = IntPtr.Zero;
        IntPtr elementWindow = IntPtr.Zero;
        try
        {
            if (!HasFocusedWindow(application, window)
                || !TryCopyElementAttribute(application, FocusedUiElementAttribute, out element)
                || !TryGetProcessId(element, out int processId)
                || processId != expectedProcessId
                || !TryCopyElementAttribute(element, WindowAttribute, out elementWindow)
                || !CoreFoundation.AreEqual(elementWindow, window))
            {
                CoreFoundation.Release(element);
                element = IntPtr.Zero;
                return false;
            }
            return true;
        }
        finally
        {
            CoreFoundation.Release(elementWindow);
        }
    }

    public static bool IsAttributeSettable(IntPtr element, string attribute)
    {
        IntPtr attributeName = CoreFoundation.CreateString(attribute);
        try
        {
            return AXUIElementIsAttributeSettable(element, attributeName, out bool settable) == Success
                && settable;
        }
        finally { CoreFoundation.Release(attributeName); }
    }

    public static bool SetStringAttribute(IntPtr element, string attribute, string value)
    {
        IntPtr attributeName = CoreFoundation.CreateString(attribute);
        IntPtr attributeValue = CoreFoundation.CreateString(value);
        try
        {
            return AXUIElementSetAttributeValue(element, attributeName, attributeValue) == Success;
        }
        finally
        {
            CoreFoundation.Release(attributeValue);
            CoreFoundation.Release(attributeName);
        }
    }

    public static bool SetElementAttribute(IntPtr element, string attribute, IntPtr value)
    {
        IntPtr attributeName = CoreFoundation.CreateString(attribute);
        try { return AXUIElementSetAttributeValue(element, attributeName, value) == Success; }
        finally { CoreFoundation.Release(attributeName); }
    }

    public static bool PerformAction(IntPtr element, string action)
    {
        IntPtr actionName = CoreFoundation.CreateString(action);
        try { return AXUIElementPerformAction(element, actionName) == Success; }
        finally { CoreFoundation.Release(actionName); }
    }

    public static bool TryGetWindowBounds(IntPtr window, out CGRect bounds)
    {
        bounds = default;
        int positionError = CopyAttributeValue(window, PositionAttribute, out IntPtr positionValue);
        if (positionError != Success) return false;

        try
        {
            if (!AXValueGetValue(positionValue, 1, out CGPoint position)) return false;

            int sizeError = CopyAttributeValue(window, SizeAttribute, out IntPtr sizeValue);
            if (sizeError != Success) return false;
            try
            {
                if (!AXValueGetValue(sizeValue, 2, out CGSize size)
                    || !double.IsFinite(position.X)
                    || !double.IsFinite(position.Y)
                    || !double.IsFinite(size.Width)
                    || !double.IsFinite(size.Height)
                    || size.Width <= 0
                    || size.Height <= 0)
                    return false;

                bounds = new CGRect(position.X, position.Y, size.Width, size.Height);
                return true;
            }
            finally { CoreFoundation.Release(sizeValue); }
        }
        finally { CoreFoundation.Release(positionValue); }
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
