using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DiskPulse.Models;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Bilah visualisasi kapasitas disk yang sangat terpoles (polished texture),
/// dengan segment tick markers, pill badge persentase, dan tata letak anti-clipping.
/// </summary>
public class ModernDiskBar : Control
{
    private DiskUsageInfo? _usageInfo;

    public ModernDiskBar()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        Height = 100;
        BackColor = Color.Transparent;
    }

    public void SetDiskUsage(DiskUsageInfo? info)
    {
        _usageInfo = info;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (_usageInfo == null)
        {
            using var brush = new SolidBrush(Theme.TextMuted);
            g.DrawString("Memuat data Drive C...", Theme.BodyFont, brush, new PointF(12, 20));
            return;
        }

        int width = ClientSize.Width;
        int padX = 14;
        int barY = 40;
        int barHeight = 18;
        int barWidth = width - (padX * 2);

        // 1. Baris Judul & Metrik Atas
        string driveTitle = $"Drive {_usageInfo.DriveName} ({_usageInfo.VolumeLabel})";
        using (var titleBrush = new SolidBrush(Theme.TextPrimary))
        {
            g.DrawString(driveTitle, Theme.SectionFont, titleBrush, new PointF(padX, 10));
        }

        // Percentage Badge Pill
        string badgeText = $"{_usageInfo.UsedPercentage:F1}% Used";
        var badgeSize = g.MeasureString(badgeText, Theme.SmallFont);
        float badgeW = badgeSize.Width + 14;
        float badgeH = 22;
        float badgeX = width - padX - badgeW;
        float badgeY = 9;

        Color accentColor = Theme.AccentEmerald;
        Color badgeBg = Color.FromArgb(25, 45, 35);
        if (_usageInfo.UsedPercentage >= 90.0)
        {
            accentColor = Theme.AccentRose;
            badgeBg = Color.FromArgb(50, 25, 25);
        }
        else if (_usageInfo.UsedPercentage >= 75.0)
        {
            accentColor = Theme.AccentAmber;
            badgeBg = Color.FromArgb(45, 38, 20);
        }

        var badgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);
        using (var badgePath = Theme.CreateRoundedRectangle(badgeRect, 11))
        using (var badgeFill = new SolidBrush(badgeBg))
        using (var badgePen = new Pen(accentColor, 1f))
        using (var badgeTextBrush = new SolidBrush(accentColor))
        {
            g.FillPath(badgeFill, badgePath);
            g.DrawPath(badgePen, badgePath);
            g.DrawString(badgeText, Theme.SmallFont, badgeTextBrush, new PointF(badgeX + 7, badgeY + 4));
        }

        // Space stats to the left of badge
        string spaceStats = $"{_usageInfo.FormattedFree} free of {_usageInfo.FormattedTotal}";
        var statsSize = g.MeasureString(spaceStats, Theme.SubtitleFont);
        using (var statBrush = new SolidBrush(Theme.TextSecondary))
        {
            g.DrawString(spaceStats, Theme.SubtitleFont, statBrush, new PointF(badgeX - statsSize.Width - 12, 12));
        }

        // 2. Background Track Bar (Deep slate)
        var barRect = new RectangleF(padX, barY, barWidth, barHeight);
        using (var trackPath = Theme.CreateRoundedRectangle(barRect, 6))
        using (var trackBrush = new SolidBrush(Theme.TrackBg))
        using (var trackPen = new Pen(Theme.CardBorder, 1f))
        {
            g.FillPath(trackBrush, trackPath);
            g.DrawPath(trackPen, trackPath);
        }

        // 3. Filled Progress Bar
        double ratio = Math.Clamp(_usageInfo.UsedPercentage / 100.0, 0.02, 1.0);
        float fillWidth = (float)(barWidth * ratio);
        var fillRect = new RectangleF(padX, barY, fillWidth, barHeight);

        using (var fillPath = Theme.CreateRoundedRectangle(fillRect, 6))
        using (var fillBrush = new SolidBrush(accentColor))
        {
            g.FillPath(fillBrush, fillPath);
        }

        // Subtle segment ticks (25%, 50%, 75%)
        using (var tickPen = new Pen(Color.FromArgb(80, 0, 0, 0), 1.5f))
        {
            float[] ticks = [0.25f, 0.50f, 0.75f];
            foreach (var t in ticks)
            {
                float tx = padX + (barWidth * t);
                g.DrawLine(tickPen, tx, barY + 2, tx, barY + barHeight - 2);
            }
        }

        // 4. Bottom status text
        string subStatus = _usageInfo.UsedPercentage >= 90.0
            ? "Warning: Storage capacity is critical. Clean up junk files to maintain system performance."
            : $"Storage is in optimal condition. {_usageInfo.FormattedUsed} used of {_usageInfo.FormattedTotal} total.";

        Color subColor = _usageInfo.UsedPercentage >= 90.0 ? Theme.AccentRose : Theme.TextMuted;
        using (var subBrush = new SolidBrush(subColor))
        {
            g.DrawString(subStatus, Theme.SmallFont, subBrush, new PointF(padX, barY + barHeight + 8));
        }
    }
}
