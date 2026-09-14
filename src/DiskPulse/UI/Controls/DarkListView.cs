using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using DiskPulse.Models;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Kontrol ListView tema gelap dengan Custom Owner-Draw penuh untuk Header, Row, dan Checkbox.
/// Menyertakan subclassing native Header Control untuk sepenuhnya membasmi blok putih
/// di ujung kanan header tabel bawaan Windows.
/// </summary>
public class DarkListView : ListView
{
    private const int LVM_FIRST = 0x1000;
    private const int LVM_GETHEADER = LVM_FIRST + 31;
    private const int LVM_SETEXTENDEDLISTVIEWSTYLE = LVM_FIRST + 54;
    private const int LVS_EX_DOUBLEBUFFER = 0x00010000;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_PAINT = 0x000F;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
    private const int SB_HORZ = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT

    {
        public int Left, Top, Right, Bottom;
    }

    private HeaderSubclass? _headerSubclass;
    private int _hoveredIndex = -1;
    private int _hoveredActionIconIndex = -1;
    private readonly ToolTip _actionToolTip = new()
    {
        InitialDelay = 150,
        ReshowDelay = 80,
        AutoPopDelay = 3500,
        ShowAlways = true
    };
    private string _lastTipText = "";

    public event Action<ListViewItem, int>? RowActionClicked;

