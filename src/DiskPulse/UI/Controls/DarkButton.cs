using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DiskPulse.UI.Controls;

public enum ButtonVariant
{
    Primary,
    Success,
    Danger,
    Secondary,
    Ghost,
    ElevatedNav,
    GradientCyan,
    RefreshHeader
}

public enum NavIconType
{
    None,
    TrashCleaner,
    FolderAnalyzer,
    AppGrid,
    UpdateSpark
}

/// <summary>
/// Tombol datar modern dengan rendering presisi tinggi (Apple Pro / Linear luxury aesthetic).
/// </summary>
public class DarkButton : Button
{
    private bool _isHovered;
    private bool _isPressed;
    private ButtonVariant _variant = ButtonVariant.Secondary;
    private NavIconType _navIcon = NavIconType.None;

    public ButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    public NavIconType NavIcon
    {
        get => _navIcon;
        set { _navIcon = value; Invalidate(); }
    }

    public DarkButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        SetStyle(ControlStyles.Selectable, false);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.BodyBold;
        ForeColor = Theme.TextPrimary;
        Cursor = Cursors.Hand;
        Size = new Size(130, 36);
        BackColor = Theme.ControlBg;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        Color parentColor = GetEffectiveParentColor();
        using var brush = new SolidBrush(parentColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        Color parentColor = GetEffectiveParentColor();
        g.Clear(parentColor);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        float radius = _variant is ButtonVariant.ElevatedNav or ButtonVariant.RefreshHeader ? 8f : 6f;
        var rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
        using var path = Theme.CreateRoundedRectangle(rect, radius);

        Color border = GetBorderColor();
        Color textCol = Enabled ? GetTextColor() : Theme.TextMuted;

        if (_variant == ButtonVariant.GradientCyan && Enabled)
        {
            Color gradStart = _isPressed ? Color.FromArgb(2, 132, 199) : _isHovered ? Color.FromArgb(8, 145, 178) : Color.FromArgb(6, 182, 212);
            Color gradEnd = _isPressed ? Color.FromArgb(29, 78, 216) : _isHovered ? Color.FromArgb(37, 99, 235) : Color.FromArgb(59, 130, 246);
            using var lgb = new LinearGradientBrush(rect, gradStart, gradEnd, LinearGradientMode.Horizontal);
            g.FillPath(lgb, path);
        }
        else
        {
            Color bg = GetBgColor();
            using var brush = new SolidBrush(bg);
            g.FillPath(brush, path);
        }

        if (border != Color.Transparent)
        {
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
        }

        // Render Ikon & Teks Khusus untuk RefreshHeader
        if (_variant == ButtonVariant.RefreshHeader)
        {
            // Gambar Ikon Panah Melingkar Vektor (Anti-Emoji!)
            float iconX = 14f;
            float iconY = ClientSize.Height / 2f;
            float iconR = 5.5f;
            Color iconCol = _isHovered ? Color.White : Color.FromArgb(160, 180, 205);
            using (var arcPen = new Pen(iconCol, 1.6f))
            {
                g.DrawArc(arcPen, iconX - iconR, iconY - iconR, iconR * 2f, iconR * 2f, 45, 270);
                // Arrowhead
                PointF tip = new(iconX + iconR, iconY - 1f);
                PointF[] arrowPts = [
                    new PointF(tip.X - 3.5f, tip.Y - 2.5f),
                    tip,
                    new PointF(tip.X + 2.5f, tip.Y - 2.5f)
                ];
                g.DrawLines(arcPen, arrowPts);
            }

            var textRect = new Rectangle(28, 0, ClientSize.Width - 32, ClientSize.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textCol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        // Render Ikon Vektor Sidebar Navigasi jika NavIcon aktif
        if (_navIcon != NavIconType.None)
        {
            float iconCenterY = ClientSize.Height / 2f;
            Color iconColor = (_variant == ButtonVariant.ElevatedNav || _isHovered) ? Theme.AccentCyan : Color.FromArgb(148, 163, 184);

            switch (_navIcon)
            {
                case NavIconType.AppGrid:
                {
                    // 4 kotak sudut bulat cyan (Grid 2x2 identik dengan Mockup Gambar 1)
                    float startX = 13f;
                    float startY = iconCenterY - 7f;
                    float sqSize = 5.5f;
                    float gap = 3f;
                    using var pen = new Pen(iconColor, 1.5f);

                    using var p1 = Theme.CreateRoundedRectangle(new RectangleF(startX, startY, sqSize, sqSize), 1.5f);
                    g.DrawPath(pen, p1);

                    using var p2 = Theme.CreateRoundedRectangle(new RectangleF(startX + sqSize + gap, startY, sqSize, sqSize), 1.5f);
                    g.DrawPath(pen, p2);

                    using var p3 = Theme.CreateRoundedRectangle(new RectangleF(startX, startY + sqSize + gap, sqSize, sqSize), 1.5f);
                    g.DrawPath(pen, p3);

                    using var p4 = Theme.CreateRoundedRectangle(new RectangleF(startX + sqSize + gap, startY + sqSize + gap, sqSize, sqSize), 1.5f);
                    g.DrawPath(pen, p4);
                    break;
                }
                case NavIconType.FolderAnalyzer:
                {
                    // Ikon Vektor Folder
                    float startX = 12f;
                    float startY = iconCenterY - 7f;
                    using var pen = new Pen(iconColor, 1.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                    PointF[] folderOutline =
                    [
                        new PointF(startX + 1f, startY + 2f),
                        new PointF(startX + 5.5f, startY + 2f),
                        new PointF(startX + 7.5f, startY + 4f),
                        new PointF(startX + 14f, startY + 4f),
                        new PointF(startX + 14f, startY + 12f),
                        new PointF(startX + 1f, startY + 12f),
                        new PointF(startX + 1f, startY + 2f)
                    ];
                    g.DrawLines(pen, folderOutline);
                    g.DrawLine(pen, startX + 1f, startY + 5.5f, startX + 14f, startY + 5.5f);
                    break;
                }
                case NavIconType.TrashCleaner:
                {
                    // Ikon Vektor Tempat Sampah
                    float startX = 13f;
                    float startY = iconCenterY - 7f;
                    using var pen = new Pen(iconColor, 1.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawLine(pen, startX + 1f, startY + 3f, startX + 13f, startY + 3f);
                    g.DrawLine(pen, startX + 5f, startY + 1.5f, startX + 9f, startY + 1.5f);
                    PointF[] body =
                    [
                        new PointF(startX + 2.5f, startY + 3.5f),
                        new PointF(startX + 3.5f, startY + 13f),
                        new PointF(startX + 10.5f, startY + 13f),
                        new PointF(startX + 11.5f, startY + 3.5f)
                    ];
                    g.DrawLines(pen, body);
                    g.DrawLine(pen, startX + 5.5f, startY + 5.5f, startX + 5.5f, startY + 11f);
                    g.DrawLine(pen, startX + 8.5f, startY + 5.5f, startX + 8.5f, startY + 11f);
                    break;
                }
                case NavIconType.UpdateSpark:
                {
                    // Ikon Vektor Pembaruan Aplikasi (Panah Sirkular Melingkar Presisi)
                    float cx = 20f;
                    float cy = iconCenterY;
                    float r = 5.5f;
                    using var pen = new Pen(iconColor, 1.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawArc(pen, cx - r, cy - r, r * 2f, r * 2f, 40f, 275f);
                    PointF tip = new(cx + r, cy - 0.5f);
                    PointF[] arrowPts = [
                        new PointF(tip.X - 3.2f, tip.Y - 2.5f),
                        tip,
                        new PointF(tip.X + 2.2f, tip.Y - 2.5f)
                    ];
                    g.DrawLines(pen, arrowPts);
                    break;
                }
            }
        }

        // Render Teks Normal
        int padL = Padding.Left > 0 ? Padding.Left : 4;
        int padR = Padding.Right > 0 ? Padding.Right : 4;
        var normalTextRect = new Rectangle(padL, 0, Math.Max(10, ClientSize.Width - padL - padR), ClientSize.Height);
        var horizFlag = TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft 
            ? TextFormatFlags.Left 
            : TextFormatFlags.HorizontalCenter;
        var flags = horizFlag | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, Text, Font, normalTextRect, textCol, flags);
    }

    private Color GetEffectiveParentColor()
    {
        Control? current = Parent;
        while (current != null)
        {
            if (current.BackColor != Color.Transparent && current.BackColor.A == 255)
            {
                return current.BackColor;
            }
            current = current.Parent;
        }

        return Theme.Background;
    }

    private Color GetBgColor()
    {
        if (!Enabled) return Color.FromArgb(28, 28, 34);

        return _variant switch
        {
            ButtonVariant.Primary => _isPressed ? Color.FromArgb(29, 78, 216) : _isHovered ? Theme.AccentBlueHover : Theme.AccentBlue,
            ButtonVariant.Success => _isPressed ? Color.FromArgb(4, 120, 87) : _isHovered ? Theme.AccentEmeraldHover : Theme.AccentEmerald,
            ButtonVariant.Danger => _isPressed ? Color.FromArgb(185, 28, 28) : _isHovered ? Theme.AccentRoseHover : Theme.AccentRose,
            ButtonVariant.Ghost => _isPressed ? Color.FromArgb(35, 35, 45) : _isHovered ? Color.FromArgb(28, 32, 45) : Color.Transparent,
            ButtonVariant.ElevatedNav => _isPressed ? Color.FromArgb(18, 22, 34) : _isHovered ? Color.FromArgb(28, 34, 50) : Color.FromArgb(22, 26, 38),
            ButtonVariant.RefreshHeader => _isPressed ? Color.FromArgb(15, 18, 28) : _isHovered ? Color.FromArgb(26, 32, 48) : Color.FromArgb(17, 20, 30),
            _ => _isPressed ? Color.FromArgb(38, 44, 60) : _isHovered ? Theme.ControlBgHover : Theme.ControlBg
        };
    }

    private Color GetBorderColor()
    {
        if (!Enabled) return Color.FromArgb(40, 40, 48);

        return _variant switch
        {
            ButtonVariant.Primary => _isHovered ? Color.FromArgb(96, 165, 250) : Color.Transparent,
            ButtonVariant.Success => _isHovered ? Color.FromArgb(52, 211, 153) : Color.Transparent,
            ButtonVariant.Danger => _isHovered ? Color.FromArgb(248, 113, 113) : Color.Transparent,
            ButtonVariant.Ghost => _isHovered ? Color.FromArgb(50, 60, 80) : Color.Transparent,
            ButtonVariant.ElevatedNav => _isHovered ? Color.FromArgb(6, 182, 212) : Color.FromArgb(45, 55, 78),
            ButtonVariant.RefreshHeader => _isHovered ? Color.FromArgb(6, 182, 212) : Color.FromArgb(40, 48, 68),
            _ => _isHovered ? Color.FromArgb(70, 80, 105) : Theme.CardBorder
        };
    }

    private Color GetTextColor()
    {
        return _variant switch
        {
            ButtonVariant.Ghost => _isHovered ? Color.White : Theme.TextSecondary,
            ButtonVariant.Secondary => _isHovered ? Color.White : Theme.TextSecondary,
            ButtonVariant.RefreshHeader => _isHovered ? Color.White : Color.FromArgb(210, 225, 240),
            ButtonVariant.ElevatedNav => Color.White,
            _ => Color.White
        };
    }
}
