using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using DiskPulse.Core;
using DiskPulse.Models;

namespace DiskPulse.UI.Controls;

/// <summary>
/// Kontrol grid aplikasi terpasang berkinerja tinggi (100% Double-Buffered, Owner-Drawn).
/// Menggantikan FlowLayoutPanel multi-kontrol untuk sepenuhnya mengeliminasi tearing,
/// ghosting smearing, dan lag saat scrolling cepat up/down.
/// Dilengkapi animasi cincin putar pada logo aplikasi, status unduh/instalasi in-app,
/// luxury dark scrollbar, dan tombol Update online (winget).
/// </summary>
public class DarkAppGridControl : Control
{
    private List<AppUninstallInfo> _apps = [];
    private int _scrollY = 0;
    private int _hoveredCardIndex = -1;
    private int _hoveredButtonIndex = -1;
    private int _pressedButtonIndex = -1;
    private int _hoveredUpdateButtonIndex = -1;
    private int _pressedUpdateButtonIndex = -1;

    // Scrollbar kustom tema gelap
    private bool _isHoveringThumb = false;
    private bool _isDraggingThumb = false;
    private int _dragStartMouseY = 0;
    private int _dragStartScrollY = 0;

    // State pemuatan awal (loading state)
    private bool _isLoading = false;
    private string _loadingTitle = "Scanning Installed Applications...";
    private string _loadingDesc = "Reading Windows Registry, calculating install sizes, and loading icons...";
    private readonly DarkSpinner _spinner;

    // Animasi putar cincin unduh pada kartu aplikasi (Apple App Store aesthetic)
    private readonly System.Windows.Forms.Timer _animTimer;
    private float _spinnerAngle = 0f;

    // Font caching
    private readonly Font _fontTitle = new("Segoe UI", 9.5f, FontStyle.Bold);
    private readonly Font _fontSize = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _fontPath = new("Consolas", 7.5f, FontStyle.Regular);
    private readonly Font _fontBadge = new("Segoe UI", 7.2f, FontStyle.Bold);
    private readonly Font _fontButton = new("Segoe UI", 7.8f, FontStyle.Bold);
    private readonly Font _fontInitial = new("Segoe UI", 10.5f, FontStyle.Bold);
    private readonly Font _fontLoadingTitle = new("Segoe UI", 11.5f, FontStyle.Bold);

    public event Action<AppUninstallInfo>? UninstallRequested;
    public event Action<AppUninstallInfo>? UpdateRequested;

    public DarkAppGridControl()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        DoubleBuffered = true;
        BackColor = Theme.CardBackground;

        _spinner = new DarkSpinner
        {
            Size = new Size(38, 38),
            SpinnerColor = Theme.AccentCyan,
            Visible = false
        };
        Controls.Add(_spinner);

