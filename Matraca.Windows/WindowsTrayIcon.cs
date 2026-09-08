using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsTrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = WindowsNativeMethods.WmApp + 0x32;

    private readonly WindowsIconSet _icons;
    private readonly Dictionary<uint, Action> _menuActions = new();
    private readonly WindowsNativeWindow _window;
    private readonly nint _menu;
    private readonly uint _taskbarCreatedMessage;
    private bool _usesVersion4;
    private bool _visible;
    private int _disposed;

    public WindowsTrayIcon(WindowsIconSet icons, string toolTip = "Matraca")
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        ToolTip = Truncate(toolTip, 127);
        _taskbarCreatedMessage = WindowsNativeMethods.RegisterWindowMessage("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao registrar TaskbarCreated.");

        _window = new WindowsNativeWindow("TrayIcon", WindowProcedure, messageOnly: false);
        _menu = WindowsNativeMethods.CreatePopupMenu();
        if (_menu != nint.Zero) return;

        int error = Marshal.GetLastWin32Error();
        _window.Dispose();
        throw new Win32Exception(error, "Falha ao criar o menu da bandeja.");
    }

    public event Action? Activated;

    public event Action? AppearanceChanged;

    public Func<bool>? MenuOpening { get; set; }

    public event Action? MenuClosed;

    public ShellState CurrentState { get; private set; } = ShellState.Idle;

    public string ToolTip { get; private set; }

    public bool IsVisible => _visible;

    public void Show()
    {
        VerifyNotDisposed();
        _window.VerifyAccess();
        if (_visible) return;

        if (!AddIcon())
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao adicionar o icone na bandeja.");

        _visible = true;
    }

    public void Hide()
    {
        VerifyNotDisposed();
        _window.VerifyAccess();
        DeleteIcon();
    }

    public void SetState(ShellState state, string toolTip)
    {
        VerifyNotDisposed();
        _window.VerifyAccess();
        CurrentState = state;
        ToolTip = Truncate(toolTip, 127);
        if (!_visible) return;

        WindowsNotifyIconData data = CreateData(WindowsNativeMethods.NifIcon |
                                                WindowsNativeMethods.NifTip |
                                                WindowsNativeMethods.NifShowTip);
        if (!WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimModify, ref data))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao atualizar o icone da bandeja.");
    }

    public void ShowNotification(
        string title,
        string message,
        ShellNotificationLevel level = ShellNotificationLevel.Info)
    {
        VerifyNotDisposed();
        _window.VerifyAccess();
        if (!_visible)
            throw new InvalidOperationException("O icone deve estar visivel antes de exibir uma notificacao.");

        WindowsNotifyIconData data = CreateData(WindowsNativeMethods.NifInfo);
        data.InfoTitle = Truncate(title, 63);
        data.Info = Truncate(message, 255);
        data.InfoFlags = level switch
        {
            ShellNotificationLevel.Warning => WindowsNativeMethods.NiifWarning,
            ShellNotificationLevel.Error => WindowsNativeMethods.NiifError,
            _ => WindowsNativeMethods.NiifInfo,
        };

        if (!WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimModify, ref data))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao exibir a notificacao da bandeja.");
    }

    public void AddMenuItem(uint commandId, string text, Action action)
    {
        VerifyNotDisposed();
        _window.VerifyAccess();
        if (commandId is 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(commandId), "O comando deve estar entre 1 e 65535.");
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(action);
        if (_menuActions.ContainsKey(commandId))
            throw new ArgumentException($"O comando {commandId} ja existe no menu.", nameof(commandId));

        if (!WindowsNativeMethods.AppendMenu(_menu, WindowsNativeMethods.MfString, commandId, text))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao adicionar um item ao menu da bandeja.");

        _menuActions.Add(commandId, action);
    }

    public void AddMenuSeparator()
    {
        VerifyNotDisposed();
        _window.VerifyAccess();
        if (!WindowsNativeMethods.AppendMenu(_menu, WindowsNativeMethods.MfSeparator, 0, null))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao adicionar um separador ao menu da bandeja.");
    }

    private nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message is 0x001A or 0x031A or 0x007E) // WM_SETTINGCHANGE / THEMECHANGED / DISPLAYCHANGE
        {
            SetState(CurrentState, ToolTip);
            AppearanceChanged?.Invoke();
        }
        if (message == _taskbarCreatedMessage)
        {
            if (_visible && !AddIcon())
                Logger.Warn($"Falha ao restaurar o icone da bandeja (Win32 {Marshal.GetLastWin32Error()}).");
            return nint.Zero;
        }

        if (message != CallbackMessage)
            return WindowsNativeMethods.DefWindowProcW(window, message, wParam, lParam);

        uint notification = _usesVersion4
            ? unchecked((uint)lParam.ToInt64()) & 0xffff
            : unchecked((uint)lParam.ToInt64());

        switch (notification)
        {
            case WindowsNativeMethods.WmContextMenu:
            case WindowsNativeMethods.WmRButtonUp:
                ShowMenu();
                break;
            case WindowsNativeMethods.NinSelect:
            case WindowsNativeMethods.NinKeySelect:
            case WindowsNativeMethods.WmLButtonUp:
            case WindowsNativeMethods.WmLButtonDoubleClick:
                Activated?.Invoke();
                break;
        }

        return nint.Zero;
    }

    private void ShowMenu()
    {
        if (MenuOpening?.Invoke() == false) return;
        if (!WindowsNativeMethods.GetCursorPos(out WindowsPoint point))
        {
            MenuClosed?.Invoke();
            return;
        }

        nint previousForeground = WindowsNativeMethods.GetForegroundWindow();
        uint command;
        try
        {
            WindowsNativeMethods.SetForegroundWindow(_window.Handle);
            command = WindowsNativeMethods.TrackPopupMenu(
                _menu,
                WindowsNativeMethods.TpmRightButton |
                WindowsNativeMethods.TpmNonotify |
                WindowsNativeMethods.TpmReturnCommand,
                point.X,
                point.Y,
                0,
                _window.Handle,
                nint.Zero);
            WindowsNativeMethods.PostMessageW(_window.Handle, WindowsNativeMethods.WmNull, nint.Zero, nint.Zero);
        }
        finally
        {
            if (previousForeground != nint.Zero && WindowsNativeMethods.IsWindow(previousForeground))
                WindowsNativeMethods.SetForegroundWindow(previousForeground);
            MenuClosed?.Invoke();
        }

        if (command != 0 && _menuActions.TryGetValue(command, out Action? action))
            action();
    }

    private bool AddIcon()
    {
        WindowsNotifyIconData data = CreateData(
            WindowsNativeMethods.NifMessage |
            WindowsNativeMethods.NifIcon |
            WindowsNativeMethods.NifTip |
            WindowsNativeMethods.NifShowTip);
        if (!WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimAdd, ref data))
            return false;

        data.VersionOrTimeout = WindowsNativeMethods.NotifyIconVersion4;
        _usesVersion4 = WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimSetVersion, ref data);
        return true;
    }

    private void DeleteIcon()
    {
        if (!_visible) return;

        WindowsNotifyIconData data = CreateData(0);
        WindowsNativeMethods.ShellNotifyIcon(WindowsNativeMethods.NimDelete, ref data);
        _visible = false;
        _usesVersion4 = false;
    }

    private WindowsNotifyIconData CreateData(uint flags)
        => new()
        {
            Size = (uint)Marshal.SizeOf<WindowsNotifyIconData>(),
            Window = _window.Handle,
            Id = IconId,
            Flags = flags,
            CallbackMessage = CallbackMessage,
            Icon = _icons.Tray(CurrentState, WindowsIconSet.TaskbarTheme(),
                WindowsNativeMethods.GetSystemMetrics(49)), // SM_CXSMICON
            Tip = ToolTip,
            Info = string.Empty,
            InfoTitle = string.Empty,
        };

    private static string Truncate(string? value, int maximumLength)
    {
        value ??= string.Empty;
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private void VerifyNotDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _window.VerifyAccess();
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        DeleteIcon();
        WindowsNativeMethods.DestroyMenu(_menu);
        _menuActions.Clear();
        Activated = null;
        AppearanceChanged = null;
        MenuOpening = null;
        MenuClosed = null;
        _window.Dispose();
    }
}
