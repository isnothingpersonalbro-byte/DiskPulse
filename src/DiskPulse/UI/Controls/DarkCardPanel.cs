using System.Drawing.Drawing2D;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Kontainer panel kartu modern dengan sudut membulat presisi tinggi dan border halus.
/// </summary>
public class DarkCardPanel : Panel
{
    public float CornerRadius { get; set; } = 8f;
    public Color BorderColor { get; set; } = Theme.CardBorder;

    public DarkCardPanel()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Theme.CardBackground;
        Padding = new Padding(14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // Bersihkan area luar dengan warna Parent agar sudut luar membulat menyatu sempurna
        Color parentBg = Parent?.BackColor ?? Theme.Background;
        using (var pBrush = new SolidBrush(parentBg))
        {
            g.FillRectangle(pBrush, ClientRectangle);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
        using var path = Theme.CreateRoundedRectangle(rect, CornerRadius);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(BorderColor, 1f);

        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }
}
