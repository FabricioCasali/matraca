using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Matraca;

/// <summary>
/// Cola texto na janela em foco via clipboard + Ctrl+V (SendInput),
/// preservando o conteudo anterior do clipboard.
/// IMPORTANTE: chamar na UI thread (STA), por causa do Clipboard do WinForms.
/// </summary>
internal static class TextInjector
{
    public static void PasteText(string text, bool autoEnter)
    {
        if (string.IsNullOrEmpty(text)) return;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
