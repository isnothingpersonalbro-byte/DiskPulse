using System.Drawing.Drawing2D;

namespace DiskPulse.UI.Controls;

public enum CaptionButtonType
{
    Minimize,
    Maximize,
    Close
}

/// <summary>
/// Tombol kontrol jendela (Minimize, Maximize/Restore, Close) yang terintegrasi langsung
/// ke dalam Header Bar aplikasi dengan rendering vektor modern dan sudut membulat halus.
/// </summary>
public class WindowCaptionButton : Control
{
    private bool _isHovered;
    private bool _isPressed;
    private bool _isMaximized;

    public CaptionButtonType ButtonType { get; set; } = CaptionButtonType.Minimize;

    public bool IsMaximized
    {
        get => _isMaximized;
        set
        {
            if (_isMaximized != value)
            {
                _isMaximized = value;
                Invalidate();
            }
        }
    }

    public WindowCaptionButton(CaptionButtonType type)
    {
        ButtonType = type;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        Size = type == CaptionButtonType.Close ? new Size(38, 28) : new Size(32, 28);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
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
        if (_isPressed)
        {
            _isPressed = false;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Gambar latar belakang saat hover atau ditekan
        if (_isHovered)
        {
            Color bg = ButtonType == CaptionButtonType.Close
                ? (_isPressed ? Color.FromArgb(190, 18, 60) : Color.FromArgb(225, 29, 72))
                : (_isPressed ? Color.FromArgb(22, 28, 42) : Color.FromArgb(32, 40, 58));

            var bgRect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            using var bgPath = Theme.CreateRoundedRectangle(bgRect, 6f);
            using var brush = new SolidBrush(bg);
            g.FillPath(brush, bgPath);
        }

        // Tentukan warna ikon
        Color iconColor = _isHovered
            ? Color.White
            : Color.FromArgb(150, 168, 192);

        float cx = Width / 2f;
        float cy = Height / 2f;

        using var pen = new Pen(iconColor, 1.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        switch (ButtonType)
        {
            case CaptionButtonType.Minimize:
                // Garis horizontal minimalis
                g.DrawLine(pen, cx - 5f, cy, cx + 5f, cy);
                break;

            case CaptionButtonType.Maximize:
                if (_isMaximized)
                {
                    // Ikon Restore (dua kotak saling bertumpuk)
                    using var restorePen = new Pen(iconColor, 1.2f);
                    // Kotak belakang
                    g.DrawRectangle(restorePen, cx - 2.5f, cy - 4.5f, 6.5f, 6.5f);
                    // Kotak depan
                    using var fillBrush = new SolidBrush(_isHovered ? Color.FromArgb(32, 40, 58) : Theme.HeaderBackground);
                    g.FillRectangle(fillBrush, cx - 4.5f, cy - 2.5f, 6.5f, 6.5f);
                    g.DrawRectangle(restorePen, cx - 4.5f, cy - 2.5f, 6.5f, 6.5f);
                }
                else
                {
                    // Kotak tunggal rapi
                    using var maxPen = new Pen(iconColor, 1.3f);
                    g.DrawRectangle(maxPen, cx - 4.5f, cy - 4.5f, 9f, 9f);
                }
                break;

            case CaptionButtonType.Close:
                // Garis silang presisi 45 derajat
                float sz = 4.2f;
                g.DrawLine(pen, cx - sz, cy - sz, cx + sz, cy + sz);
                g.DrawLine(pen, cx + sz, cy - sz, cx - sz, cy + sz);
                break;
        }
    }
}
