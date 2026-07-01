using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Matraca;

/// <summary>
/// Moldura colorida ao redor da janela em foco enquanto a gravacao esta ativa,
/// mostrando ONDE o texto ditado vai ser colado. O overlay e' click-through
/// (WS_EX_TRANSPARENT) e nunca rouba foco (WS_EX_NOACTIVATE), entao ele proprio
/// nao interfere no ditado. Segue trocas de foco e movimentos da janela via timer.
/// IMPORTANTE: chamar sempre na UI thread (o timer e o Form sao WinForms).
/// </summary>
internal sealed class FocusBorder : IDisposable
{
    private readonly OverlayForm _overlay;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly int _thickness;
    private IntPtr _lastHwnd;
    private RECT _lastRect;
    private bool _visible;

    public FocusBorder(Color color, int thickness, float opacity)
    {
        _thickness = Math.Max(1, thickness);
        _overlay = new OverlayForm
        {
            BackColor = color,
            Opacity = Math.Clamp(opacity, 0.1f, 1f),
        };
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Track();
    }

    /// <summary>Comeca a marcar a janela em foco (chamar ao iniciar gravacao/sessao).</summary>
    public void ShowBorder()
    {
        _lastHwnd = IntPtr.Zero; // forca reposicionamento no 1o tick
        Track();
        _timer.Start();
    }

    /// <summary>Esconde a moldura (chamar ao voltar pro estado ocioso).</summary>
    public void HideBorder()
    {
        _timer.Stop();
        _lastHwnd = IntPtr.Zero;
        HideOverlay();
    }

    private void Track()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == _overlay.Handle || IsIconic(hwnd)
            || !TryGetBounds(hwnd, out RECT r)
            || r.Right - r.Left < 20 || r.Bottom - r.Top < 20)
        {
            HideOverlay();
            _lastHwnd = IntPtr.Zero;
            return;
        }

        if (_visible && hwnd == _lastHwnd && RectEquals(r, _lastRect)) return;
        _lastHwnd = hwnd;
        _lastRect = r;

        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int t = Math.Min(_thickness, Math.Min(w, h) / 2);
        var region = new Region(new Rectangle(0, 0, w, h));
        region.Exclude(new Rectangle(t, t, w - 2 * t, h - 2 * t));
        var old = _overlay.Region;
        _overlay.Region = region;
        old?.Dispose();

        // SetWindowPos com SWP_NOACTIVATE: mostra/posiciona sem tocar no foco atual.
        SetWindowPos(_overlay.Handle, HWND_TOPMOST, r.Left, r.Top, w, h,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _visible = true;
    }

    private void HideOverlay()
    {
        if (!_visible) return;
        _visible = false;
        if (_overlay.IsHandleCreated) ShowWindow(_overlay.Handle, SW_HIDE);
    }

    private static bool RectEquals(RECT a, RECT b)
        => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    // DWMWA_EXTENDED_FRAME_BOUNDS: contorno visivel real da janela (sem a borda de
    // redimensionamento invisivel do Win10/11 que o GetWindowRect inclui).
    private static bool TryGetBounds(IntPtr hwnd, out RECT rect)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect,
                Marshal.SizeOf<RECT>()) == 0)
            return true;
        return GetWindowRect(hwnd, out rect);
    }

    public void Dispose()
    {
        try { _timer.Dispose(); } catch { }
        try { _overlay.Region?.Dispose(); _overlay.Dispose(); } catch { }
    }

    /// <summary>Form invisivel ao alt-tab, click-through e que nunca ganha foco.</summary>
    private sealed class OverlayForm : Form
    {
        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }

    // ---- Win32 ----
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute,
        out RECT pvAttribute, int cbAttribute);
}
