using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Matraca;

/// <summary>
/// Entrega o texto ditado na janela em foco, por um de dois caminhos:
///  - "unicode" (padrao): digita direto via SendInput/KEYEVENTF_UNICODE, sem tocar no clipboard;
///  - "clipboard": copia e manda Ctrl+V, restaurando o conteudo anterior do clipboard.
/// IMPORTANTE: chamar na UI thread (STA), por causa do Clipboard do WinForms.
/// </summary>
internal static class TextInjector
{
    public static void PasteText(string text, bool autoEnter, string method)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (method == "clipboard")
        {
            PasteViaClipboard(text, autoEnter);
        }
        else
        {
            SendUnicode(text);
            if (autoEnter) SendEnter();
        }
    }

    private static void PasteViaClipboard(string text, bool autoEnter)
    {
        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); }
        catch (Exception ex) { Logger.Warn("Nao consegui ler clipboard anterior: " + ex.Message); }

        if (!TrySetClipboard(text))
        {
            Logger.Error("Nao consegui escrever no clipboard; abortando cola.");
            return;
        }

        SendCtrlV();
        if (autoEnter) SendEnter();

        // restaura o clipboard anterior depois de um tempinho (na UI thread)
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            timer.Dispose();
            try
            {
                if (previous != null) Clipboard.SetText(previous);
                else Clipboard.Clear();
            }
            catch { /* ignora */ }
        };
        timer.Start();
    }

    // Rajada grande de KEYEVENTF_UNICODE estoura a fila de mensagens de alguns alvos
    // (terminal, apps Electron) e caracteres somem. Envia em blocos com uma folga minima.
    private const int UnicodeChunkChars = 40;
    private const int UnicodeChunkPauseMs = 2;

    private static void SendUnicode(string text)
    {
        for (int start = 0; start < text.Length; start += UnicodeChunkChars)
        {
            int len = Math.Min(UnicodeChunkChars, text.Length - start);
            var inputs = new INPUT[len * 2];
            for (int i = 0; i < len; i++)
            {
                ushort ch = text[start + i];
                inputs[i * 2]     = UnicodeKey(ch, keyUp: false);
                inputs[i * 2 + 1] = UnicodeKey(ch, keyUp: true);
            }
            Send(inputs);
            if (start + len < text.Length) Thread.Sleep(UnicodeChunkPauseMs);
        }
    }

    private static INPUT UnicodeKey(ushort ch, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = keyUp ? KEYEVENTF_UNICODE | KEYEVENTF_KEYUP : KEYEVENTF_UNICODE,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };

    // ---- entrega numa janela fixa, sem traze-la pro primeiro plano ----

    /// <summary>
    /// Posta o texto direto na fila do controle com foco da janela alvo (WM_CHAR), sem
    /// SetForegroundWindow — a janela nao pisca nem rouba o foco de onde voce esta.
    /// LIMITE CONHECIDO: so funciona em alvos que processam mensagens postadas (campos Win32,
    /// Notepad, muita coisa nativa). Terminal, console e apps Chromium/Electron fazem o proprio
    /// tratamento de entrada e simplesmente ignoram — nesses o texto nao aparece.
    /// Devolve false apenas quando a janela nao existe mais; o resto e' melhor esforco.
    /// </summary>
    public static bool SendToWindow(IntPtr hwnd, string text, bool autoEnter)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        if (string.IsNullOrEmpty(text)) return true;

        IntPtr target = ResolveFocusedChild(hwnd);
        foreach (char ch in text)
        {
            if (ch == '\r') continue;                       // trata CRLF como um Enter so
            if (ch == '\n') { PostEnter(target); continue; }
            PostMessage(target, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
        }
        if (autoEnter) PostEnter(target);
        return true;
    }

    private static void PostEnter(IntPtr hwnd)
    {
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
        PostMessage(hwnd, WM_CHAR, (IntPtr)'\r', IntPtr.Zero);
        PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
    }

    /// <summary>
    /// Descobre qual controle tem o foco DENTRO da janela alvo. GetFocus() e' por thread, entao
    /// e' preciso grudar nossa fila de entrada na dela por um instante. Sem isso o texto iria
    /// pro frame da janela em vez do campo de edicao.
    /// </summary>
    private static IntPtr ResolveFocusedChild(IntPtr topLevel)
    {
        uint targetThread = GetWindowThreadProcessId(topLevel, out _);
        uint ourThread = GetCurrentThreadId();
        if (targetThread == 0 || targetThread == ourThread) return topLevel;

        if (!AttachThreadInput(ourThread, targetThread, true)) return topLevel;
        try
        {
            IntPtr focus = GetFocus();
            return focus != IntPtr.Zero ? focus : topLevel;
        }
        finally { AttachThreadInput(ourThread, targetThread, false); }
    }

    /// <summary>Handle da janela que esta em primeiro plano agora.</summary>
    public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

    /// <summary>
    /// Traz uma janela pro primeiro plano (restaurando se estiver minimizada). Usado pela tela
    /// de historico p/ devolver o foco antes de recolar — ao contrario do pin de destino, aqui
    /// roubar o foco e' justamente o que se quer.
    /// </summary>
    public static void FocusWindow(IntPtr hwnd)
    {
        if (!IsWindowAlive(hwnd)) return;
        try
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex) { Logger.Warn("Falha ao focar a janela: " + ex.Message); }
    }

    /// <summary>A janela ainda existe?</summary>
    public static bool IsWindowAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

    /// <summary>Titulo da janela (vazio se nao tiver ou nao existir mais).</summary>
    public static string GetWindowTitle(IntPtr hwnd)
    {
        if (!IsWindowAlive(hwnd)) return "";
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new System.Text.StringBuilder(len + 1);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static bool TrySetClipboard(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch { Thread.Sleep(30); }
        }
        return false;
    }

    // ---- SendInput ----
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;

    private static void SendCtrlV() => Send(new[]
    {
        Key(VK_CONTROL, false),
        Key(VK_V, false),
        Key(VK_V, true),
        Key(VK_CONTROL, true),
    });

    private static void SendEnter() => Send(new[]
    {
        Key(VK_RETURN, false),
        Key(VK_RETURN, true),
    });

    private static void Send(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            Logger.Warn($"SendInput enviou {sent}/{inputs.Length} eventos (err {Marshal.GetLastWin32Error()}).");
    }

    private static INPUT Key(ushort vk, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
