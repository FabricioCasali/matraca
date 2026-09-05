using System.Runtime.InteropServices;

namespace Matraca;

/// <summary>
/// Entrega o texto ditado na janela em foco, por um de dois caminhos:
///  - "unicode" (padrao): digita direto via SendInput/KEYEVENTF_UNICODE, sem tocar no clipboard;
///  - "clipboard": copia e manda Ctrl+V, restaurando o conteudo anterior do clipboard.
/// Chamado somente pela thread STA dedicada da DeliveryQueue. Ela nao hospeda o hook.
/// </summary>
internal static class TextInjector
{
    /// <summary>
    /// Assinatura posta em dwExtraInfo de tudo que injetamos, p/ o hook de teclado reconhecer
    /// os proprios eventos e deixa-los passar sem trabalho. Ver o comentario em SendUnicode.
    /// </summary>
    /// <remarks>Cabe em 32 bits de proposito: IntPtr tem esse tamanho num processo x86.</remarks>
    public const int InjectionTag = 0x4D54_5243; // "MTRC"

    public static bool PasteText(string text, bool autoEnter, string method)
    {
        if (string.IsNullOrEmpty(text))
        {
            return !autoEnter || SendEnter();
        }

        if (method == "clipboard")
            return PasteViaClipboard(text, autoEnter);

        return SendUnicode(text) && (!autoEnter || SendEnter());
    }

    private static bool PasteViaClipboard(string text, bool autoEnter)
    {
        if (!WindowsClipboard.TryWriteText(text, out WindowsClipboardSnapshot? previous))
        {
            Logger.Error("Nao consegui escrever no clipboard; abortando cola.");
            return false;
        }

        using (previous)
        {
            bool delivered = SendCtrlV() && (!autoEnter || SendEnter());
            Thread.Sleep(500);
            if (previous.RestoreIfOwned() == WindowsClipboardRestoreResult.Failed)
                Logger.Warn("Nao consegui restaurar o clipboard anterior.");
            return delivered;
        }
    }

    // Rajada grande de KEYEVENTF_UNICODE estoura a fila de mensagens de alguns alvos
    // (terminal, apps Electron) e caracteres somem. Envia em blocos com uma folga minima.
    private const int UnicodeChunkChars = 40;
    private const int UnicodeChunkPauseMs = 2;

    private static bool SendUnicode(string text)
    {
        bool sentAll = true;
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
            sentAll &= Send(inputs);
            if (start + len < text.Length) Thread.Sleep(UnicodeChunkPauseMs);
        }
        return sentAll;
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
                dwExtraInfo = (IntPtr)InjectionTag,
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
    /// So aceita controles de edicao Win32 conhecidos e confirma cada PostMessage.
    /// </summary>
    public static TextDeliveryResult SendToWindow(IntPtr hwnd, string text, bool autoEnter)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return TextDeliveryResult.TargetUnavailable;

        IntPtr? resolvedTarget = ResolveTextTarget(hwnd);
        if (resolvedTarget == null)
            return IsWindow(hwnd)
                ? TextDeliveryResult.Unsupported
                : TextDeliveryResult.TargetUnavailable;
        IntPtr target = resolvedTarget.Value;
        if (!IsWindow(hwnd)) return TextDeliveryResult.TargetUnavailable;
        if (!IsWindow(target)) return TextDeliveryResult.Failed;

        if (string.IsNullOrEmpty(text))
        {
            if (autoEnter && !PostEnter(target)) return PostingFailureFor(hwnd);
            return TextDeliveryResult.Delivered;
        }

