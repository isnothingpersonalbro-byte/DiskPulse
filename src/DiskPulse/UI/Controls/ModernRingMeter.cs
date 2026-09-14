using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DiskPulse.Models;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Kontrol visualisasi kapasitas disk centerpiece kelas mewah (Apple Pro / Linear aesthetic):
/// - Centerpiece Dual-Ring Circular Meter berdiameter besar dengan ambient cyan glow.
/// - Persentase terpakai tebal dan label status di dalam cincin.
/// - Rincian partisi dan bilah rasio segmen di kolom tengah.
/// - Panel metrik terintegrasi di sisi kanan dengan garis pemisah vertikal elegan (bebas tabrakan border).
/// </summary>
public class ModernRingMeter : Control
{
    private DiskUsageInfo? _usageInfo;

    public ModernRingMeter()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        Height = 175;
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

        int w = ClientSize.Width;
        int h = ClientSize.Height;

        if (_usageInfo == null)
        {
            using var placeholderBrush = new SolidBrush(Theme.TextMuted);
            g.DrawString("Loading drive data...", Theme.BodyFont, placeholderBrush, new PointF(24, h / 2f - 10));
            return;
        }

        // =========================================================================
        // 1. Centerpiece Large Dual-Ring Circular Meter (Sisi Kiri)
        // =========================================================================
        float ringDiameter = 136f;
        float ringX = 22f;
        float ringY = (h - ringDiameter) / 2f;
        var outerRingRect = new RectangleF(ringX, ringY, ringDiameter, ringDiameter);

