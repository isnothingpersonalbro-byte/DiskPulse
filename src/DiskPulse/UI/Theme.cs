using System.Drawing.Drawing2D;

namespace DiskPulse.UI;

/// <summary>
/// Definisi tema gelap modern (Windows 11 Fluent Dark) dengan tekstur halus dan palet warna kohesif.
/// </summary>
public static class Theme
{
    // Palet Warna Gelap Fluent 2 & Obsidian Luxury
    public static readonly Color Background = Color.FromArgb(10, 12, 18);          // #0A0C12 Obsidian Canvas
    public static readonly Color SidebarBackground = Color.FromArgb(9, 11, 17);   // #090B11 Deep Sidebar
    public static readonly Color CardBackground = Color.FromArgb(17, 20, 30);      // #11141E Card Surface
    public static readonly Color CardBackgroundHover = Color.FromArgb(24, 28, 42); // #181C2A
    public static readonly Color CardBackgroundElevated = Color.FromArgb(22, 26, 38); // #161A26
    public static readonly Color CardBorder = Color.FromArgb(35, 42, 58);          // #232A3A Subtle border
    public static readonly Color HeaderBackground = Color.FromArgb(13, 16, 24);   // #0D1018 Table/Panel header

    // Teks & Tipografi
    public static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);      // #F8FAFC
    public static readonly Color TextSecondary = Color.FromArgb(148, 163, 184);    // #94A3B8
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);        // #64748B

    // Aksen Warna
    public static readonly Color AccentCyan = Color.FromArgb(6, 182, 212);        // #06B6D4 Luminous Cyan
    public static readonly Color AccentCyanGlow = Color.FromArgb(45, 6, 182, 212);
    public static readonly Color AccentBlue = Color.FromArgb(59, 130, 246);        // #3B82F6
    public static readonly Color AccentBlueHover = Color.FromArgb(37, 99, 235);   // #2563EB
    public static readonly Color AccentEmerald = Color.FromArgb(16, 185, 129);     // #10B981
    public static readonly Color AccentEmeraldHover = Color.FromArgb(5, 150, 105);
    public static readonly Color AccentAmber = Color.FromArgb(245, 158, 11);       // #F59E0B
    public static readonly Color AccentRose = Color.FromArgb(239, 68, 68);         // #EF4444
    public static readonly Color AccentRoseHover = Color.FromArgb(220, 38, 38);
    public static readonly Color ControlBg = Color.FromArgb(24, 28, 40);           // #181C28
    public static readonly Color ControlBgHover = Color.FromArgb(34, 40, 56);
    public static readonly Color TrackBg = Color.FromArgb(25, 30, 44);

    // Tipografi Berbasis Segoe UI
    public static readonly Font TitleFont = new("Segoe UI", 13.5f, FontStyle.Bold);
    public static readonly Font SubtitleFont = new("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font SectionFont = new("Segoe UI", 10.5f, FontStyle.Bold);
    public static readonly Font BodyFont = new("Segoe UI", 9.25f, FontStyle.Regular);
    public static readonly Font BodyBold = new("Segoe UI", 9.25f, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8.25f, FontStyle.Regular);
    public static readonly Font SmallBold = new("Segoe UI", 8.25f, FontStyle.Bold);
    public static readonly Font MonospaceFont = new("Consolas", 8.5f, FontStyle.Regular);

    /// <summary>
    /// Membuat GraphicsPath Rounded Rectangle presisi tinggi tanpa distorsi sudut atau akumulasi pixel.
    /// </summary>
    public static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        float d = radius * 2f;
        if (d > bounds.Width) d = bounds.Width;
        if (d > bounds.Height) d = bounds.Height;

        // Top-left
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        // Top-right
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        // Bottom-right
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        // Bottom-left
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);

        path.CloseFigure();
        return path;
    }
}