        foreach (char ch in text)
        {
            if (ch == '\r') continue;                       // trata CRLF como um Enter so
            bool posted = ch == '\n'
                ? PostEnter(target)
                : PostMessage(target, WM_CHAR, (IntPtr)ch, IntPtr.Zero);
            if (!posted) return PostingFailureFor(hwnd);
        }
        if (autoEnter && !PostEnter(target)) return PostingFailureFor(hwnd);
        return TextDeliveryResult.Delivered;
    }

    private static TextDeliveryResult PostingFailureFor(IntPtr topLevel)
        => IsWindow(topLevel) ? TextDeliveryResult.Failed : TextDeliveryResult.TargetUnavailable;

    private static bool PostEnter(IntPtr hwnd)
    {
        bool keyDown = PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
        bool character = PostMessage(hwnd, WM_CHAR, (IntPtr)'\r', IntPtr.Zero);
        bool keyUp = PostMessage(hwnd, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
        return keyDown && character && keyUp;
    }

    /// <summary>
    /// Descobre qual controle deve receber o texto DENTRO da janela alvo.
    ///
    /// GetFocus() e' por thread e so' responde por uma thread que esteja em primeiro plano —
    /// justamente o que a janela fixada NAO esta'. Por isso ha' um segundo caminho: varrer as
    /// janelas filhas atras de um controle de edicao conhecido. Sem isso o WM_CHAR ia parar no
    /// frame da janela, que simplesmente o descarta (foi o que aconteceu no Notepad++ e no
    /// Bloco de Notas).
    /// </summary>
    private static IntPtr? ResolveTextTarget(IntPtr topLevel)
    {
        uint targetThread = GetWindowThreadProcessId(topLevel, out _);
        uint ourThread = GetCurrentThreadId();

        if (targetThread != 0 && targetThread != ourThread
            && AttachThreadInput(ourThread, targetThread, true))
        {
            try
            {
                IntPtr focus = GetFocus();
                if (focus != IntPtr.Zero
                    && (focus == topLevel || IsChild(topLevel, focus))
                    && IsKnownEditableControl(focus))
                    return focus;
            }
            finally { AttachThreadInput(ourThread, targetThread, false); }
        }

        if (IsKnownEditableControl(topLevel)) return topLevel;
        return FindEditChild(topLevel);
    }

    private static readonly string[] EditClassHints =
        { "Edit", "RichEdit", "Scintilla", "TextBox" };

    /// <summary>Primeira janela filha visivel cuja classe parece um campo de texto.</summary>
    private static IntPtr? FindEditChild(IntPtr parent)
    {
        IntPtr? found = null;
        try
        {
            EnumChildWindows(parent, (child, _) =>
            {
                if (!IsKnownEditableControl(child)) return true;
                found = child;
                return false;   // achou: para a varredura
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Logger.Warn("Falha ao varrer janelas filhas: " + ex.Message); }
        return found;
    }

    private static bool IsKnownEditableControl(IntPtr hwnd)
    {
        if (!IsWindow(hwnd) || !IsWindowVisible(hwnd) || !IsWindowEnabled(hwnd)) return false;
        var cls = new System.Text.StringBuilder(128);
        if (GetClassName(hwnd, cls, cls.Capacity) == 0) return false;
        string name = cls.ToString();
        return EditClassHints.Any(hint =>
            name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>Handle da janela que esta em primeiro plano agora.</summary>
    public static IntPtr GetForegroundWindowHandle() => GetForegroundWindow();

    /// <summary>
    /// Traz uma janela pro primeiro plano (restaurando se estiver minimizada).
    ///
    /// O Windows nao deixa um processo qualquer roubar o primeiro plano: quem nao recebeu o
    /// ultimo evento de entrada so' consegue piscar o botao na barra de tarefas. O jeito
    /// consagrado de contornar e' grudar nossa fila de entrada na da thread que esta' em
    /// primeiro plano pelo instante da troca — dai o SetForegroundWindow e' aceito.
    /// </summary>
    public static void FocusWindow(IntPtr hwnd)
    {
        if (!IsWindowAlive(hwnd)) return;
        try
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

            uint ourThread = GetCurrentThreadId();
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);

            bool attached = fgThread != 0 && fgThread != ourThread
                            && AttachThreadInput(ourThread, fgThread, true);
            try { SetForegroundWindow(hwnd); }
            finally { if (attached) AttachThreadInput(ourThread, fgThread, false); }
        }
        catch (Exception ex) { Logger.Warn("Falha ao focar a janela: " + ex.Message); }
    }

    /// <summary>
    /// Entrega o texto numa janela especifica TRAZENDO-A pro primeiro plano, e devolvendo o
    /// foco pra onde estava. Ao contrario do <see cref="SendToWindow"/>, funciona em qualquer
    /// alvo — terminal, Electron, UWP — porque o texto entra pelo caminho normal de teclado.
    /// O preco e' a janela piscar na tela por um instante.
    ///
    /// Roda inteiro numa thread de fundo (ver o aviso em PasteText: digitar na UI thread faz o
    /// proprio hook de teclado engasgar e o Windows descartar caracteres). Por isso digita
    /// sempre via SendInput para manter a thread do hook livre.
    /// </summary>
    public static bool DeliverWithFocus(IntPtr hwnd, string text, bool autoEnter)
    {
        if (!IsWindowAlive(hwnd)) return false;

        var previous = GetForegroundWindow();
        bool delivered = false;
        try
        {
            FocusWindow(hwnd);
            if (!WaitForForeground(hwnd, 800))
            {
                Logger.Warn("A janela fixada nao veio pro primeiro plano a tempo; nao "
                          + "entreguei o texto p/ nao colar na janela errada.");
            }
            else
            {
                delivered = SendUnicode(text) && (!autoEnter || SendEnter());
                Thread.Sleep(SettleMsFor(text));
            }
        }
        catch (Exception ex) { Logger.Error("Falha ao entregar na janela fixada", ex); }
        finally
        {
            try { if (previous != hwnd && IsWindowAlive(previous)) FocusWindow(previous); }
            catch (Exception ex) { Logger.Warn("Falha ao devolver o foco: " + ex.Message); }
        }
        return delivered;
    }

    /// <summary>
    /// Folga pro app alvo drenar a fila de entrada antes de a gente mexer no foco. Cresce com o
    /// tamanho do texto porque a fila tambem cresce; limitada nas pontas p/ nao travar a
    /// devolucao do foco em textos enormes nem encurtar demais nos curtos.
    /// </summary>
    private static int SettleMsFor(string text) => Math.Clamp(120 + text.Length * 2, 200, 1500);

    private static bool WaitForForeground(IntPtr hwnd, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (GetForegroundWindow() == hwnd) return true;
            Thread.Sleep(15);
        }
        return GetForegroundWindow() == hwnd;
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

    // ---- SendInput ----
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const ushort VK_RETURN = 0x0D;

    private static bool SendCtrlV() => Send(new[]
    {
        Key(VK_CONTROL, false),
        Key(VK_V, false),
        Key(VK_V, true),
        Key(VK_CONTROL, true),
    });

    private static bool SendEnter() => Send(new[]
    {
        Key(VK_RETURN, false),
        Key(VK_RETURN, true),
    });

    private static bool Send(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            Logger.Warn($"SendInput enviou {sent}/{inputs.Length} eventos (err {Marshal.GetLastWin32Error()}).");
        return sent == inputs.Length;
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
                dwExtraInfo = (IntPtr)InjectionTag,
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

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

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