        // Timer untuk animasi cincin putar pada logo aplikasi saat mengunduh/memasang update
        _animTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _animTimer.Tick += (_, _) =>
        {
            _spinnerAngle = (_spinnerAngle + 12f) % 360f;
            Invalidate();
        };
    }

    public void SetApps(List<AppUninstallInfo> apps)
    {
        _apps = apps ?? [];
        _scrollY = 0;
        _hoveredCardIndex = -1;
        _hoveredButtonIndex = -1;
        _pressedButtonIndex = -1;
        _hoveredUpdateButtonIndex = -1;
        _pressedUpdateButtonIndex = -1;
        ClampScroll();
        NotifyUpdatingStateChanged();
        Invalidate();
    }

    public void NotifyUpdatingStateChanged()
    {
        bool anyUpdating = false;
        foreach (var a in _apps)
        {
            if (a.IsUpdating)
            {
                anyUpdating = true;
                break;
            }
        }

        if (anyUpdating && !_animTimer.Enabled)
        {
            _animTimer.Start();
        }
        else if (!anyUpdating && _animTimer.Enabled)
        {
            _animTimer.Stop();
        }

        Invalidate();
    }

    public void SetLoading(bool loading, string title = "", string desc = "")
    {
        _isLoading = loading;
        if (!string.IsNullOrEmpty(title)) _loadingTitle = title;
        if (!string.IsNullOrEmpty(desc)) _loadingDesc = desc;

        if (_isLoading)
        {
            _spinner.Location = new Point((Width - _spinner.Width) / 2, Math.Max(24, (Height / 2) - 50));
            _spinner.Visible = true;
            _spinner.Start();
        }
        else
        {
            _spinner.Stop();
            _spinner.Visible = false;
        }

        Invalidate();
    }

    public int AppCount => _apps.Count;
    public IReadOnlyList<AppUninstallInfo> Apps => _apps;

    public int ScrollY => _scrollY;

    public void ScrollBy(int delta)
    {
        SetScrollY(_scrollY + delta);
    }

    public void SetScrollY(int newScrollY)
    {
        int maxScroll = GetMaxScroll();
        int clamped = Math.Clamp(newScrollY, 0, maxScroll);
        if (clamped != _scrollY)
        {
            _scrollY = clamped;
            UpdateHoverStates(PointToClient(Cursor.Position));
            Invalidate();
        }
    }

    public int GetMaxScroll()
    {
        if (_apps.Count == 0 || ClientRectangle.Width <= 100) return 0;

        int padTop = 10;
        int padBottom = 16;
        int cardH = 84;
        int gapY = 8;

        int usableW = ClientRectangle.Width - 14 - 24;
        int cols = usableW >= 480 ? 2 : 1;
        int rows = (int)Math.Ceiling(_apps.Count / (double)cols);
        int totalH = padTop + (rows * (cardH + gapY)) + padBottom;

        return Math.Max(0, totalH - ClientRectangle.Height);
    }

    private void ClampScroll()
    {
        int maxScroll = GetMaxScroll();
        _scrollY = Math.Clamp(_scrollY, 0, maxScroll);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_isLoading)
        {
            _spinner.Location = new Point((Width - _spinner.Width) / 2, Math.Max(24, (Height / 2) - 50));
        }
        ClampScroll();
        UpdateHoverStates(PointToClient(Cursor.Position));
        Invalidate();
    }

    private bool GetCardBounds(int index, out Rectangle cardBounds, out Rectangle btnUninstallBounds, out Rectangle btnUpdateBounds)
    {
        cardBounds = Rectangle.Empty;
        btnUninstallBounds = Rectangle.Empty;
        btnUpdateBounds = Rectangle.Empty;
        if (index < 0 || index >= _apps.Count) return false;

        int padLeft = 14;
        int padTop = 10;
        int gapX = 12;
        int gapY = 8;
        int cardH = 84;

        int usableW = ClientRectangle.Width - padLeft - 24;
        if (usableW <= 100) return false;

        int cols = usableW >= 480 ? 2 : 1;
        int cardW = cols == 2 ? (usableW - gapX) / 2 : usableW;

        int col = index % cols;
        int row = index / cols;

        int cx = padLeft + (col * (cardW + gapX));
        int cy = padTop + (row * (cardH + gapY)) - _scrollY;

        cardBounds = new Rectangle(cx, cy, cardW, cardH);
        btnUninstallBounds = new Rectangle(cardBounds.Right - 82, cardBounds.Y + 50, 72, 24);

        var app = _apps[index];
        if (app.HasUpdate || app.IsUpdating)
        {
            int btnW = app.IsUpdating ? 104 : 74;
            int btnX = cardBounds.Right - 82 - btnW - 6;
            btnUpdateBounds = new Rectangle(btnX, cardBounds.Y + 50, btnW, 24);
        }

        return true;
    }

    private bool GetScrollbarLayout(out Rectangle trackRect, out Rectangle thumbRect, out Rectangle hitArea)
    {
        trackRect = Rectangle.Empty;
        thumbRect = Rectangle.Empty;
        hitArea = Rectangle.Empty;

        int maxScroll = GetMaxScroll();
        if (maxScroll <= 0 || _apps.Count == 0) return false;

        int sbW = 6;
        int sbRightMargin = 8;
        int sbX = ClientRectangle.Width - sbRightMargin - sbW;
        int sbTop = 10;
        int sbH = Math.Max(20, ClientRectangle.Height - 20);

        int totalH = maxScroll + ClientRectangle.Height;
        float viewRatio = (float)ClientRectangle.Height / totalH;
        int thumbH = Math.Clamp((int)(sbH * viewRatio), 26, sbH);
        int thumbY = sbTop + (int)((float)_scrollY / maxScroll * (sbH - thumbH));

        trackRect = new Rectangle(sbX, sbTop, sbW, sbH);
        thumbRect = new Rectangle(sbX, thumbY, sbW, thumbH);
        hitArea = new Rectangle(sbX - 8, sbTop, sbW + 16, sbH);
        return true;
    }

    private void UpdateHoverStates(Point pt)
    {
        if (_isLoading || _apps.Count == 0)
        {
            if (_hoveredCardIndex != -1 || _hoveredButtonIndex != -1 || _hoveredUpdateButtonIndex != -1 || _isHoveringThumb)
            {
                _hoveredCardIndex = -1;
                _hoveredButtonIndex = -1;
                _hoveredUpdateButtonIndex = -1;
                _isHoveringThumb = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
            return;
        }

        bool oldHoverThumb = _isHoveringThumb;
        int oldHoverCard = _hoveredCardIndex;
        int oldHoverBtn = _hoveredButtonIndex;
        int oldHoverUpdate = _hoveredUpdateButtonIndex;

        if (GetScrollbarLayout(out _, out var thumbRect, out var hitArea))
        {
            _isHoveringThumb = hitArea.Contains(pt);
            if (_isDraggingThumb || _isHoveringThumb)
            {
                _hoveredCardIndex = -1;
                _hoveredButtonIndex = -1;
                _hoveredUpdateButtonIndex = -1;
                Cursor = Cursors.Default;
                if (oldHoverThumb != _isHoveringThumb || oldHoverCard != -1 || oldHoverBtn != -1 || oldHoverUpdate != -1)
                {
                    Invalidate();
                }
                return;
            }
        }
        else
        {
            _isHoveringThumb = false;
        }

        int newHoverCard = -1;
        int newHoverBtn = -1;
        int newHoverUpdate = -1;

        // Cari kartu yang berpotongan dengan posisi mouse
        int padLeft = 14;
        int padTop = 10;
        int gapX = 12;
        int gapY = 8;
        int cardH = 84;
        int usableW = ClientRectangle.Width - padLeft - 24;

        if (usableW > 100)
        {
            int cols = usableW >= 480 ? 2 : 1;
            int cardW = cols == 2 ? (usableW - gapX) / 2 : usableW;

            int relY = pt.Y + _scrollY - padTop;
            int rowHeight = cardH + gapY;
            if (relY >= 0)
            {
                int row = relY / rowHeight;
                int yInRow = relY % rowHeight;
                if (yInRow < cardH)
                {
                    int relX = pt.X - padLeft;
                    if (relX >= 0)
                    {
                        int col = -1;
                        if (cols == 1 && relX < cardW) col = 0;
                        else if (cols == 2)
                        {
                            if (relX < cardW) col = 0;
                            else if (relX >= cardW + gapX && relX < (2 * cardW) + gapX) col = 1;
                        }

                        if (col >= 0)
                        {
                            int idx = (row * cols) + col;
                            if (idx < _apps.Count)
                            {
                                var app = _apps[idx];
                                newHoverCard = idx;
                                if (GetCardBounds(idx, out _, out var btnBounds, out var updateBounds))
                                {
                                    if (!app.IsUpdating)
                                    {
                                        if (btnBounds.Contains(pt))
                                        {
                                            newHoverBtn = idx;
                                        }
                                        else if (!updateBounds.IsEmpty && updateBounds.Contains(pt))
                                        {
                                            newHoverUpdate = idx;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        _hoveredCardIndex = newHoverCard;
        _hoveredButtonIndex = newHoverBtn;
        _hoveredUpdateButtonIndex = newHoverUpdate;

        Cursor = (newHoverBtn >= 0 || newHoverUpdate >= 0) ? Cursors.Hand : Cursors.Default;

        if (oldHoverThumb != _isHoveringThumb ||
            oldHoverCard != _hoveredCardIndex ||
            oldHoverBtn != _hoveredButtonIndex ||
            oldHoverUpdate != _hoveredUpdateButtonIndex)
        {
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isDraggingThumb && GetScrollbarLayout(out var trackRect, out var thumbRect, out _))
        {
            int maxScroll = GetMaxScroll();
            int deltaY = e.Y - _dragStartMouseY;
            float trackTravel = trackRect.Height - thumbRect.Height;
            if (trackTravel > 0)
            {
                float scrollRatio = deltaY / trackTravel;
                int targetScroll = (int)(_dragStartScrollY + (scrollRatio * maxScroll));
                SetScrollY(targetScroll);
            }
            return;
        }

        UpdateHoverStates(e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Left)
        {
            if (GetScrollbarLayout(out var trackRect, out var thumbRect, out var hitArea) && hitArea.Contains(e.Location))
            {
                if (thumbRect.Contains(e.Location))
                {
                    _isDraggingThumb = true;
                    _dragStartMouseY = e.Y;
                    _dragStartScrollY = _scrollY;
                    Capture = true;
                    Invalidate();
                }
                else
                {
                    // Klik di luar thumb (page up / page down)
                    if (e.Y < thumbRect.Y) ScrollBy(-ClientRectangle.Height);
                    else ScrollBy(ClientRectangle.Height);
                }
                return;
            }

            if (_hoveredButtonIndex >= 0 && _hoveredButtonIndex < _apps.Count)
            {
                if (!_apps[_hoveredButtonIndex].IsUpdating)
                {
                    _pressedButtonIndex = _hoveredButtonIndex;
                    Capture = true;
                    Invalidate();
                    return;
                }
            }

            if (_hoveredUpdateButtonIndex >= 0 && _hoveredUpdateButtonIndex < _apps.Count)
            {
                if (!_apps[_hoveredUpdateButtonIndex].IsUpdating)
                {
                    _pressedUpdateButtonIndex = _hoveredUpdateButtonIndex;
                    Capture = true;
                    Invalidate();
                    return;
                }
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isDraggingThumb)
        {
            _isDraggingThumb = false;
            Capture = false;
            Invalidate();
            return;
        }

        if (_pressedButtonIndex >= 0)
        {
            int clicked = _pressedButtonIndex;
            _pressedButtonIndex = -1;
            Capture = false;
            Invalidate();

            if (_hoveredButtonIndex == clicked && clicked < _apps.Count)
            {
                UninstallRequested?.Invoke(_apps[clicked]);
            }
            return;
        }

        if (_pressedUpdateButtonIndex >= 0)
        {
            int clicked = _pressedUpdateButtonIndex;
            _pressedUpdateButtonIndex = -1;
            Capture = false;
            Invalidate();

            if (_hoveredUpdateButtonIndex == clicked && clicked < _apps.Count)
            {
                UpdateRequested?.Invoke(_apps[clicked]);
            }
            return;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int scrollDelta = -Math.Sign(e.Delta) * 64;
        SetScrollY(_scrollY + scrollDelta);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_isDraggingThumb && _pressedButtonIndex == -1 && _pressedUpdateButtonIndex == -1)
        {
            _hoveredCardIndex = -1;
            _hoveredButtonIndex = -1;
            _hoveredUpdateButtonIndex = -1;
            _isHoveringThumb = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        // 1. Tampilan Status Memuat Awal (Scanning Registry)
        if (_isLoading)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int cy = _spinner.Bottom + 16;
            var titleRect = new Rectangle(20, cy, Width - 40, 24);
            var descRect = new Rectangle(20, cy + 26, Width - 40, 20);
            TextRenderer.DrawText(g, _loadingTitle, _fontLoadingTitle, titleRect, Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _loadingDesc, Theme.SmallFont, descRect, Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        // 2. Tampilan Kosong (Tidak Ada Aplikasi Terpasang)
        if (_apps.Count == 0)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var emptyRect = new Rectangle(20, 40, Width - 40, 40);
            TextRenderer.DrawText(g, "No registered applications found on this system.", Theme.BodyFont, emptyRect, Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        // 3. Render Kartu Aplikasi (100% Double-Buffered, Zero Tearing)
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int viewTop = 0;
        int viewBottom = ClientRectangle.Height;

        for (int i = 0; i < _apps.Count; i++)
        {
            if (!GetCardBounds(i, out var cardRect, out var btnUninstallRect, out var btnUpdateRect)) continue;

            // Viewport culling: hanya gambar kartu yang tampak di layar
            if (cardRect.Bottom < viewTop || cardRect.Top > viewBottom) continue;

            DrawAppCard(g, i, _apps[i], cardRect, btnUninstallRect, btnUpdateRect);
        }

        // 4. Render Scrollbar Modern Tema Gelap
        if (GetScrollbarLayout(out var trackRect, out var thumbRect, out _))
        {
            using var trackPath = Theme.CreateRoundedRectangle(new RectangleF(trackRect.X, trackRect.Y, trackRect.Width, trackRect.Height), 3f);
            using var trackBrush = new SolidBrush(Color.FromArgb(16, 21, 30));
            g.FillPath(trackBrush, trackPath);

            Color thumbColor = _isDraggingThumb
                ? Color.FromArgb(110, 140, 185)
                : (_isHoveringThumb ? Color.FromArgb(80, 105, 140) : Color.FromArgb(46, 58, 80));

            using var thumbPath = Theme.CreateRoundedRectangle(new RectangleF(thumbRect.X, thumbRect.Y, thumbRect.Width, thumbRect.Height), 3f);
            using var thumbBrush = new SolidBrush(thumbColor);
            g.FillPath(thumbBrush, thumbPath);
        }
    }

    private void DrawAppCard(Graphics g, int index, AppUninstallInfo app, Rectangle cardRect, Rectangle btnUninstallRect, Rectangle btnUpdateRect)
    {
        // A. Background & Border Kartu
        using var cardPath = Theme.CreateRoundedRectangle(new RectangleF(cardRect.X + 0.5f, cardRect.Y + 0.5f, cardRect.Width - 1f, cardRect.Height - 1f), 10f);
        bool isCardHovered = _hoveredCardIndex == index && _hoveredButtonIndex != index && _hoveredUpdateButtonIndex != index;
        Color cardBg = isCardHovered ? Color.FromArgb(22, 28, 40) : Color.FromArgb(17, 21, 30);
        Color cardBorder = isCardHovered ? Color.FromArgb(50, 65, 92) : Color.FromArgb(32, 38, 52);

        using (var brushBg = new SolidBrush(cardBg))
        using (var penBorder = new Pen(cardBorder, 1f))
        {
            g.FillPath(brushBg, cardPath);
            g.DrawPath(penBorder, cardPath);
        }

        // B. Kotak Ikon Aplikasi (44x44)
        var iconBoxRect = new Rectangle(cardRect.X + 12, cardRect.Y + 18, 44, 44);
        using (var iconBoxPath = Theme.CreateRoundedRectangle(new RectangleF(iconBoxRect.X + 0.5f, iconBoxRect.Y + 0.5f, iconBoxRect.Width - 1f, iconBoxRect.Height - 1f), 8f))
        using (var iconBg = new SolidBrush(Color.FromArgb(20, 26, 38)))
        using (var iconPen = new Pen(Color.FromArgb(40, 52, 75), 1f))
        {
            g.FillPath(iconBg, iconBoxPath);
            g.DrawPath(iconPen, iconBoxPath);
        }

        var appIcon = AppIconService.GetAppIcon(app);
        if (appIcon != null)
        {
            int ix = iconBoxRect.X + ((iconBoxRect.Width - appIcon.Width) / 2);
            int iy = iconBoxRect.Y + ((iconBoxRect.Height - appIcon.Height) / 2);
            g.DrawImage(appIcon, ix, iy, appIcon.Width, appIcon.Height);
        }
        else
        {
            string initial = !string.IsNullOrWhiteSpace(app.DisplayName)
                ? (app.DisplayName.Length >= 2 ? app.DisplayName.Substring(0, 2).ToUpper() : app.DisplayName.Substring(0, 1).ToUpper())
                : "AP";
            TextRenderer.DrawText(g, initial, _fontInitial, iconBoxRect, Theme.AccentCyan,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // ANIMASI CINCIN PUTAR LOADING PADA LOGO APLIKASI SAAT SEDANG UPDATE
        if (app.IsUpdating)
        {
            using var overlayPath = Theme.CreateRoundedRectangle(new RectangleF(iconBoxRect.X + 0.5f, iconBoxRect.Y + 0.5f, iconBoxRect.Width - 1f, iconBoxRect.Height - 1f), 8f);
            using var overlayBrush = new SolidBrush(Color.FromArgb(180, 10, 15, 24));
            g.FillPath(overlayBrush, overlayPath);

            using var trackPen = new Pen(Color.FromArgb(32, 48, 70), 2.5f);
            var ringRect = new RectangleF(iconBoxRect.X + 8.5f, iconBoxRect.Y + 8.5f, 27f, 27f);
            g.DrawEllipse(trackPen, ringRect);

            using var spinPen = new Pen(Theme.AccentCyan, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(spinPen, ringRect, _spinnerAngle, 105f);
        }

        // C. Judul Aplikasi (Baris 1 Kiri) & Ukuran (Baris 1 Kanan)
        int sizeW = 82;
        var sizeRect = new Rectangle(cardRect.Right - sizeW - 10, cardRect.Y + 10, sizeW, 18);
        TextRenderer.DrawText(g, app.FormattedSize, _fontSize, sizeRect, Theme.AccentEmerald,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        int titleW = Math.Max(60, cardRect.Width - 64 - sizeW - 16);
        var titleRect = new Rectangle(cardRect.X + 64, cardRect.Y + 10, titleW, 18);
        TextRenderer.DrawText(g, app.DisplayName, _fontTitle, titleRect, Theme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // D. Lokasi Path Instalasi / Penerbit (Baris 2)
        string locPath = !string.IsNullOrWhiteSpace(app.InstallLocation)
            ? app.InstallLocation
            : (!string.IsNullOrWhiteSpace(app.Publisher)
                ? (!string.IsNullOrWhiteSpace(app.DisplayVersion) ? $"{app.DisplayVersion} • {app.Publisher}" : app.Publisher)
                : "Windows Application");

        if (app.IsUpdating)
        {
            locPath = $"⚡ Updating to v{app.AvailableVersion}: {app.UpdateStatusText}";
        }
        else if (app.HasUpdate && !string.IsNullOrEmpty(app.AvailableVersion))
        {
            locPath = $"{locPath} • ⚡ Latest: v{app.AvailableVersion}";
        }

        int pathW = Math.Max(60, cardRect.Width - 74);
        var pathRect = new Rectangle(cardRect.X + 64, cardRect.Y + 31, pathW, 16);
        Color pathCol = app.IsUpdating ? Theme.AccentCyan : (app.HasUpdate ? Color.FromArgb(147, 197, 253) : Theme.TextMuted);
        TextRenderer.DrawText(g, locPath, _fontPath, pathRect, pathCol,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // E. Badge Uninstaller (Baris 3 Kiri)
        bool hasOfficial = !string.IsNullOrEmpty(app.UninstallString);
        int badgeW = hasOfficial ? 122 : 115;

        // Jika ada tombol update / status, batasi lebar badge agar tidak menabrak
        int maxBadgeRight = (!btnUpdateRect.IsEmpty) ? btnUpdateRect.Left - 10 : btnUninstallRect.Left - 10;
        int availBadgeW = Math.Max(70, maxBadgeRight - (cardRect.X + 64));
        badgeW = Math.Min(badgeW, availBadgeW);

        var badgeRect = new Rectangle(cardRect.X + 64, cardRect.Y + 52, badgeW, 20);

        Color badgeBg = hasOfficial ? Color.FromArgb(10, 40, 30) : Color.FromArgb(45, 25, 8);
        Color badgeBorder = hasOfficial ? Color.FromArgb(16, 140, 90) : Color.FromArgb(190, 100, 15);
        Color badgeTextCol = hasOfficial ? Theme.AccentEmerald : Color.FromArgb(245, 158, 11);

        using (var badgePath = Theme.CreateRoundedRectangle(new RectangleF(badgeRect.X + 0.5f, badgeRect.Y + 0.5f, badgeRect.Width - 1f, badgeRect.Height - 1f), 4f))
        using (var bgBrush = new SolidBrush(badgeBg))
        using (var borderPen = new Pen(badgeBorder, 1f))
        {
            g.FillPath(bgBrush, badgePath);
            g.DrawPath(borderPen, badgePath);
        }

        string badgeText = hasOfficial ? "Official Uninstaller" : "Custom Directory";
        TextRenderer.DrawText(g, badgeText, _fontBadge, badgeRect, badgeTextCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // F. Tombol Online Update / Status Live In-App Update
        if ((app.HasUpdate || app.IsUpdating) && !btnUpdateRect.IsEmpty)
        {
            if (app.IsUpdating)
            {
                string statusTxt = string.IsNullOrEmpty(app.UpdateStatusText) ? "Downloading..." : app.UpdateStatusText;
                bool isInstalling = statusTxt.Contains("Installing", StringComparison.OrdinalIgnoreCase);
                bool isUpdated = statusTxt.Contains("Updated", StringComparison.OrdinalIgnoreCase);
                bool isFailed = statusTxt.Contains("Failed", StringComparison.OrdinalIgnoreCase);

                Color upgBg = isUpdated
                    ? Color.FromArgb(12, 50, 32)
                    : (isInstalling ? Color.FromArgb(20, 50, 26) : (isFailed ? Color.FromArgb(50, 14, 20) : Color.FromArgb(10, 48, 65)));

                Color upgBorder = isUpdated
                    ? Theme.AccentEmerald
                    : (isInstalling ? Color.FromArgb(52, 211, 153) : (isFailed ? Theme.AccentRose : Theme.AccentCyan));

                Color upgTextCol = isUpdated
                    ? Theme.AccentEmerald
                    : (isInstalling ? Color.FromArgb(167, 243, 208) : (isFailed ? Color.FromArgb(253, 164, 175) : Color.FromArgb(186, 230, 253)));

                string btnText = isUpdated
                    ? "✓ Updated!"
                    : (isInstalling ? "⚙ Installing..." : (isFailed ? "Failed" : "⬇ Downloading..."));

                using var upgPath = Theme.CreateRoundedRectangle(new RectangleF(btnUpdateRect.X + 0.5f, btnUpdateRect.Y + 0.5f, btnUpdateRect.Width - 1f, btnUpdateRect.Height - 1f), 4f);
                using var upgBgBrush = new SolidBrush(upgBg);
                using var upgBorderPen = new Pen(upgBorder, 1.2f);

                g.FillPath(upgBgBrush, upgPath);
                g.DrawPath(upgBorderPen, upgPath);

                TextRenderer.DrawText(g, btnText, _fontButton, btnUpdateRect, upgTextCol,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                bool isUpdateHovered = _hoveredUpdateButtonIndex == index;
                bool isUpdatePressed = _pressedUpdateButtonIndex == index;

                Color upgBg = isUpdatePressed ? Color.FromArgb(18, 115, 160) : (isUpdateHovered ? Color.FromArgb(14, 85, 120) : Color.FromArgb(10, 48, 65));
                Color upgBorder = isUpdatePressed ? Color.FromArgb(125, 211, 252) : (isUpdateHovered ? Color.FromArgb(56, 189, 248) : Color.FromArgb(14, 165, 233));
                Color upgTextCol = (isUpdateHovered || isUpdatePressed) ? Color.White : Color.FromArgb(56, 189, 248);

                using var upgPath = Theme.CreateRoundedRectangle(new RectangleF(btnUpdateRect.X + 0.5f, btnUpdateRect.Y + 0.5f, btnUpdateRect.Width - 1f, btnUpdateRect.Height - 1f), 4f);
                using var upgBgBrush = new SolidBrush(upgBg);
                using var upgBorderPen = new Pen(upgBorder, 1f);

                g.FillPath(upgBgBrush, upgPath);
                g.DrawPath(upgBorderPen, upgPath);

                TextRenderer.DrawText(g, "⚡ Update", _fontButton, btnUpdateRect, upgTextCol,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        // G. Tombol Uninstall (Baris 3 Kanan)
        if (app.IsUpdating)
        {
            // Dimmed/disabled saat sedang proses update
            using var btnPath = Theme.CreateRoundedRectangle(new RectangleF(btnUninstallRect.X + 0.5f, btnUninstallRect.Y + 0.5f, btnUninstallRect.Width - 1f, btnUninstallRect.Height - 1f), 4f);
            using var btnBgBrush = new SolidBrush(Color.FromArgb(24, 28, 38));
            using var btnBorderPen = new Pen(Color.FromArgb(40, 48, 64), 1f);

            g.FillPath(btnBgBrush, btnPath);
            g.DrawPath(btnBorderPen, btnPath);

            TextRenderer.DrawText(g, "Uninstall", _fontButton, btnUninstallRect, Color.FromArgb(70, 80, 100),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        else
        {
            bool isBtnHovered = _hoveredButtonIndex == index;
            bool isBtnPressed = _pressedButtonIndex == index;

            Color btnBg = isBtnPressed ? Color.FromArgb(85, 20, 35) : (isBtnHovered ? Color.FromArgb(60, 18, 30) : Color.FromArgb(40, 14, 22));
            Color btnBorder = isBtnPressed ? Color.FromArgb(244, 63, 94) : (isBtnHovered ? Color.FromArgb(225, 29, 72) : Color.FromArgb(140, 20, 50));
            Color btnTextCol = (isBtnHovered || isBtnPressed) ? Color.White : Color.FromArgb(253, 164, 175);

            using var btnPath = Theme.CreateRoundedRectangle(new RectangleF(btnUninstallRect.X + 0.5f, btnUninstallRect.Y + 0.5f, btnUninstallRect.Width - 1f, btnUninstallRect.Height - 1f), 4f);
            using var btnBgBrush = new SolidBrush(btnBg);
            using var btnBorderPen = new Pen(btnBorder, 1f);

            g.FillPath(btnBgBrush, btnPath);
            g.DrawPath(btnBorderPen, btnPath);

            TextRenderer.DrawText(g, "Uninstall", _fontButton, btnUninstallRect, btnTextCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
            _fontTitle.Dispose();
            _fontSize.Dispose();
            _fontPath.Dispose();
            _fontBadge.Dispose();
            _fontButton.Dispose();
            _fontInitial.Dispose();
            _fontLoadingTitle.Dispose();
            _spinner.Dispose();
        }
        base.Dispose(disposing);
    }
}