        // Ambient Cyan Glow di belakang ring
        using (var glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(outerRingRect);
            using var pgb = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(45, 6, 182, 212),
                SurroundColors = [Color.FromArgb(0, 7, 8, 12)]
            };
            g.FillPath(pgb, glowPath);
        }

        // Cincin Luar: Background Track
        float outerPenWidth = 7.5f;
        var outerArcRect = new RectangleF(
            outerRingRect.X + outerPenWidth / 2f,
            outerRingRect.Y + outerPenWidth / 2f,
            outerRingRect.Width - outerPenWidth,
            outerRingRect.Height - outerPenWidth);

        using (var outerTrackPen = new Pen(Color.FromArgb(28, 35, 48), outerPenWidth))
        {
            g.DrawArc(outerTrackPen, outerArcRect, 0, 360);
        }

        // Cincin Luar: Progress Arc (Cyan Neon)
        float sweepAngleOuter = Math.Clamp((float)(_usageInfo.UsedPercentage / 100.0 * 360.0), 1f, 359.5f);

        // Soft Outer Glow Arc
        using (var glowArcPen = new Pen(Color.FromArgb(55, 6, 182, 212), outerPenWidth + 5f))
        {
            glowArcPen.StartCap = LineCap.Round;
            glowArcPen.EndCap = LineCap.Round;
            g.DrawArc(glowArcPen, outerArcRect, -90, sweepAngleOuter);
        }

        // Main Luminous Arc
        using (var outerProgPen = new Pen(Theme.AccentCyan, outerPenWidth))
        {
            outerProgPen.StartCap = LineCap.Round;
            outerProgPen.EndCap = LineCap.Round;
            g.DrawArc(outerProgPen, outerArcRect, -90, sweepAngleOuter);
        }

        // Cincin Dalam: Track & Progress (Royal Blue Accent)
        float innerInset = 16f;
        float innerPenWidth = 3.5f;
        var innerArcRect = new RectangleF(
            outerArcRect.X + innerInset,
            outerArcRect.Y + innerInset,
            outerArcRect.Width - (innerInset * 2f),
            outerArcRect.Height - (innerInset * 2f));

        using (var innerTrackPen = new Pen(Color.FromArgb(22, 26, 38), innerPenWidth))
        {
            g.DrawArc(innerTrackPen, innerArcRect, 0, 360);
        }

        float sweepAngleInner = Math.Clamp(sweepAngleOuter * 0.92f, 1f, 359.5f);
        using (var innerProgPen = new Pen(Theme.AccentBlue, innerPenWidth))
        {
            innerProgPen.StartCap = LineCap.Round;
            innerProgPen.EndCap = LineCap.Round;
            g.DrawArc(innerProgPen, innerArcRect, -90, sweepAngleInner);
        }

        // Teks di Tengah Cincin
        string pctText = $"{_usageInfo.UsedPercentage:F1}%";
        using var fontPct = new Font("Segoe UI", 16f, FontStyle.Bold);
        using var brushPct = new SolidBrush(Theme.TextPrimary);
        var sizePct = g.MeasureString(pctText, fontPct);

        float centerX = ringX + (ringDiameter / 2f);
        float centerY = ringY + (ringDiameter / 2f);

        g.DrawString(pctText, fontPct, brushPct, centerX - (sizePct.Width / 2f), centerY - (sizePct.Height / 2f) - 6f);

        string subText = "USED";
        using var fontSub = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var brushSub = new SolidBrush(Theme.AccentCyan);
        var sizeSub = g.MeasureString(subText, fontSub);
        g.DrawString(subText, fontSub, brushSub, centerX - (sizeSub.Width / 2f), centerY + 12f);

        // =========================================================================
        // 2. Kolom Tengah: Info Partisi & Mini Segmen
        // =========================================================================
        float midX = ringX + ringDiameter + 28f;
        float rightPanelWidth = 260f;
        float dividerX = Math.Max(midX + 220f, w - rightPanelWidth);

        // Pill Kategori Atas
        float pillY = ringY + 8f;
        using (var pillDotBrush = new SolidBrush(Theme.AccentCyan))
        {
            g.FillEllipse(pillDotBrush, midX, pillY + 4f, 6f, 6f);
        }

        string drivePillText = $"DISK CAPACITY OVERVIEW (DRIVE {_usageInfo.DriveName})";
        using (var pillFont = new Font("Segoe UI", 8f, FontStyle.Bold))
        using (var pillBrush = new SolidBrush(Theme.AccentCyan))
        {
            g.DrawString(drivePillText, pillFont, pillBrush, midX + 12f, pillY);
        }

        // Judul Utama
        float titleY = pillY + 22f;
        using (var titleFont = new Font("Segoe UI", 12.5f, FontStyle.Bold))
        using (var titleBrush = new SolidBrush(Theme.TextPrimary))
        {
            g.DrawString("Primary Storage Capacity", titleFont, titleBrush, midX, titleY);
        }

        // Subtitle Hardware & Partisi
        float descY = titleY + 26f;
        string volLabel = string.IsNullOrWhiteSpace(_usageInfo.VolumeLabel) ? "Windows OS" : _usageInfo.VolumeLabel;
        string hardwareDesc = $"NVMe SSD • System Partition ({volLabel}) • NTFS";
        using (var descFont = new Font("Segoe UI", 8.5f, FontStyle.Regular))
        using (var descBrush = new SolidBrush(Theme.TextSecondary))
        {
            g.DrawString(hardwareDesc, descFont, descBrush, midX, descY);
        }

        // Mini Segmented Bar
        float barY = descY + 26f;
        float barWidth = Math.Min(280f, dividerX - midX - 20f);
        if (barWidth > 60f)
        {
            float barHeight = 7f;
            var barTrackRect = new RectangleF(midX, barY, barWidth, barHeight);
            using var barTrackPath = Theme.CreateRoundedRectangle(barTrackRect, 3.5f);
            using var barTrackBrush = new SolidBrush(Color.FromArgb(30, 36, 50));
            g.FillPath(barTrackBrush, barTrackPath);

            float usedBarWidth = Math.Clamp((float)(barWidth * (_usageInfo.UsedPercentage / 100.0)), 6f, barWidth);
            var barUsedRect = new RectangleF(midX, barY, usedBarWidth, barHeight);
            using var barUsedPath = Theme.CreateRoundedRectangle(barUsedRect, 3.5f);
            using var barUsedBrush = new SolidBrush(Theme.AccentCyan);
            g.FillPath(barUsedBrush, barUsedPath);

            // Ratio summary text
            string ratioText = $"{_usageInfo.FormattedUsed} used ({_usageInfo.UsedPercentage:F1}%)  —  {_usageInfo.FormattedFree} free";
            using var ratioFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            using var ratioBrush = new SolidBrush(Theme.TextMuted);
            g.DrawString(ratioText, ratioFont, ratioBrush, midX, barY + 12f);
        }

        // =========================================================================
        // 3. Garis Pemisah Vertikal Elegan (Spacious, Tanpa Nested Box Tabrakan)
        // =========================================================================
        using (var divPen = new Pen(Color.FromArgb(35, 42, 58), 1f))
        {
            g.DrawLine(divPen, dividerX, ringY + 4f, dividerX, ringY + ringDiameter - 4f);
        }

        // =========================================================================
        // 4. Panel Metrik Kanan (Langsung di atas kartu, jarak lega, angka tabular)
        // =========================================================================
        float statX = dividerX + 26f;
        float statValX = w - 24f; // Nilai sejajar kanan dengan padding 24px
        float statStartY = ringY + 10f;
        float statRowGap = 26f;

        using var statLabelFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var statValFont = new Font("Segoe UI", 9.25f, FontStyle.Bold);
        using var statLabelBrush = new SolidBrush(Theme.TextSecondary);

        // Baris 1: Total Disk
        float row1Y = statStartY;
        using (var dotBrush = new SolidBrush(Theme.TextMuted))
        {
            g.FillEllipse(dotBrush, statX, row1Y + 5f, 6f, 6f);
        }
        g.DrawString("Total Disk", statLabelFont, statLabelBrush, statX + 14f, row1Y);
        string totalStr = _usageInfo.FormattedTotal;
        var totalSize = g.MeasureString(totalStr, statValFont);
        using (var totalBrush = new SolidBrush(Theme.TextPrimary))
        {
            g.DrawString(totalStr, statValFont, totalBrush, statValX - totalSize.Width, row1Y - 1f);
        }

        // Baris 2: Used Space
        float row2Y = row1Y + statRowGap;
        using (var dotBrush = new SolidBrush(Theme.AccentCyan))
        {
            g.FillEllipse(dotBrush, statX, row2Y + 5f, 6f, 6f);
        }
        g.DrawString("Used Space", statLabelFont, statLabelBrush, statX + 14f, row2Y);
        string usedStr = _usageInfo.FormattedUsed;
        var usedSize = g.MeasureString(usedStr, statValFont);
        using (var usedBrush = new SolidBrush(Theme.AccentCyan))
        {
            g.DrawString(usedStr, statValFont, usedBrush, statValX - usedSize.Width, row2Y - 1f);
        }

        // Baris 3: Free Space
        float row3Y = row2Y + statRowGap;
        using (var dotBrush = new SolidBrush(Theme.AccentEmerald))
        {
            g.FillEllipse(dotBrush, statX, row3Y + 5f, 6f, 6f);
        }
        g.DrawString("Free Space", statLabelFont, statLabelBrush, statX + 14f, row3Y);
        string freeStr = _usageInfo.FormattedFree;
        var freeSize = g.MeasureString(freeStr, statValFont);
        using (var freeBrush = new SolidBrush(Theme.AccentEmerald))
        {
            g.DrawString(freeStr, statValFont, freeBrush, statValX - freeSize.Width, row3Y - 1f);
        }

        // Garis pemisah horizontal tipis sebelum status kesehatan
        float sepLineY = row3Y + 24f;
        using (var subSepPen = new Pen(Color.FromArgb(30, 36, 50), 1f))
        {
            g.DrawLine(subSepPen, statX, sepLineY, statValX, sepLineY);
        }

        // Baris 4: Disk Health
        float row4Y = sepLineY + 8f;
        using (var condLabelBrush = new SolidBrush(Theme.TextMuted))
        using (var condFont = new Font("Segoe UI", 8f, FontStyle.Regular))
        {
            g.DrawString("Disk Health", condFont, condLabelBrush, statX, row4Y);
        }

        bool isHealthy = _usageInfo.UsedPercentage < 90.0;
        string healthStr = isHealthy ? "Optimal" : "Critical";
        Color healthColor = isHealthy ? Theme.AccentEmerald : Theme.AccentRose;

        using (var healthFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
        using (var healthBrush = new SolidBrush(healthColor))
        {
            var healthSize = g.MeasureString(healthStr, healthFont);
            float hValX = statValX - healthSize.Width;
            // Glowing status dot
            using var hDotBrush = new SolidBrush(healthColor);
            g.FillEllipse(hDotBrush, hValX - 10f, row4Y + 5f, 5f, 5f);
            g.DrawString(healthStr, healthFont, healthBrush, hValX, row4Y);
        }
    }
}
