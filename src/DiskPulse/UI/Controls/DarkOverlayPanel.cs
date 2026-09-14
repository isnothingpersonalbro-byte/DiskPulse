using System.Drawing.Drawing2D;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Panel overlay khusus untuk memuat status pemindaian dengan double-buffering penuh,
/// animasi garis laser pemindaian aktif berkecepatan tinggi, dan latar belakang gelap yang bersih.
/// </summary>
public class DarkOverlayPanel : Panel
{
    private readonly System.Windows.Forms.Timer _animTimer;
    private float _pulsePosition = 0f;
    private bool _pulseForward = true;
    private bool _isScanning = false;

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            _isScanning = value;
            if (_isScanning)
            {
                _animTimer.Start();
            }
            else
            {
                _animTimer.Stop();
            }
            Invalidate();
        }
    }

    public DarkOverlayPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        DoubleBuffered = true;
        BackColor = Color.FromArgb(10, 15, 26);

        _animTimer = new System.Windows.Forms.Timer
        {
            Interval = 25 // 40 FPS untuk animasi gelombang laser
        };
        _animTimer.Tick += (_, _) =>
        {
            if (_pulseForward)
            {
                _pulsePosition += 0.035f;
                if (_pulsePosition >= 1f)
                {
                    _pulsePosition = 1f;
                    _pulseForward = false;
                }
            }
            else
            {
                _pulsePosition -= 0.035f;
                if (_pulsePosition <= 0f)
                {
                    _pulsePosition = 0f;
                    _pulseForward = true;
                }
            }
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_isScanning)
        {
            // Garis gelombang laser pemindaian horizontal (Active Cyber Scan Line)
            int barWidth = Math.Min(480, Width - 60);
            if (barWidth > 80)
            {
                int barHeight = 4;
                int barX = (Width - barWidth) / 2;
                int barY = Math.Max(20, Height / 2 + 38);

                var trackRect = new RectangleF(barX, barY, barWidth, barHeight);
                using (var trackPath = Theme.CreateRoundedRectangle(trackRect, 2f))
                using (var trackBrush = new SolidBrush(Color.FromArgb(20, 30, 48)))
                {
                    g.FillPath(trackBrush, trackPath);
                }

                // Laser glow beam
                int beamWidth = Math.Max(60, barWidth / 4);
                float beamX = barX + (_pulsePosition * (barWidth - beamWidth));
                var beamRect = new RectangleF(beamX, barY - 1, beamWidth, barHeight + 2);

                using (var beamPath = Theme.CreateRoundedRectangle(beamRect, 3f))
                using (var beamBrush = new LinearGradientBrush(
                    beamRect,
                    Color.FromArgb(0, 0, 242, 254),
                    Color.FromArgb(230, 0, 242, 254),
                    LinearGradientMode.Horizontal))
                {
                    var cb = new ColorBlend(3)
                    {
                        Colors = [Color.FromArgb(30, 0, 242, 254), Color.FromArgb(255, 0, 242, 254), Color.FromArgb(30, 0, 242, 254)],
                        Positions = [0f, 0.5f, 1f]
                    };
                    beamBrush.InterpolationColors = cb;
                    g.FillPath(beamBrush, beamPath);
                }

                // Aura cahaya halus di sekitar laser
                using var auraBrush = new SolidBrush(Color.FromArgb(35, 0, 242, 254));
                g.FillEllipse(auraBrush, beamX + beamWidth / 2f - 24f, barY - 4f, 48f, 12f);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
