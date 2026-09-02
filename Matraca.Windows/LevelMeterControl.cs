using System.Drawing.Drawing2D;

namespace Matraca;

/// <summary>
/// Barra de nivel do microfone com a marca do limiar. Verde = o VAD consideraria isto fala;
/// cinza = silencio. E' a referencia que faltava: o numero sozinho nao diz nada.
/// </summary>
internal sealed class LevelMeterControl : Control
{
    /// <summary>Topo da escala. RMS de fala normal fica entre ~0,02 e ~0,15.</summary>
    private const float FullScale = 0.30f;

    private float _level;
    private float _peak;
    private float _threshold = 0.012f;

    public LevelMeterControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 26;
        Width = 320;
    }

    /// <summary>Nivel instantaneo e pico recente, ambos em RMS.</summary>
    public void SetLevel(float level, float peak)
    {
        if (Math.Abs(level - _level) < 0.0002f && Math.Abs(peak - _peak) < 0.0002f) return;
        _level = level;
        _peak = peak;
        Invalidate();
    }

    public float Threshold
    {
        get => _threshold;
        set { if (Math.Abs(value - _threshold) < 0.0001f) return; _threshold = value; Invalidate(); }
    }

    /// <summary>Nao ha' captura no ar (dispositivo ocupado ou medidor parado).</summary>
    public bool Offline { get; set; }

    /// <summary>
    /// Escala com raiz quadrada: em escala linear tudo o que interessa (0,005-0,05) ficaria
    /// espremido no canto esquerdo e a barra nao ajudaria a escolher nada.
    /// </summary>
    public static float Frac(float rms)
        => (float)Math.Sqrt(Math.Clamp(rms, 0f, FullScale) / FullScale);

    /// <summary>Inversa de <see cref="Frac"/> - usada pelo slider do limiar.</summary>
    public static float Rms(float frac)
    {
        frac = Math.Clamp(frac, 0f, 1f);
        return frac * frac * FullScale;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(0, 6, Math.Max(1, Width - 1), Math.Max(1, Height - 12));
        int radius = track.Height;

        using (var path = RoundedRect(track, radius))
        {
            using var back = new SolidBrush(Color.FromArgb(232, 232, 236));
            g.FillPath(back, path);

            if (!Offline)
            {
                int fill = (int)(track.Width * Frac(_level));
                if (fill > 2)
                {
                    var bar = new Rectangle(track.X, track.Y, fill, track.Height);
                    bool speech = _level > _threshold;
                    using var brush = new LinearGradientBrush(
                        bar,
                        speech ? Color.FromArgb(0x3F, 0xB9, 0x50) : Color.FromArgb(0x9A, 0xA6, 0xB2),
                        speech ? Color.FromArgb(0x2A, 0x8C, 0x3B) : Color.FromArgb(0x7C, 0x88, 0x94),
                        LinearGradientMode.Horizontal);
                    var clip = g.Clip;
                    g.SetClip(path);
                    g.FillRectangle(brush, bar);
                    g.Clip = clip;
                }

                int peakX = track.X + (int)(track.Width * Frac(_peak));
                if (peakX > track.X + 2)
                {
                    using var peakPen = new Pen(Color.FromArgb(120, 0, 0, 0), 1.5f);
                    g.DrawLine(peakPen, peakX, track.Y + 2, peakX, track.Bottom - 2);
                }
            }

            using var edge = new Pen(Color.FromArgb(200, 200, 206));
            g.DrawPath(edge, path);
        }

        int tx = track.X + (int)(track.Width * Frac(_threshold));
        tx = Math.Clamp(tx, track.X, track.Right - 1);
        using (var pen = new Pen(Color.FromArgb(0xC5, 0x0F, 0x1F), 2f))
            g.DrawLine(pen, tx, track.Y - 3, tx, track.Bottom + 3);
        using (var brush = new SolidBrush(Color.FromArgb(0xC5, 0x0F, 0x1F)))
            g.FillPolygon(brush, new[]
            {
                new Point(tx - 4, 0), new Point(tx + 4, 0), new Point(tx, 5),
            });

        if (Offline)
        {
            using var brush = new SolidBrush(SystemColors.GrayText);
            using var font = new Font(Font.FontFamily, 7.5f);
            g.DrawString("sem captura", font, brush, track.X + 6, track.Y);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = Math.Max(2, Math.Min(radius, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 90, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 270, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 0, 90);
        path.CloseFigure();
        return path;
    }
}
