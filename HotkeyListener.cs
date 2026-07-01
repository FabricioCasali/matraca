using System.Runtime.InteropServices;

namespace Matraca;

/// <summary>
/// Hook global de teclado (WH_KEYBOARD_LL). Necessario p/ capturar teclas incomuns
/// (F13-F24, media keys) que RegisterHotKey nem sempre pega.
/// Precisa rodar numa thread com message loop (a UI thread do WinForms).
/// </summary>
internal sealed class HotkeyListener : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc; // manter referencia viva (anti-GC)
    private IntPtr _hook = IntPtr.Zero;

    private readonly bool _discover;
    private readonly int _targetVk;
    private readonly bool _holdMode;
    private bool _isDown; // p/ ignorar auto-repeat no modo toggle

    /// <summary>Modo toggle: dispara a cada pressionar. Modo hold: true=apertou, false=soltou.</summary>
    public event Action<bool>? Triggered;
    /// <summary>Modo descoberta: reporta o vkCode de qualquer tecla pressionada.</summary>
    public event Action<int>? KeyDiscovered;

    /// <summary>Enquanto true, o hook deixa tudo passar (usado pela tela de config
    /// durante a captura de tecla, p/ o atalho atual nao disparar gravacao).</summary>
    public bool Suspended { get; set; }

    public HotkeyListener(Config cfg)
        : this(cfg.DiscoverMode, cfg.HotkeyVk, cfg.Mode == "hold" || cfg.Mode == "push") { }

    private HotkeyListener(bool discover, int targetVk, bool holdMode)
    {
        _discover = discover;
        _targetVk = targetVk;
        _holdMode = holdMode; // hold/push precisam do evento de soltar
        _proc = HookCallback;
    }

    /// <summary>Hook avulso so pra descobrir teclas (tela de config). Nao engole nada.</summary>
    public static HotkeyListener CreateDiscovery() => new(true, 0, false);

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
        if (nCode >= 0 && !Suspended)
        {
            int msg = (int)wParam;
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode (primeiro campo)
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (_discover)
            {
                if (down) KeyDiscovered?.Invoke(vk);
                // nao engole a tecla no modo descoberta
            }
            else if (vk == _targetVk)
            {
                if (_holdMode)
                {
                    if (down && !_isDown) { _isDown = true; Triggered?.Invoke(true); }
                    else if (up) { _isDown = false; Triggered?.Invoke(false); }
                }
                else // toggle
                {
                    if (down && !_isDown) { _isDown = true; Triggered?.Invoke(true); }
                    else if (up) { _isDown = false; }
                }
                return (IntPtr)1; // engole a tecla alvo (nao propaga p/ outros apps)
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
