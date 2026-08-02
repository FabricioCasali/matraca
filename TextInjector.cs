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
    /// <summary>
    /// Assinatura posta em dwExtraInfo de tudo que injetamos, p/ o hook de teclado reconhecer
    /// os proprios eventos e deixa-los passar sem trabalho. Ver o comentario em SendUnicode.
    /// </summary>
    /// <remarks>Cabe em 32 bits de proposito: IntPtr tem esse tamanho num processo x86.</remarks>
    public const int InjectionTag = 0x4D54_5243; // "MTRC"

    // Toda entrega por SendInput passa por aqui, uma de cada vez. Dois Task.Run soltos iriam
    // para workers diferentes do pool e podem se ultrapassar — no modo live isso faz o Enter
    // final chegar antes do ultimo pedaco de texto, enviando a mensagem pela metade.
    private static readonly object _chainGate = new();
    private static Task _chain = Task.CompletedTask;

    private static void Enqueue(Action work)
    {
        lock (_chainGate)
            _chain = _chain.ContinueWith(_ =>
            {
                try { work(); }
                catch (Exception ex) { Logger.Error("Falha ao entregar o texto", ex); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public static void PasteText(string text, bool autoEnter, string method)
    {
        if (string.IsNullOrEmpty(text))
        {
            // "so o Enter" nao passa pelo clipboard: Clipboard.SetText("") lanca, e a cola seria
            // abortada antes de chegar no Enter.
            if (autoEnter) Enqueue(SendEnter);
            return;
        }

        if (method == "clipboard")
        {
            // precisa da UI thread (STA): o Clipboard do WinForms exige
            PasteViaClipboard(text, autoEnter);
            return;
        }

        // ATENCAO: NAO digitar na UI thread.
        //
        // O hook global de teclado (WH_KEYBOARD_LL) vive na UI thread, e TODO evento que a
        // gente injeta passa por ele. Digitando aqui, a thread fica presa dentro do SendInput
        // e nao consegue atender os callbacks dos eventos que ela mesma esta' injetando —
        // estourado o LowLevelHooksTimeout (~300ms), o Windows DESCARTA os eventos. O sintoma
        // e' texto chegando sem espacos e cortado no meio. Numa thread de fundo a UI fica
        // livre p/ servir o hook, e nada se perde.
        Enqueue(() =>
        {
            try
            {
                SendUnicode(text);
                if (autoEnter) SendEnter();
            }
            catch (Exception ex) { Logger.Error("Falha ao digitar o texto", ex); }
        });
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
    /// Devolve false apenas quando a janela nao existe mais; o resto e' melhor esforco.
    /// </summary>
    public static bool SendToWindow(IntPtr hwnd, string text, bool autoEnter)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        if (string.IsNullOrEmpty(text))
        {
            if (autoEnter) PostEnter(ResolveTextTarget(hwnd));
            return true;
        }

        IntPtr target = ResolveTextTarget(hwnd);
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
    /// Descobre qual controle deve receber o texto DENTRO da janela alvo.
    ///
    /// GetFocus() e' por thread e so' responde por uma thread que esteja em primeiro plano —
    /// justamente o que a janela fixada NAO esta'. Por isso ha' um segundo caminho: varrer as
    /// janelas filhas atras de um controle de edicao conhecido. Sem isso o WM_CHAR ia parar no
    /// frame da janela, que simplesmente o descarta (foi o que aconteceu no Notepad++ e no
    /// Bloco de Notas).
    /// </summary>
    private static IntPtr ResolveTextTarget(IntPtr topLevel)
    {
        uint targetThread = GetWindowThreadProcessId(topLevel, out _);
        uint ourThread = GetCurrentThreadId();

        if (targetThread != 0 && targetThread != ourThread
            && AttachThreadInput(ourThread, targetThread, true))
        {
            try
            {
                IntPtr focus = GetFocus();
                if (focus != IntPtr.Zero) return focus;
            }
            finally { AttachThreadInput(ourThread, targetThread, false); }
        }

        return FindEditChild(topLevel) ?? topLevel;
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
                if (!IsWindowVisible(child)) return true;   // segue procurando
                var cls = new System.Text.StringBuilder(128);
                if (GetClassName(child, cls, cls.Capacity) == 0) return true;
                var name = cls.ToString();
                foreach (var hint in EditClassHints)
                {
                    if (name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found = child;
                    return false;   // achou: para a varredura
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { Logger.Warn("Falha ao varrer janelas filhas: " + ex.Message); }
        return found;
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
    /// sempre via SendInput, mesmo com pasteMethod=clipboard — o Clipboard do WinForms exigiria
    /// a UI thread, que e' justamente a que precisa ficar livre aqui.
    /// </summary>
    public static void DeliverWithFocus(IntPtr hwnd, string text, bool autoEnter,
                                        Action<bool> onDone)
    {
        if (!IsWindowAlive(hwnd)) { onDone(false); return; }

        var previous = GetForegroundWindow();

        Enqueue(() =>
        {
            bool ok = false;
            try
            {
                FocusWindow(hwnd);

                // Sem esperar a troca efetivar, o SendInput cairia na janela antiga: o
                // SetForegroundWindow retorna antes de o Windows concluir a mudanca.
                if (!WaitForForeground(hwnd, 800))
                {
                    Logger.Warn("A janela fixada nao veio pro primeiro plano a tempo; nao "
                              + "entreguei o texto p/ nao colar na janela errada.");
                }
                else
                {
                    SendUnicode(text);
                    if (autoEnter) SendEnter();

                    // O SendInput e' assincrono: enfileira os eventos e volta na hora, sem
                    // esperar o app alvo consumi-los. Devolver o foco aqui cortaria o fim do
                    // texto — os ultimos caracteres chegariam com a janela ja' trocada.
                    Thread.Sleep(SettleMsFor(text));
                    ok = true;
                }
            }
            catch (Exception ex) { Logger.Error("Falha ao entregar na janela fixada", ex); }
            finally
            {
                // devolve o foco mesmo se a digitacao falhou, p/ nao largar o usuario na
                // janela errada
                try { if (previous != hwnd && IsWindowAlive(previous)) FocusWindow(previous); }
                catch (Exception ex) { Logger.Warn("Falha ao devolver o foco: " + ex.Message); }
                onDone(ok);
            }
        });
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
