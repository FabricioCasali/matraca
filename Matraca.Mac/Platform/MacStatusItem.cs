using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform;

internal sealed class MacStatusItem : IDisposable
{
    private const double VariableLength = -1;

    private readonly IntPtr _statusBar;
    private readonly IntPtr _button;
    private readonly IntPtr _stateItem;
    private readonly MacMenuActionTarget _openTarget;
    private readonly MacMenuActionTarget _copyTarget;
    private IntPtr _openItem;
    private IntPtr _copyItem;
    private IntPtr _quitItem;
    private MacUiText _ui;
    private IntPtr _statusItem;

    public event Action? OpenRequested;
    public event Action? CopyLastTranscriptionRequested;
    public Func<bool>? CanCopyLastTranscription { get; set; }

    public MacStatusItem(MacApplication application, string effectiveUiLanguage)
    {
        ArgumentNullException.ThrowIfNull(application);
        _ui = new MacUiText(effectiveUiLanguage);

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
        SetAccessibilityLabel(_button, _ui.AccessibilityLabel);

        IntPtr menu = ObjC.New(ObjCClasses.NSMenu);
        _stateItem = CreateMenuItem(_ui.MenuStarting, IntPtr.Zero, "");
        if (menu == IntPtr.Zero || _stateItem == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the status menu.");
        ObjC.SendVoidBool(_stateItem, ObjCSelectors.SetEnabled, false);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem, _stateItem);
        ObjC.SendVoid(_stateItem, ObjCSelectors.Release);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem,
            ObjC.Send(ObjCClasses.NSMenuItem, ObjCSelectors.SeparatorItem));

        _openTarget = new MacMenuActionTarget(() => OpenRequested?.Invoke());
        _openItem = CreateMenuItem(_ui.MenuOpen, ObjCSelectors.OpenMatraca, "");
        if (_openItem == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the open menu item.");
        ObjC.SendVoid(_openItem, ObjCSelectors.SetTarget, _openTarget.Handle);
        SetAccessibilityLabel(_openItem, _ui.MenuOpen);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem, _openItem);
        ObjC.SendVoid(_openItem, ObjCSelectors.Release);
        _copyTarget = new MacMenuActionTarget(
            () => CopyLastTranscriptionRequested?.Invoke(),
            () => CanCopyLastTranscription?.Invoke() == true);
        _copyItem = CreateMenuItem(_ui.MenuCopyLastTranscription, ObjCSelectors.CopyLastTranscription, "");
        if (_copyItem == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the copy menu item.");
        ObjC.SendVoid(_copyItem, ObjCSelectors.SetTarget, _copyTarget.Handle);
        ObjC.SendVoidBool(_copyItem, ObjCSelectors.SetEnabled, false);
        SetAccessibilityLabel(_copyItem, _ui.MenuCopyLastTranscription);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem, _copyItem);
        ObjC.SendVoid(_copyItem, ObjCSelectors.Release);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem,
            ObjC.Send(ObjCClasses.NSMenuItem, ObjCSelectors.SeparatorItem));

        _quitItem = CreateMenuItem(_ui.MenuQuit, ObjCSelectors.Terminate, "q");
        if (_quitItem == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the status menu.");

        ObjC.SendVoid(_quitItem, ObjCSelectors.SetTarget, application.Handle);
        SetAccessibilityLabel(_quitItem, _ui.MenuQuit);
        ObjC.SendVoid(menu, ObjCSelectors.AddItem, _quitItem);
        ObjC.SendVoid(_statusItem, ObjCSelectors.SetMenu, menu);
        ObjC.SendVoid(_quitItem, ObjCSelectors.Release);
        ObjC.SendVoid(menu, ObjCSelectors.Release);
    }

    public void SetLanguage(string effectiveUiLanguage)
    {
        _ui = new MacUiText(effectiveUiLanguage);
        ObjC.SendVoid(_stateItem, ObjCSelectors.SetTitle, NSStringRef.From(_ui.MenuStarting));
        ObjC.SendVoid(_openItem, ObjCSelectors.SetTitle, NSStringRef.From(_ui.MenuOpen));
        ObjC.SendVoid(_copyItem, ObjCSelectors.SetTitle, NSStringRef.From(_ui.MenuCopyLastTranscription));
        ObjC.SendVoid(_quitItem, ObjCSelectors.SetTitle, NSStringRef.From(_ui.MenuQuit));
        SetAccessibilityLabel(_button, _ui.AccessibilityLabel);
        SetAccessibilityLabel(_openItem, _ui.MenuOpen);
        SetAccessibilityLabel(_copyItem, _ui.MenuCopyLastTranscription);
        SetAccessibilityLabel(_quitItem, _ui.MenuQuit);
    }

    public void SetState(string title, string detail)
    {
        ObjC.SendVoid(_button, ObjCSelectors.SetTitle, NSStringRef.From(title));
        ObjC.SendVoid(_stateItem, ObjCSelectors.SetTitle, NSStringRef.From(detail));
        SetAccessibilityLabel(_button, $"{title}: {detail}");
    }

    private static void SetAccessibilityLabel(IntPtr handle, string value)
        => ObjC.SendVoid(handle, ObjCSelectors.SetAccessibilityLabel, NSStringRef.From(value));

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
        OpenRequested = null;
        CopyLastTranscriptionRequested = null;
        CanCopyLastTranscription = null;
        ObjC.SendVoid(_statusBar, ObjCSelectors.RemoveStatusItem, _statusItem);
        _statusItem = IntPtr.Zero;
        _openTarget.Dispose();
        _copyTarget.Dispose();
    }
}
