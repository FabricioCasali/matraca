using System.Runtime.InteropServices;

namespace Matraca;

internal sealed class WindowsHotkeyCapture : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int KbdExtraInfoOffset = 16;

    private readonly object _gate = new();
    private readonly WindowsHotkeyCaptureProc _proc;
    private readonly TaskCompletionSource<HotkeyGesture> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private IntPtr _hook;
    private int _capturedVirtualKey;
    private HotkeyGesture? _capturedGesture;
    private int _disposed;

    public WindowsHotkeyCapture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException(
                $"Falha ao iniciar captura do atalho (Win32 {Marshal.GetLastWin32Error()}).");
        _cancellationRegistration = cancellationToken.Register(Cancel);
    }

    public Task<HotkeyGesture> Completion => _completion.Task;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
        if (Marshal.ReadIntPtr(lParam, KbdExtraInfoOffset) == (IntPtr)TextInjector.InjectionTag)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        int message = (int)wParam;
        int virtualKey = Marshal.ReadInt32(lParam);
        bool down = message is WmKeyDown or WmSysKeyDown;
        bool up = message is WmKeyUp or WmSysKeyUp;
        if (!down && !up) return CallNextHookEx(_hook, nCode, wParam, lParam);

        if (down && _capturedGesture == null && !IsModifier(virtualKey))
        {
            _capturedVirtualKey = virtualKey;
            _capturedGesture = new HotkeyGesture(
                WindowsHotkeyTranslator.NameForVirtualKey(virtualKey),
                CurrentModifiers());
        }
        else if (up && virtualKey == _capturedVirtualKey && _capturedGesture != null)
        {
            HotkeyGesture gesture = _capturedGesture;
            Unhook();
            _completion.TrySetResult(gesture);
        }

        return (IntPtr)1;
    }

    private void Cancel()
    {
        Unhook();
        _completion.TrySetCanceled();
    }

    private void Unhook()
    {
        lock (_gate)
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static bool IsModifier(int virtualKey)
        => virtualKey is VkShift or VkControl or VkMenu or VkLWin or VkRWin
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static KeyMods CurrentModifiers()
    {
        KeyMods modifiers = KeyMods.None;
        if (IsDown(VkControl)) modifiers |= KeyMods.Ctrl;
        if (IsDown(VkMenu)) modifiers |= KeyMods.Alt;
        if (IsDown(VkShift)) modifiers |= KeyMods.Shift;
        if (IsDown(VkLWin) || IsDown(VkRWin)) modifiers |= KeyMods.Win;
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Unhook();
        _cancellationRegistration.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        WindowsHotkeyCaptureProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
