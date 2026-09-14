using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Dropdown ComboBox tema gelap kustom untuk memilih drive aktif (C:, D:, E:, dll.).
/// </summary>
public class DarkComboBox : ComboBox
{
    public DarkComboBox()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 28;
        BackColor = Theme.CardBackground;
        ForeColor = Theme.TextPrimary;
        Font = Theme.BodyBold;
        Cursor = Cursors.Hand;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color bg = isSelected ? Color.FromArgb(37, 99, 235) : Theme.CardBackground;
        Color textCol = isSelected ? Color.White : Theme.TextPrimary;

        using (var brush = new SolidBrush(bg))
        {
            g.FillRectangle(brush, e.Bounds);
        }

        string text = Items[e.Index]?.ToString() ?? "";
        var textRect = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top, e.Bounds.Width - 20, e.Bounds.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, text, Font, textRect, textCol, flags);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // Bersihkan background
        Color parentBg = Parent?.BackColor ?? Theme.Background;
        g.Clear(parentBg);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
        using var path = Theme.CreateRoundedRectangle(rect, 6f);
        using var fillBrush = new SolidBrush(Theme.ControlBg);
        using var borderPen = new Pen(Theme.CardBorder, 1.2f);

        g.FillPath(fillBrush, path);
        g.DrawPath(borderPen, path);

        // Render teks item terpilih
        string text = SelectedItem?.ToString() ?? Text;
        var textRect = new Rectangle(12, 0, ClientSize.Width - 36, ClientSize.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, text, Font, textRect, Theme.TextPrimary, flags);

        // Render panah Chevron ▼ di kanan
        int arrowX = ClientSize.Width - 22;
        int arrowY = ClientSize.Height / 2 - 2;
        using var arrowPen = new Pen(Theme.TextSecondary, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(arrowPen,
        [
            new PointF(arrowX, arrowY),
            new PointF(arrowX + 4f, arrowY + 4f),
            new PointF(arrowX + 8f, arrowY)
        ]);
    }
}