    public DarkListView()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.ResizeRedraw,
            true);

        DoubleBuffered = true;
        OwnerDraw = true;
        View = View.Details;
        FullRowSelect = true;
        GridLines = false;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.CardBackground;
        ForeColor = Theme.TextPrimary;
        Font = Theme.BodyFont;

        MouseMove += (_, e) =>
        {
            var hit = HitTest(e.Location);
            int newIndex = hit.Item?.Index ?? -1;
            int newActionIcon = -1;

            if (hit.Item != null && (hit.Item.Tag is FolderSizeItem || hit.Item.Tag is FolderDetailItem) && Columns.Count >= 4)
            {
                var itemRect = hit.Item.Bounds;
                int col3Left = itemRect.Left;
                for (int i = 0; i < 3; i++) col3Left += Columns[i].Width;
                int col3Width = Columns[3].Width;
                var col3Rect = new Rectangle(col3Left, itemRect.Top, col3Width, itemRect.Height);

                if (col3Rect.Contains(e.Location))
                {
                    int iconW = 24, spacing = 8;
                    int totalW = 3 * iconW + 2 * spacing;
                    int startX = col3Rect.Right - totalW - 14;
                    int relX = e.Location.X - startX;
                    int relY = e.Location.Y - (col3Rect.Top + (col3Rect.Height - iconW) / 2);

                    if (relX >= 0 && relX < totalW && relY >= 0 && relY <= iconW)
                    {
                        newActionIcon = relX / (iconW + spacing);
                        if (newActionIcon > 2) newActionIcon = -1;
                    }
                }
            }

            Cursor = newActionIcon != -1 ? Cursors.Hand : Cursors.Default;

            string targetTip = "";
            if (newActionIcon == 0) targetTip = "Open in File Explorer";
            else if (newActionIcon == 1) targetTip = "Inspect Folder Contents (Drill-down)";
            else if (newActionIcon == 2) targetTip = "Move to Recycle Bin";

            if (targetTip != _lastTipText)
            {
                _lastTipText = targetTip;
                if (!string.IsNullOrEmpty(targetTip))
                {
                    _actionToolTip.SetToolTip(this, targetTip);
                }
                else
                {
                    _actionToolTip.RemoveAll();
                }
            }

            if (newIndex != _hoveredIndex || newActionIcon != _hoveredActionIconIndex)
            {
                int oldIndex = _hoveredIndex;
                _hoveredIndex = newIndex;
                _hoveredActionIconIndex = newActionIcon;
                if (oldIndex >= 0 && oldIndex < Items.Count)
                {
                    Invalidate(Items[oldIndex].Bounds);
                }
                if (newIndex >= 0 && newIndex < Items.Count)
                {
                    Invalidate(Items[newIndex].Bounds);
                }
            }
        };

        MouseLeave += (_, _) =>
        {
            Cursor = Cursors.Default;
            _lastTipText = "";
            _actionToolTip.RemoveAll();

            if (_hoveredIndex != -1 || _hoveredActionIconIndex != -1)
            {
                int oldIndex = _hoveredIndex;
                _hoveredIndex = -1;
                _hoveredActionIconIndex = -1;
                if (oldIndex >= 0 && oldIndex < Items.Count)
                {
                    Invalidate(Items[oldIndex].Bounds);
                }
            }
        };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var hit = HitTest(e.Location);
        if (hit.Item != null && (hit.Item.Tag is FolderSizeItem || hit.Item.Tag is FolderDetailItem) && Columns.Count >= 4)
        {
            var itemRect = hit.Item.Bounds;
            int col3Left = itemRect.Left;
            for (int i = 0; i < 3; i++) col3Left += Columns[i].Width;
            int col3Width = Columns[3].Width;
            var col3Rect = new Rectangle(col3Left, itemRect.Top, col3Width, itemRect.Height);

            if (col3Rect.Contains(e.Location))
            {
                int iconW = 24, spacing = 8;
                int totalW = 3 * iconW + 2 * spacing;
                int startX = col3Rect.Right - totalW - 14;
                int relX = e.Location.X - startX;
                int relY = e.Location.Y - (col3Rect.Top + (col3Rect.Height - iconW) / 2);

                if (relX >= 0 && relX < totalW && relY >= 0 && relY <= iconW)
                {
                    int slot = relX / (iconW + spacing);
                    if (slot >= 0 && slot <= 2)
                    {
                        RowActionClicked?.Invoke(hit.Item, slot);
                    }
                }
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SendMessage(Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)LVS_EX_DOUBLEBUFFER, (IntPtr)LVS_EX_DOUBLEBUFFER);
        try
        {
            Core.NativeMethods.AllowDarkModeForWindow(Handle, true);
            Core.NativeMethods.SetWindowTheme(Handle, "DarkMode_Explorer", null);
        }
        catch { }
        AttachHeaderSubclass();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT)
        {
            try
            {
                ShowScrollBar(Handle, SB_HORZ, false);
            }
            catch { }
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _headerSubclass?.ReleaseHandle();
        _headerSubclass = null;
        base.OnHandleDestroyed(e);
    }

    private void AttachHeaderSubclass()
    {
        IntPtr headerHwnd = SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (headerHwnd != IntPtr.Zero)
        {
            try
            {
                Core.NativeMethods.AllowDarkModeForWindow(headerHwnd, true);
                Core.NativeMethods.SetWindowTheme(headerHwnd, "DarkMode_ItemsView", null);
            }
            catch { }
            _headerSubclass = new HeaderSubclass(headerHwnd, this);
        }
    }

    public int FlexibleColumnIndex { get; set; } = 1;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AutoFitFlexibleColumn();
    }

    /// <summary>
    /// Menyesuaikan lebar kolom fleksibel (misalnya Kategori atau Path) agar mengisi penuh lebar tabel
    /// sehingga kolom Ukuran dan Jumlah di sisi kanan selalu tampil rapi tanpa memicu scrollbar horizontal.
    /// </summary>
    public void AutoFitFlexibleColumn()
    {
        if (Columns.Count <= FlexibleColumnIndex) return;

        int fixedWidths = 0;
        for (int i = 0; i < Columns.Count; i++)
        {
            if (i != FlexibleColumnIndex)
            {
                fixedWidths += Columns[i].Width;
            }
        }

        int available = Math.Max(120, ClientSize.Width - fixedWidths - 4);
        Columns[FlexibleColumnIndex].Width = available;

        try
        {
            if (IsHandleCreated) ShowScrollBar(Handle, SB_HORZ, false);
        }
        catch { }
    }


    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var bounds = e.Bounds;

        // Latar Belakang Header Gelap
        using (var bgBrush = new SolidBrush(Theme.HeaderBackground))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        // Garis Pembatas Bawah & Pemisah Kolom
        using (var borderPen = new Pen(Theme.CardBorder, 1f))
        {
            g.DrawLine(borderPen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            g.DrawLine(borderPen, bounds.Right - 1, bounds.Top + 5, bounds.Right - 1, bounds.Bottom - 5);
        }

        // Teks Header
        var textRect = new Rectangle(bounds.Left + 10, bounds.Top, bounds.Width - 16, bounds.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis;

        if (e.Header?.TextAlign == HorizontalAlignment.Right)
        {
            flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis;
        }

        TextRenderer.DrawText(g, e.Header?.Text ?? "", Theme.SmallFont, textRect, Theme.TextSecondary, flags);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (e.Item == null || e.SubItem == null) return;

        var bounds = e.Bounds;
        bool isSelected = e.Item.Selected;
        bool isHovered = e.ItemIndex == _hoveredIndex;

        // 1. Gambar latar belakang baris pada tiap subitem/sel
        Color rowBg = isSelected
            ? Color.FromArgb(36, 52, 75)
            : isHovered
                ? Theme.CardBackgroundHover
                : (e.ItemIndex % 2 == 0 ? Theme.CardBackground : Color.FromArgb(20, 20, 26));

        using (var brush = new SolidBrush(rowBg))
        {
            g.FillRectangle(brush, bounds);
        }

        // Garis pembagi halus bawah antar baris
        using (var linePen = new Pen(Color.FromArgb(32, 32, 40), 1f))
        {
            g.DrawLine(linePen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }

        // 2. Render Checkbox jika di kolom pertama dan CheckBoxes aktif
        if (e.ColumnIndex == 0 && CheckBoxes)
        {
            int cbSize = 16;
            int cbX = bounds.Left + (bounds.Width - cbSize) / 2;
            int cbY = bounds.Top + (bounds.Height - cbSize) / 2;
            var cbRect = new RectangleF(cbX, cbY, cbSize, cbSize);

            using var path = Theme.CreateRoundedRectangle(cbRect, 4f);

            if (e.Item.Checked)
            {
                using var fillBrush = new SolidBrush(Theme.AccentCyan);
                g.FillPath(fillBrush, path);

                // Tanda centang gelap kontras di atas latar cyan (seperti Mockup Gambar 5)
                using var checkPen = new Pen(Color.FromArgb(10, 12, 18), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(checkPen,
                [
                    new PointF(cbX + 3.5f, cbY + 8f),
                    new PointF(cbX + 6.5f, cbY + 12f),
                    new PointF(cbX + 12.5f, cbY + 4.5f)
                ]);
            }
            else
            {
                using var fillBrush = new SolidBrush(Color.FromArgb(20, 24, 34));
                using var borderPen = new Pen(Color.FromArgb(50, 60, 80), 1.2f);
                g.FillPath(fillBrush, path);
                g.DrawPath(borderPen, path);
            }
            return;
        }

        // 3. Khusus Kategori Sampah (Mockup Gambar 5: Icon Badge Berwarna + 2 Baris Teks)
        if (e.Item.Tag is JunkItemCategory cat && e.ColumnIndex == 1)
        {
            int boxSize = 28;
            int boxX = bounds.Left + 8;
            int boxY = bounds.Top + (bounds.Height - boxSize) / 2;
            var iconBoxRect = new RectangleF(boxX, boxY, boxSize, boxSize);

            Color boxBg = Color.FromArgb(20, 35, 48);
            Color iconCol = Theme.AccentCyan;
            string iconText = "📁";

            if (cat.Id.Contains("browser", StringComparison.OrdinalIgnoreCase))
            {
                boxBg = Color.FromArgb(20, 30, 56);
                iconCol = Theme.AccentBlue;
                iconText = "🌐";
            }
            else if (cat.Id.Contains("recycle", StringComparison.OrdinalIgnoreCase))
            {
                boxBg = Color.FromArgb(48, 20, 28);
                iconCol = Theme.AccentRose;
                iconText = "🗑";
            }
            else if (cat.Id.Contains("system", StringComparison.OrdinalIgnoreCase) || cat.Id.Contains("windows", StringComparison.OrdinalIgnoreCase))
            {
                boxBg = Color.FromArgb(36, 22, 52);
                iconCol = Color.FromArgb(192, 132, 252);
                iconText = "⚙";
            }

            using (var boxPath = Theme.CreateRoundedRectangle(iconBoxRect, 6f))
            using (var boxBrush = new SolidBrush(boxBg))
            using (var boxPen = new Pen(Color.FromArgb(70, iconCol), 1f))
            {
                g.FillPath(boxBrush, boxPath);
                g.DrawPath(boxPen, boxPath);
            }

            // Gambar Ikon Kategori
            using (var iconFont = new Font("Segoe UI Emoji", 10f, FontStyle.Regular))
            using (var iconBrush = new SolidBrush(iconCol))
            {
                var iSize = g.MeasureString(iconText, iconFont);
                g.DrawString(iconText, iconFont, iconBrush, boxX + (boxSize - iSize.Width) / 2f, boxY + (boxSize - iSize.Height) / 2f);
            }

            // Gambar Nama Kategori (Baris 1) & Deskripsi/Path (Baris 2) dengan bounding box agar tidak overflow
            int textX = boxX + boxSize + 10;
            int textW = Math.Max(10, bounds.Right - textX - 8);

            var titleRect = new Rectangle(textX, bounds.Top + 6, textW, 18);
            TextRenderer.DrawText(g, cat.Name, Theme.BodyBold, titleRect, Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var descRect = new Rectangle(textX, bounds.Top + 24, textW, 16);
            TextRenderer.DrawText(g, cat.Description, Theme.SmallFont, descRect, Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }

        // 4. Khusus Tampilan Penganalisis Folder & Rincian (Mockup Gambar 1)
        if (e.Item.Tag is FolderSizeItem folderItem)
        {
            DrawFolderSizeRow(g, e, bounds, folderItem, isSelected, isHovered);
            return;
        }

        if (e.Item.Tag is FolderDetailItem detailItem)
        {
            DrawFolderDetailRow(g, e, bounds, detailItem, isSelected, isHovered);
            return;
        }

        // 5. Render Kolom Teks Normal
        Color textColor = isSelected ? Color.White : Theme.TextPrimary;

        // Berikan penekanan warna untuk ukuran berkas (Emerald tebal) dan persentase (% Cyan)
        bool isSizeColumn = (e.ColumnIndex < Columns.Count && Columns[e.ColumnIndex].Text.Contains("Ukuran"));
        bool isPctColumn = (e.ColumnIndex < Columns.Count && Columns[e.ColumnIndex].Text.Contains("% Disk"));
        Font textFont = Theme.BodyFont;

        if (isSizeColumn && e.SubItem.Text != "0 B" && e.SubItem.Text != "-")
        {
            textColor = Theme.AccentEmerald;
            textFont = Theme.BodyBold;
        }
        else if (isPctColumn)
        {
            textColor = Theme.AccentCyan;
            textFont = Theme.BodyBold;
        }

        var textBounds = new Rectangle(bounds.Left + 8, bounds.Top, Math.Max(0, bounds.Width - 16), bounds.Height);
        var formatFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis;

        if (e.ColumnIndex < Columns.Count)
        {
            var colAlign = Columns[e.ColumnIndex].TextAlign;
            if (colAlign == HorizontalAlignment.Right)
            {
                formatFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis;
            }
            else if (colAlign == HorizontalAlignment.Center)
            {
                formatFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis;
            }
        }

        TextRenderer.DrawText(g, e.SubItem.Text ?? "", textFont, textBounds, textColor, formatFlags);
    }

    private static readonly Font _rowTitleFont = new("Segoe UI", 10.5f, FontStyle.Bold);
    private static readonly Font _rowDescFont = new("Segoe UI", 8.5f, FontStyle.Regular);
    private static readonly Font _rowSizeFont = new("Segoe UI", 10.5f, FontStyle.Bold);

    private void DrawFolderSizeRow(Graphics g, DrawListViewSubItemEventArgs e, Rectangle bounds, FolderSizeItem f, bool isSelected, bool isHovered)
    {
        switch (e.ColumnIndex)
        {
            case 0: // DIREKTORI / BERKAS
            {
                int iconX = bounds.Left + 12;
                int iconY = bounds.Top + (bounds.Height - 18) / 2;
                DrawVectorFolderIcon(g, iconX, iconY, Theme.AccentCyan);

                int textX = iconX + 28;
                int textW = Math.Max(10, bounds.Right - textX - 8);

                var titleRect = new Rectangle(textX, bounds.Top + 6, textW, 20);
                TextRenderer.DrawText(g, f.Name, _rowTitleFont, titleRect, Color.FromArgb(248, 250, 252),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                string desc = GetFolderDescription(f.Name, f.Path);
                string subText = $"{f.FileCount:N0} files  •  {desc}";
                var descRect = new Rectangle(textX, bounds.Top + 26, textW, 18);
                TextRenderer.DrawText(g, subText, _rowDescFont, descRect, Color.FromArgb(100, 116, 139),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                break;
            }
            case 1: // KAPASITAS RELATIF
            {
                int trackW = Math.Clamp(bounds.Width - 28, 40, 180);
                int trackH = 5;
                int trackX = bounds.Left + 14;
                int trackY = bounds.Top + (bounds.Height - trackH) / 2;

                var trackRect = new RectangleF(trackX, trackY, trackW, trackH);
                using (var trackPath = Theme.CreateRoundedRectangle(trackRect, 2.5f))
                using (var trackBrush = new SolidBrush(Color.FromArgb(22, 32, 50)))
                {
                    g.FillPath(trackBrush, trackPath);
                }

                double pct = f.RelativePercentage > 0 ? f.RelativePercentage : f.PercentageOfDrive;
                pct = Math.Clamp(pct, 0.0, 100.0);
                int fillW = Math.Max(2, (int)(trackW * (pct / 100.0)));
                var fillRect = new RectangleF(trackX, trackY, fillW, trackH);
                using (var fillPath = Theme.CreateRoundedRectangle(fillRect, 2.5f))
                using (var fillBrush = new LinearGradientBrush(fillRect, Color.FromArgb(0, 242, 254), Color.FromArgb(0, 114, 255), 0f))
                {
                    g.FillPath(fillBrush, fillPath);
                }
                break;
            }
            case 2: // UKURAN
            {
                var textRect = new Rectangle(bounds.Left + 8, bounds.Top, bounds.Width - 16, bounds.Height);
                TextRenderer.DrawText(g, f.FormattedSize, _rowSizeFont, textRect, Color.FromArgb(16, 185, 129),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                break;
            }
            case 3: // TINDAKAN
            {
                DrawRowActionButtons(g, bounds, e.ItemIndex);
                break;
            }
        }
    }

    private void DrawFolderDetailRow(Graphics g, DrawListViewSubItemEventArgs e, Rectangle bounds, FolderDetailItem it, bool isSelected, bool isHovered)
    {
        switch (e.ColumnIndex)
        {
            case 0: // DIREKTORI / BERKAS
            {
                int iconX = bounds.Left + 12;
                int iconY = bounds.Top + (bounds.Height - 18) / 2;
                if (it.IsDirectory)
                {
                    DrawVectorFolderIcon(g, iconX, iconY, Theme.AccentCyan);
                }
                else
                {
                    DrawVectorFileIcon(g, iconX, iconY, Theme.TextSecondary);
                }

                int textX = iconX + 28;
                int textW = Math.Max(10, bounds.Right - textX - 8);

                var titleRect = new Rectangle(textX, bounds.Top + 6, textW, 20);
                TextRenderer.DrawText(g, it.Name, _rowTitleFont, titleRect, Color.FromArgb(248, 250, 252),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                string desc = it.IsDirectory ? GetFolderDescription(it.Name, it.FullPath) : it.ItemType;
                string subText = it.IsDirectory
                    ? $"{it.FileCount:N0} files  •  {desc}"
                    : $"1 file  •  {desc}";
                var descRect = new Rectangle(textX, bounds.Top + 26, textW, 18);
                TextRenderer.DrawText(g, subText, _rowDescFont, descRect, Color.FromArgb(100, 116, 139),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                break;
            }
            case 1: // KAPASITAS RELATIF
            {
                int trackW = Math.Clamp(bounds.Width - 28, 40, 180);
                int trackH = 5;
                int trackX = bounds.Left + 14;
                int trackY = bounds.Top + (bounds.Height - trackH) / 2;

                var trackRect = new RectangleF(trackX, trackY, trackW, trackH);
                using (var trackPath = Theme.CreateRoundedRectangle(trackRect, 2.5f))
                using (var trackBrush = new SolidBrush(Color.FromArgb(22, 32, 50)))
                {
                    g.FillPath(trackBrush, trackPath);
                }

                double pct = Math.Clamp(it.RelativePercentage, 0.0, 100.0);
                int fillW = Math.Max(2, (int)(trackW * (pct / 100.0)));
                var fillRect = new RectangleF(trackX, trackY, fillW, trackH);
                using (var fillPath = Theme.CreateRoundedRectangle(fillRect, 2.5f))
                using (var fillBrush = new LinearGradientBrush(fillRect, Color.FromArgb(0, 242, 254), Color.FromArgb(0, 114, 255), 0f))
                {
                    g.FillPath(fillBrush, fillPath);
                }
                break;
            }
            case 2: // UKURAN
            {
                var textRect = new Rectangle(bounds.Left + 8, bounds.Top, bounds.Width - 16, bounds.Height);
                TextRenderer.DrawText(g, it.FormattedSize, _rowSizeFont, textRect, Color.FromArgb(16, 185, 129),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                break;
            }
            case 3: // TINDAKAN
            {
                DrawRowActionButtons(g, bounds, e.ItemIndex);
                break;
            }
        }
    }

    private static void DrawVectorFolderIcon(Graphics g, int x, int y, Color color)
    {
        using var pen = new Pen(color, 1.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        PointF[] pts =
        [
            new PointF(x, y + 16),
            new PointF(x, y + 3),
            new PointF(x + 7, y + 3),
            new PointF(x + 10, y + 6),
            new PointF(x + 20, y + 6),
            new PointF(x + 20, y + 16)
        ];
        g.DrawPolygon(pen, pts);
    }

    private static void DrawVectorFileIcon(Graphics g, int x, int y, Color color)
    {
        using var pen = new Pen(color, 1.4f) { LineJoin = LineJoin.Round };
        PointF[] doc =
        [
            new PointF(x + 2, y + 1),
            new PointF(x + 12, y + 1),
            new PointF(x + 16, y + 5),
            new PointF(x + 16, y + 16),
            new PointF(x + 2, y + 16)
        ];
        g.DrawPolygon(pen, doc);
        g.DrawLine(pen, x + 12, y + 1, x + 12, y + 5);
        g.DrawLine(pen, x + 12, y + 5, x + 16, y + 5);
    }

    private void DrawRowActionButtons(Graphics g, Rectangle bounds, int itemIndex)
    {
        int iconW = 24;
        int spacing = 8;
        int totalW = 3 * iconW + 2 * spacing;
        int startX = bounds.Right - totalW - 14;
        int y = bounds.Top + (bounds.Height - iconW) / 2;

        bool isRowHovered = (itemIndex == _hoveredIndex);

        // Icon 0: Explorer ↗
        int x0 = startX;
        bool h0 = isRowHovered && _hoveredActionIconIndex == 0;
        DrawActionButtonBox(g, x0, y, iconW, h0, Color.FromArgb(0, 242, 254));
        using (var pen0 = new Pen(h0 ? Color.FromArgb(0, 242, 254) : Color.FromArgb(90, 105, 125), 1.3f))
        {
            g.DrawRectangle(pen0, x0 + 5, y + 7, 9, 9);
            g.DrawLine(pen0, x0 + 8, y + 11, x0 + 16, y + 3);
            g.DrawLine(pen0, x0 + 12, y + 3, x0 + 16, y + 3);
            g.DrawLine(pen0, x0 + 16, y + 3, x0 + 16, y + 7);
        }

        // Icon 1: Lightning ⚡ (Inspect / Drill-down Rincian Isi Folder - Mockup Gambar 2)
        int x1 = startX + iconW + spacing;
        bool h1 = isRowHovered && _hoveredActionIconIndex == 1;
        DrawActionButtonBox(g, x1, y, iconW, h1, Color.FromArgb(245, 158, 11));
        using (var brush1 = new SolidBrush(h1 ? Color.FromArgb(245, 158, 11) : Color.FromArgb(90, 105, 125)))
        {
            PointF[] bolt =
            [
                new PointF(x1 + 13f, y + 4f),
                new PointF(x1 + 7.5f, y + 11.5f),
                new PointF(x1 + 11.5f, y + 11.5f),
                new PointF(x1 + 9.5f, y + 19.5f),
                new PointF(x1 + 16.5f, y + 9.5f),
                new PointF(x1 + 12.5f, y + 9.5f)
            ];
            g.FillPolygon(brush1, bolt);
        }

        // Icon 2: Trash 🗑 (Delete - Mockup Gambar 2)
        int x2 = startX + 2 * (iconW + spacing);
        bool h2 = isRowHovered && _hoveredActionIconIndex == 2;
        DrawActionButtonBox(g, x2, y, iconW, h2, Color.FromArgb(225, 29, 72));
        using (var pen2 = new Pen(h2 ? Color.FromArgb(225, 29, 72) : Color.FromArgb(90, 105, 125), 1.3f))
        {
            g.DrawLine(pen2, x2 + 5, y + 6, x2 + 17, y + 6);
            g.DrawLine(pen2, x2 + 9, y + 4, x2 + 13, y + 4);
            g.DrawLine(pen2, x2 + 6, y + 6, x2 + 7, y + 18);
            g.DrawLine(pen2, x2 + 7, y + 18, x2 + 15, y + 18);
            g.DrawLine(pen2, x2 + 15, y + 18, x2 + 16, y + 6);
            g.DrawLine(pen2, x2 + 9, y + 9, x2 + 9, y + 15);
            g.DrawLine(pen2, x2 + 13, y + 9, x2 + 13, y + 15);
        }
    }

    public static string GetFolderDescription(string folderName, string fullPath)
    {
        string name = (folderName ?? "").ToLowerInvariant().Trim();
        return name switch
        {
            "docker" => "WSL2 virtual disk",
            "packages" => "UWP / Store data",
            "google" => "Chrome user data",
            "steam" => "Steam games & cache",
            "temp" => "System temporary files",
            "programs" => "Locally installed programs",
            "microsoft" => "System & Office components",
            "windows.old" => "Previous Windows installation",
            "windowsapps" => "Microsoft Store applications",
            "npm-cache" or "npm" => "Node.js package cache",
            "pip" => "Python package cache",
            ".gradle" or "gradle" => "Android & Gradle cache",
            "openai" or "hermes" => "AI tool cache & models",
            "desktop" => "User Desktop files",
            "documents" => "Personal user documents",
            "downloads" => "User downloaded files",
            "appdata" => "User application data",
            "local" => "Local AppData cache",
            "roaming" => "Roaming configurations",
            "locallow" => "Low integrity cache",
            "programdata" => "Shared system program data",
            "program files" => "Installed 64-bit applications",
            "program files (x86)" => "Installed 32-bit applications",
            _ => (string.IsNullOrWhiteSpace(fullPath) || fullPath.Length > 35 ? "Directory Folder" : fullPath)
        };
    }

    private static void DrawActionButtonBox(Graphics g, int x, int y, int size, bool isHovered, Color hoverBorder)
    {
        var rect = new RectangleF(x, y, size, size);
        using var path = Theme.CreateRoundedRectangle(rect, 5f);
        if (isHovered)
        {
            using var bg = new SolidBrush(Color.FromArgb(28, 40, 58));
            using var pen = new Pen(Color.FromArgb(140, hoverBorder), 1.2f);
            g.FillPath(bg, path);
            g.DrawPath(pen, path);
        }
        else
        {
            using var bg = new SolidBrush(Color.FromArgb(16, 22, 32));
            using var pen = new Pen(Color.FromArgb(38, 50, 68), 1f);
            g.FillPath(bg, path);
            g.DrawPath(pen, path);
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _actionToolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Subclass untuk SysHeader32 window handle.
    /// Mencegah Windows melukis blok putih di area sisa header tabel (sebelah kanan kolom terakhir).
    /// </summary>
    private class HeaderSubclass : NativeWindow
    {
        private readonly DarkListView _listView;

        public HeaderSubclass(IntPtr handle, DarkListView listView)
        {
            _listView = listView;
            AssignHandle(handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND)
            {
                // Warnai seluruh latar header kontrol dengan warna tema gelap
                if (m.WParam != IntPtr.Zero)
                {
                    using var g = Graphics.FromHdc(m.WParam);
                    GetClientRect(Handle, out RECT rect);
                    using var brush = new SolidBrush(Theme.HeaderBackground);
                    g.FillRectangle(brush, 0, 0, rect.Right, rect.Bottom);
                }
                m.Result = (IntPtr)1; // Pesan sudah ditangani
                return;
            }

            base.WndProc(ref m);

            if (m.Msg == WM_PAINT)
            {
                // Setelah header selesai melukis kolom-kolomnya, tutup sisa area kanan jika ada
                GetClientRect(Handle, out RECT rect);
                int totalColumnsWidth = 0;
                foreach (ColumnHeader col in _listView.Columns)
                {
                    totalColumnsWidth += col.Width;
                }

                if (totalColumnsWidth < rect.Right)
                {
                    using var g = Graphics.FromHwnd(Handle);
                    var remainderRect = new Rectangle(
                        Math.Max(0, totalColumnsWidth - 1),
                        0,
                        rect.Right - totalColumnsWidth + 4,
                        rect.Bottom);

                    using (var brush = new SolidBrush(Theme.HeaderBackground))
                    {
                        g.FillRectangle(brush, remainderRect);
                    }

                    using (var pen = new Pen(Theme.CardBorder, 1f))
                    {
                        g.DrawLine(pen, totalColumnsWidth, rect.Bottom - 1, rect.Right, rect.Bottom - 1);
                    }
                }
            }
        }
    }
}
