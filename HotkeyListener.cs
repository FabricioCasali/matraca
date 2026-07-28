using System.Runtime.InteropServices;

namespace Matraca;

/// <summary>Modificadores aceitos num atalho combinado (Ctrl+Alt+X).</summary>
[Flags]
internal enum KeyMods
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

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

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;   // Alt
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly LowLevelKeyboardProc _proc; // manter referencia viva (anti-GC)
    private IntPtr _hook = IntPtr.Zero;

    private readonly bool _discover;
    private readonly int _targetVk;
    private readonly KeyMods _targetMods;
    private readonly int _pinVk;               // 0 = recurso de fixar janela desligado
    private readonly KeyMods _pinMods;
    private readonly bool _holdMode;
    private bool _isDown;    // p/ ignorar auto-repeat no modo toggle
    private bool _pinDown;   // idem, p/ a tecla de fixar janela

    /// <summary>Modo toggle: dispara a cada pressionar. Modo hold: true=apertou, false=soltou.</summary>
    public event Action<bool>? Triggered;
    /// <summary>Modo descoberta: reporta a tecla (e os modificadores) que foi pressionada.</summary>
    public event Action<int, KeyMods>? KeyDiscovered;
    /// <summary>Tecla de fixar/soltar a janela de destino do ditado.</summary>
    public event Action? PinToggled;

    /// <summary>Enquanto true, o hook deixa tudo passar (usado pela tela de config
    /// durante a captura de tecla, p/ o atalho atual nao disparar gravacao).</summary>
    public bool Suspended { get; set; }

    public HotkeyListener(Config cfg)
        : this(cfg.DiscoverMode, cfg.HotkeyVk, cfg.HotkeyMods, cfg.PinHotkeyVk, cfg.PinHotkeyMods,
               cfg.Mode == "hold" || cfg.Mode == "push") { }

    private HotkeyListener(bool discover, int targetVk, KeyMods targetMods,
                           int pinVk, KeyMods pinMods, bool holdMode)
    {
        _discover = discover;
        _targetVk = targetVk;
        _targetMods = targetMods;
        _pinVk = pinVk;
        _pinMods = pinMods;
        _holdMode = holdMode; // hold/push precisam do evento de soltar
        _proc = HookCallback;
    }

    /// <summary>Hook avulso so pra descobrir teclas (tela de config). Nao engole nada.</summary>
    public static HotkeyListener CreateDiscovery()
        => new(true, 0, KeyMods.None, 0, KeyMods.None, false);

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
                // ignora modificador puro: quem aperta Ctrl+Alt+X passa por Ctrl e Alt antes
                if (down && !IsModifierVk(vk)) KeyDiscovered?.Invoke(vk, CurrentMods());
                // nao engole a tecla no modo descoberta
            }
            else if (vk == _targetVk)
            {
                var handled = HandleTarget(down, up, _targetMods, ref _isDown, pressed =>
                {
                    if (pressed || _holdMode) Triggered?.Invoke(pressed);
                });
                if (handled) return (IntPtr)1; // engole a tecla alvo (nao propaga p/ outros apps)
            }
            else if (_pinVk != 0 && vk == _pinVk)
            {
                var handled = HandleTarget(down, up, _pinMods, ref _pinDown, pressed =>
                {
                    if (pressed) PinToggled?.Invoke();
                });
                if (handled) return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Trata uma tecla alvo. Devolve true quando o evento foi consumido (e deve ser engolido).
    /// Com modificadores configurados, so consome se eles baterem exatamente — senao a tecla
    /// precisa seguir o caminho normal, ou o atalho sequestraria a tecla pura do usuario.
    /// </summary>
    private static bool HandleTarget(bool down, bool up, KeyMods required, ref bool isDown,
                                     Action<bool> fire)
    {
        if (down)
        {
            if (isDown) return true;                   // auto-repeat do teclado
            if (CurrentMods() != required) return false; // combo nao bate: deixa passar
            isDown = true;
            fire(true);
            return true;
        }
        if (up)
        {
            // no soltar os modificadores ja podem ter sido liberados; o que vale e' se
            // o pressionar correspondente foi nosso.
            if (!isDown) return false;
            isDown = false;
            fire(false);
            return true;
        }
        return false;
    }

    private static bool IsModifierVk(int vk)
        => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN
            or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5; // L/R Shift, Ctrl, Alt

    private static KeyMods CurrentMods()
    {
        var m = KeyMods.None;
        if (IsPhysicallyDown(VK_CONTROL)) m |= KeyMods.Ctrl;
        if (IsPhysicallyDown(VK_MENU)) m |= KeyMods.Alt;
        if (IsPhysicallyDown(VK_SHIFT)) m |= KeyMods.Shift;
        if (IsPhysicallyDown(VK_LWIN) || IsPhysicallyDown(VK_RWIN)) m |= KeyMods.Win;
        return m;
    }

    private static bool IsPhysicallyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
