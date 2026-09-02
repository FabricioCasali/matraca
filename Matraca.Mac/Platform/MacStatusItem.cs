using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacStatusItem : IDisposable
{
    private const double VariableLength = -1;

    private readonly IntPtr _statusBar;
    private IntPtr _statusItem;

    public MacStatusItem(MacApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        _statusBar = ObjC.Send(ObjCClasses.NSStatusBar, ObjCSelectors.SystemStatusBar);
        _statusItem = ObjC.SendDouble(
            _statusBar,
            ObjCSelectors.StatusItemWithLength,
            VariableLength);
        if (_statusItem == IntPtr.Zero)
            throw new InvalidOperationException("NSStatusBar failed to create a status item.");

        IntPtr button = ObjC.Send(_statusItem, ObjCSelectors.Button);
        if (button == IntPtr.Zero)
            throw new InvalidOperationException("NSStatusItem.button returned nil.");
        ObjC.SendVoid(button, ObjCSelectors.SetTitle, NSStringRef.From("Matraca"));

        IntPtr menu = ObjC.New(ObjCClasses.NSMenu);
        IntPtr quitItem = ObjC.Send(
            ObjC.Send(ObjCClasses.NSMenuItem, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithTitleActionKeyEquivalent,
            NSStringRef.From("Quit Matraca"),
            ObjCSelectors.Terminate,
            NSStringRef.From("q"));
        if (menu == IntPtr.Zero || quitItem == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the status menu.");

        ObjC.SendVoid(quitItem, ObjCSelectors.SetTarget, application.Handle);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem, quitItem);
        ObjC.SendVoid(_statusItem, ObjCSelectors.SetMenu, menu);
        ObjC.SendVoid(quitItem, ObjCSelectors.Release);
        ObjC.SendVoid(menu, ObjCSelectors.Release);
    }

    public void Dispose()
    {
        if (_statusItem == IntPtr.Zero) return;
        ObjC.SendVoid(_statusBar, ObjCSelectors.RemoveStatusItem, _statusItem);
        _statusItem = IntPtr.Zero;
    }
}
