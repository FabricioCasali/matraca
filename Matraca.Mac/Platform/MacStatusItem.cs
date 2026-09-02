using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacStatusItem : IDisposable
{
    private const double VariableLength = -1;

    private readonly IntPtr _statusBar;
    private readonly IntPtr _button;
    private readonly IntPtr _stateItem;
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

        _button = ObjC.Send(_statusItem, ObjCSelectors.Button);
        if (_button == IntPtr.Zero)
            throw new InvalidOperationException("NSStatusItem.button returned nil.");
        ObjC.SendVoid(_button, ObjCSelectors.SetTitle, NSStringRef.From("Matraca"));

        IntPtr menu = ObjC.New(ObjCClasses.NSMenu);
        _stateItem = CreateMenuItem("Iniciando...", IntPtr.Zero, "");
        if (menu == IntPtr.Zero || _stateItem == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the status menu.");
        ObjC.SendVoidBool(_stateItem, ObjCSelectors.SetEnabled, false);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem, _stateItem);
        ObjC.SendVoid(_stateItem, ObjCSelectors.Release);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem,
            ObjC.Send(ObjCClasses.NSMenuItem, ObjCSelectors.SeparatorItem));

        IntPtr quitItem = CreateMenuItem("Sair do Matraca", ObjCSelectors.Terminate, "q");
        if (quitItem == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the status menu.");

        ObjC.SendVoid(quitItem, ObjCSelectors.SetTarget, application.Handle);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem, quitItem);
        ObjC.SendVoid(_statusItem, ObjCSelectors.SetMenu, menu);
        ObjC.SendVoid(quitItem, ObjCSelectors.Release);
        ObjC.SendVoid(menu, ObjCSelectors.Release);
    }

    public void SetState(string title, string detail)
    {
        ObjC.SendVoid(_button, ObjCSelectors.SetTitle, NSStringRef.From(title));
        ObjC.SendVoid(_stateItem, ObjCSelectors.SetTitle, NSStringRef.From(detail));
    }

    private static IntPtr CreateMenuItem(string title, IntPtr action, string keyEquivalent)
        => ObjC.Send(
            ObjC.Send(ObjCClasses.NSMenuItem, ObjCSelectors.Alloc),
            ObjCSelectors.InitWithTitleActionKeyEquivalent,
            NSStringRef.From(title),
            action,
            NSStringRef.From(keyEquivalent));

    public void Dispose()
    {
        if (_statusItem == IntPtr.Zero) return;
        ObjC.SendVoid(_statusBar, ObjCSelectors.RemoveStatusItem, _statusItem);
        _statusItem = IntPtr.Zero;
    }
}
