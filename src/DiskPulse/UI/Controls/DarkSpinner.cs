using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Kontrol indikator pemuatan (loading spinner) modern dengan animasi putar anti-alias berkecepatan tinggi.
/// Otomatis mematikan timer saat kontrol tidak terlihat untuk menghemat 100% penggunaan CPU.
/// </summary>
public class DarkSpinner : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private float _currentAngle = 0f;
    private bool _isRunning = false;

    public Color SpinnerColor { get; set; } = Color.FromArgb(0, 242, 254);
    public Color TrackColor { get; set; } = Color.FromArgb(28, 38, 54);
    public float ArcSweepAngle { get; set; } = 110f;
    public float LineWidth { get; set; } = 3.2f;

    public DarkSpinner()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Size = new Size(50, 50);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 20 // ~50 FPS untuk animasi putar ultra mulus
        };
        _timer.Tick += (_, _) =>
        {
            _currentAngle = (_currentAngle + 8.5f) % 360f;
            Invalidate();
        };
    }

    public void Start()
    {
        _isRunning = true;
        _timer.Start();
        Invalidate();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Stop();
        Invalidate();
    }


    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && _isRunning)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_isRunning && Visible)
        {
            _timer.Start();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float pad = LineWidth + 1f;
        float w = Width - pad * 2f;
        float h = Height - pad * 2f;

        if (w <= 4 || h <= 4) return;

        var rect = new RectangleF(pad, pad, w, h);

        // 1. Lingkaran Jalur Dasar (Background Track)
        using (var trackPen = new Pen(TrackColor, LineWidth))
        {
            g.DrawEllipse(trackPen, rect);
        }

        // 2. Busur Putar Menyala (Glowing Sweeping Arc)
        if (_isRunning)
        {
            using var arcPen = new Pen(SpinnerColor, LineWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawArc(arcPen, rect, _currentAngle, ArcSweepAngle);

            // Cahaya tengah halus (Core Glow Accent)
            float coreSize = Math.Max(4f, w * 0.16f);
            var coreRect = new RectangleF((Width - coreSize) / 2f, (Height - coreSize) / 2f, coreSize, coreSize);
            using var coreBrush = new SolidBrush(Color.FromArgb(50, SpinnerColor));
            g.FillEllipse(coreBrush, coreRect);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
