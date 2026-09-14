using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DiskPulse.Models;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Kontrol badge ringkasan total kapasitas dan jumlah berkas
/// dengan tipografi multi-warna persis seperti prototype (Total: dalam muted grey, angka GB dalam putih tebal, item dalam muted grey).
/// </summary>
public class FolderSummaryBadge : Control
{
    private string _bytesText = "0 B";
    private int _itemCount = 0;

    private static readonly Font _fontRegular = new("Segoe UI", 9f, FontStyle.Regular);
    private static readonly Font _fontBold = new("Segoe UI", 9.5f, FontStyle.Bold);

    public FolderSummaryBadge()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Size = new Size(240, 24);
    }

    public void UpdateSummary(long totalBytes, int itemCount)
    {
        _bytesText = DiskUsageInfo.FormatBytes(totalBytes);
        _itemCount = itemCount;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        string prefix = "Total: ";
        string suffix = _itemCount == 1 ? $" ({_itemCount:N0} Item)" : $" ({_itemCount:N0} Items)";

        using var brushMuted = new SolidBrush(Color.FromArgb(148, 163, 184));
        using var brushWhite = new SolidBrush(Color.FromArgb(248, 250, 252));

        var szPrefix = g.MeasureString(prefix, _fontRegular);
        var szBytes = g.MeasureString(_bytesText, _fontBold);
        var szSuffix = g.MeasureString(suffix, _fontRegular);

        float totalW = szPrefix.Width + szBytes.Width + szSuffix.Width;
        float startX = Math.Max(0, Width - totalW);
        float y = (Height - szPrefix.Height) / 2f;

        g.DrawString(prefix, _fontRegular, brushMuted, startX, y);
        float xBytes = startX + szPrefix.Width;
        g.DrawString(_bytesText, _fontBold, brushWhite, xBytes, y - 0.5f);
        float xSuffix = xBytes + szBytes.Width;
        g.DrawString(suffix, _fontRegular, brushMuted, xSuffix, y);
    }
}
