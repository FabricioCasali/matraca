using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsKeyboardHook : IKeyboardHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int KbdExtraInfoOffset = 16;

    private const int DictationDown = 1;
    private const int DictationUp = 2;
    private const int Pin = 3;
    private const int Discovery = 4;

    private readonly LowLevelKeyboardProc _proc;
    private readonly AsyncCallbackQueue<(int Kind, int VirtualKey, KeyMods Modifiers)> _callbacks;
    private readonly bool _discover;
    private readonly int _targetVk;
    private readonly KeyMods _targetMods;
    private readonly HotkeyGesture? _targetGesture;
    private readonly HotkeyGesture? _pinGesture;
    private readonly int _pinVk;
    private readonly KeyMods _pinMods;
    private readonly bool _needsKeyUp;

    private IntPtr _hook;
    private bool _isDown;
    private bool _pinDown;

    public event Action<HotkeyGesture, bool>? DictationKeyChanged;
    public event Action<HotkeyGesture>? PinToggled;
    public event Action<HotkeyGesture>? KeyDiscovered;

    public bool Suspended { get; set; }

    public WindowsKeyboardHook(Config config)
        : this(
            config.DiscoverMode,
            config.Hotkey,
            config.PinHotkey,
            config.HotkeyNeedsKeyUp)
    {
    }

    private WindowsKeyboardHook(
        bool discover,
        HotkeyGesture? target,
        HotkeyGesture? pin,
        bool needsKeyUp)
    {
        _discover = discover;
        _targetGesture = target;
        _targetVk = target == null ? 0 : WindowsHotkeyTranslator.ToVirtualKey(target);
        _targetMods = target?.Modifiers ?? KeyMods.None;
        _pinGesture = pin;
        _pinVk = pin == null ? 0 : WindowsHotkeyTranslator.ToVirtualKey(pin);
        _pinMods = pin?.Modifiers ?? KeyMods.None;
        _needsKeyUp = needsKeyUp;
        _proc = HookCallback;
        _callbacks = new AsyncCallbackQueue<(int, int, KeyMods)>(
            Publish,
            exception => Logger.Error("Falha ao publicar evento do teclado", exception));
    }

    public static IKeyboardHook CreateDiscovery()
        => new WindowsKeyboardHook(true, null, null, false);

    public void Start()
    {
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            Logger.Error($"Falha ao instalar hook de teclado (Win32 err {Marshal.GetLastWin32Error()})");
        else
            Logger.Info("Hook global de teclado instalado.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Marshal.ReadIntPtr(lParam, KbdExtraInfoOffset) == (IntPtr)TextInjector.InjectionTag)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        if (nCode >= 0 && !Suspended)
        {
            int message = (int)wParam;
            int virtualKey = Marshal.ReadInt32(lParam);
            bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
            bool up = message == WM_KEYUP || message == WM_SYSKEYUP;

            if (_discover)
            {
                if (down && !IsModifierVk(virtualKey))
                    _callbacks.TryPost((Discovery, virtualKey, CurrentMods()));
            }
            else if (down)
            {
                var modifiers = CurrentMods();
                if (_pinVk != 0 && virtualKey == _pinVk && modifiers == _pinMods && !_pinDown)
                {
                    _pinDown = true;
                    _callbacks.TryPost((Pin, virtualKey, modifiers));
                    return (IntPtr)1;
                }

                if (_targetVk != 0 && virtualKey == _targetVk && modifiers == _targetMods && !_isDown)
                {
                    _isDown = true;
                    _callbacks.TryPost((DictationDown, virtualKey, modifiers));
                    return (IntPtr)1;
                }

                if ((_isDown && virtualKey == _targetVk) || (_pinDown && virtualKey == _pinVk))
                    return (IntPtr)1;
            }
            else if (up)
            {
                if (_pinDown && virtualKey == _pinVk)
                {
                    _pinDown = false;
                    return (IntPtr)1;
                }

                if (_isDown && virtualKey == _targetVk)
                {
                    _isDown = false;
                    if (_needsKeyUp)
                        _callbacks.TryPost((DictationUp, virtualKey, KeyMods.None));
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Publish((int Kind, int VirtualKey, KeyMods Modifiers) signal)
    {
        switch (signal.Kind)
        {
            case DictationDown when _targetGesture != null:
                DictationKeyChanged?.Invoke(_targetGesture, true);
                break;
            case DictationUp when _targetGesture != null:
                DictationKeyChanged?.Invoke(_targetGesture, false);
                break;
            case Pin when _pinGesture != null:
                PinToggled?.Invoke(_pinGesture);
                break;
            case Discovery:
                KeyDiscovered?.Invoke(new HotkeyGesture(
                    WindowsHotkeyTranslator.NameForVirtualKey(signal.VirtualKey),
                    signal.Modifiers));
                break;
        }
    }

    private static bool IsModifierVk(int virtualKey)
        => virtualKey is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static KeyMods CurrentMods()
    {
        var modifiers = KeyMods.None;
        if (IsPhysicallyDown(VK_CONTROL)) modifiers |= KeyMods.Ctrl;
        if (IsPhysicallyDown(VK_MENU)) modifiers |= KeyMods.Alt;
        if (IsPhysicallyDown(VK_SHIFT)) modifiers |= KeyMods.Shift;
        if (IsPhysicallyDown(VK_LWIN) || IsPhysicallyDown(VK_RWIN)) modifiers |= KeyMods.Win;
        return modifiers;
    }

    private static bool IsPhysicallyDown(int virtualKey)
        => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _callbacks.Dispose();
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
