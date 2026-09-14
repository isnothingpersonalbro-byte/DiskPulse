using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Menu konteks klik kanan (ContextMenuStrip) dengan tema Fluent Dark presisi,
/// bebas dari gutter putih dan border default Windows yang terang.
/// </summary>
public class DarkContextMenu : ContextMenuStrip
{
    public DarkContextMenu()
    {
        Renderer = new DarkMenuRenderer();
        BackColor = Theme.CardBackground;
        ForeColor = Theme.TextPrimary;
        Font = Theme.BodyFont;
        ShowImageMargin = false;
        DropShadowEnabled = true;
    }

    private class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable())
        {
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bounds = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);

            if (e.Item.Selected && e.Item.Enabled)
            {
                using var brush = new SolidBrush(Theme.ControlBg);
                using var borderPen = new Pen(Theme.AccentBlue, 1f);
                using var path = CreateRoundedRectangle(bounds, 4);

                g.FillPath(brush, path);
                g.DrawPath(borderPen, path);
            }
            else
            {
                using var bgBrush = new SolidBrush(Theme.CardBackground);
                g.FillRectangle(bgBrush, e.Item.Bounds);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextSecondary;
            e.TextFormat |= TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            int y = e.Item.Height / 2;
            using var pen = new Pen(Theme.CardBorder, 1f);
            g.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            using var pen = new Pen(Theme.CardBorder, 1.2f);
            g.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    private class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.CardBackground;
        public override Color ImageMarginGradientBegin => Theme.CardBackground;
        public override Color ImageMarginGradientMiddle => Theme.CardBackground;
        public override Color ImageMarginGradientEnd => Theme.CardBackground;
        public override Color MenuBorder => Theme.CardBorder;
        public override Color MenuItemBorder => Theme.AccentBlue;
        public override Color MenuItemSelected => Theme.ControlBg;
        public override Color MenuItemSelectedGradientBegin => Theme.ControlBg;
        public override Color MenuItemSelectedGradientEnd => Theme.ControlBg;
        public override Color MenuStripGradientBegin => Theme.CardBackground;
        public override Color MenuStripGradientEnd => Theme.CardBackground;
        public override Color SeparatorDark => Theme.CardBorder;
        public override Color SeparatorLight => Theme.CardBorder;
    }
}
