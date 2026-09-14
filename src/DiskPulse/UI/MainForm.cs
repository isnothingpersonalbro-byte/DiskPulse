using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Security.Principal;
using DiskPulse.Core;
using DiskPulse.Models;
using DiskPulse.UI.Controls;

namespace DiskPulse.UI;

/// <summary>
/// Jendela Utama DiskPulse dengan dukungan pemilihan Multi-Drive (Drive C:, D:, E:, dll.),
/// tampilan dark Fluent presisi bebas dari cacat sudut atau akumulasi warna subpixel,
/// serta penganalisis dan pembersih disk yang sepenuhnya responsif.
/// </summary>
public partial class MainForm : Form
{
    private readonly DiskMonitorService _diskMonitor;
    private readonly JunkScannerService _junkScanner;
    private readonly FolderAnalyzerService _folderAnalyzer;
    private readonly FileCleanerService _fileCleaner;

    private List<DiskUsageInfo> _availableDrives = [];
    private string _selectedDrive = "C:\\";
    private bool _suppressDriveSelectionChanged;
    private List<JunkItemCategory> _categories = [];
    private DiskUsageInfo? _currentDiskUsage;
    private CancellationTokenSource? _cts;

    // Header, Pemilih Drive & Kontrol Jendela
    private Panel _pnlHeader = null!;
    private DarkComboBox _cmbDrives = null!;
    private DarkButton _btnHeaderRefresh = null!;
    private Label _lblAdminBadge = null!;
    private Panel _pnlWindowActions = null!;
    private WindowCaptionButton _btnWinMinimize = null!;
    private WindowCaptionButton _btnWinMaximize = null!;
    private WindowCaptionButton _btnWinClose = null!;

    // Sidebar & Navigasi Kiri (Ruang Kerja)
    private Panel _pnlSidebar = null!;
    private DarkButton _btnNavCleaner = null!;
    private DarkButton _btnNavFolders = null!;
    private DarkButton _btnNavApps = null!;
    private DarkCardPanel _cardSidebarDisk = null!;
    private Label _lblSidebarStatus = null!;
    private Label _lblSidebarPct = null!;
    private Panel _pnlSidebarBarTrack = null!;
    private Panel _pnlSidebarBarFill = null!;
    private Label _lblSidebarStats = null!;
    private DarkButton _btnUpdateApp = null!;

    // Kanvas Utama (Main Canvas & Workspace)
    private Panel _pnlMainCanvas = null!;
    private Panel _pnlWorkspace = null!;
    private Panel _pnlTransitionOverlay = null!;
    private Bitmap? _transitionSnapshot;
    private float _transitionAlpha = 0f;
    private System.Windows.Forms.Timer? _transitionTimer;
    private int _currentTabIndex = -1;

    // Hero Card: Centerpiece Dual-Ring Gauge & Metrik Kapasitas
    private DarkCardPanel _cardDisk = null!;
    private ModernRingMeter _ringMeter = null!;

    // Tab 1: Optimasi Penyimpanan (Dedicated Room)
    private Panel _pnlContentCleaner = null!;
    private DarkCardPanel _cardTableCleaner = null!;
    private Panel _pnlCleanerHeader = null!;
    private Label _lblCleanerTitle = null!;
    private Label _lblCleanerSub = null!;
    private Label _lblCleanerBadge = null!;
    private DarkListView _lvCategories = null!;
    private DarkCardPanel _cardActionsCleaner = null!;
    private CheckBox _chkDryRun = null!;
    private DarkButton _btnSelectAll = null!;
    private DarkButton _btnUnselectAll = null!;
    private DarkButton _btnScan = null!;
    private DarkButton _btnClean = null!;
    private DarkButton _btnCancel = null!;
    private DarkButton _btnToggleLog = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblCleanStatus = null!;

    // Tab 2: Penganalisis Folder & Rincian Drill-Down
    private Panel _pnlContentFolders = null!;
    private DarkCardPanel _cardTableFolders = null!;
    private Panel _pnlFolderNav = null!;
    private DarkButton _btnBackFolder = null!;
    private FlowLayoutPanel _pnlBreadcrumb = null!;
    private FolderSummaryBadge _lblFolderSummary = null!;
    private DarkButton _btnScanWholeDrive = null!;
    private DarkButton _btnAnalyzeFolders = null!;
    private DarkListView _lvFolders = null!;

    private DarkOverlayPanel _pnlFolderOverlay = null!;
    private DarkSpinner _spinnerFolder = null!;
    private Label _lblFolderOverlayTitle = null!;
    private Label _lblFolderOverlayStatus = null!;
    private DarkButton _btnFolderOverlayAction = null!;


    // Tab 3: Manajemen Aplikasi
    private Panel _pnlContentApps = null!;
    private DarkCardPanel _cardTableApps = null!;
    private DarkAppGridControl _gridApps = null!;
    private Label _lblAppsTitle = null!;
    private Label _lblAppsSub = null!;
    private Label _lblAppsBadge = null!;
    private DarkButton _btnOpenWindowsApps = null!;
    private DarkButton _btnCheckAppUpdates = null!;
    private bool _isCheckingUpdates = false;
    private List<AppUninstallInfo> _installedApps = [];

    // State Navigasi Folder
    private List<FolderSizeItem> _cachedDriveFolders = [];
    private readonly Stack<string> _folderNavHistory = new();
    private string? _currentInspectedPath;
    private bool _isInDetailView;
    private bool _isScanningFolders;

    // Layanan Uninstaller & Menu Konteks Klik Kanan
    private readonly AppUninstallerService _uninstallerService;
    private DarkContextMenu _ctxFolderMenu = null!;

    // Laci Log Aktivitas (Collapsible Drawer)
    private DarkCardPanel _cardLogDrawer = null!;
    private TextBox _txtLog = null!;
    private bool _isLogExpanded;

    public MainForm()
    {
        _diskMonitor = new DiskMonitorService("C:\\");
        _junkScanner = new JunkScannerService();
        _folderAnalyzer = new FolderAnalyzerService("C:\\");
        _fileCleaner = new FileCleanerService();
        _uninstallerService = new AppUninstallerService();

        InitializeCustomComponents();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.EnableImmersiveDarkMode(Handle);
        NativeMethods.EnableWindowRounding(Handle);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW (Shadow halus di Windows)
            return cp;
        }
    }

    private const int WM_NCHITTEST = 0x84;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int BORDER_RESIZE_THICKNESS = 7;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                var screenPoint = new Point(m.LParam.ToInt32());
                var clientPoint = PointToClient(screenPoint);

                bool onLeft = clientPoint.X <= BORDER_RESIZE_THICKNESS;
                bool onRight = clientPoint.X >= ClientSize.Width - BORDER_RESIZE_THICKNESS;
                bool onTop = clientPoint.Y <= BORDER_RESIZE_THICKNESS;
                bool onBottom = clientPoint.Y >= ClientSize.Height - BORDER_RESIZE_THICKNESS;

                if (onTop && onLeft) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (onTop && onRight) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (onBottom && onLeft) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (onBottom && onRight) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (onLeft) { m.Result = (IntPtr)HTLEFT; return; }
                if (onRight) { m.Result = (IntPtr)HTRIGHT; return; }
                if (onTop) { m.Result = (IntPtr)HTTOP; return; }
                if (onBottom) { m.Result = (IntPtr)HTBOTTOM; return; }
            }
            return;
        }

        base.WndProc(ref m);
    }

    private void ApplyWindowRegion()
    {
        try
        {
            if (WindowState == FormWindowState.Normal)
            {
                IntPtr hRgn = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 16, 16);
                Region = Region.FromHrgn(hRgn);
                NativeMethods.DeleteObject(hRgn);
            }
            else
            {
                Region = null;
            }
        }
        catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (WindowState == FormWindowState.Normal)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using var path = Theme.CreateRoundedRectangle(r, 16f);
            using var borderPen = new Pen(Color.FromArgb(45, 55, 78), 1.2f);
            e.Graphics.DrawPath(borderPen, path);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyWindowRegion();
        if (_btnWinMaximize != null)
        {
            _btnWinMaximize.IsMaximized = (WindowState == FormWindowState.Maximized);
        }
        if (WindowState == FormWindowState.Maximized)
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
        }
        Invalidate();
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        ApplyWindowRegion();
        if (_btnWinMaximize != null)
        {
            _btnWinMaximize.IsMaximized = (WindowState == FormWindowState.Maximized);
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyWindowRegion();
        _suppressDriveSelectionChanged = true;
        try
        {
            LoadAvailableDrives();
        }
        finally
        {
            _suppressDriveSelectionChanged = false;
        }

        await OnDriveSelectionChangedAsync();
    }

    private void InitializeCustomComponents()
    {
        Text = "DiskPulse - Storage Monitor & Cleaner";
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        Size = new Size(1120, 800);
        MinimumSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = Theme.BodyFont;

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { }

        // 1. Header Bar dengan Dropdown Drive & Logo PRO
        BuildHeader();

        // 2. Left Sidebar (Ruang Kerja)
        BuildSidebar();

        // 3. Hero Card (Large Centerpiece Dual-Ring Meter)
        BuildDiskCard();

        // 4. Tab 1: Pembersih Sampah (Dedicated Room)
        BuildCleanerTab();

        // 5. Tab 2: Penganalisis Folder
        BuildFoldersTab();

        // 6. Tab 3: Manajemen Aplikasi
        BuildAppsTab();

        // 7. Log Drawer (Collapsible)
        BuildLogDrawer();

        // 8. Main Canvas (Hero Card + Dedicated Workspace)
        BuildMainCanvas();

        // Susun Tata Letak Form
        Controls.Add(_pnlMainCanvas);
        Controls.Add(_pnlSidebar);
        Controls.Add(_cardLogDrawer);
        Controls.Add(_pnlHeader);

        _pnlHeader.BringToFront();
        _pnlSidebar.BringToFront();
        _cardLogDrawer.BringToFront();
        _pnlMainCanvas.BringToFront();

        SwitchTab(0);
    }

    private void BuildHeader()
    {
        _pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(18, 10, 18, 10),
            BackColor = Theme.HeaderBackground
        };

        _pnlHeader.Paint += (_, pe) =>
        {
            using var bPen = new Pen(Theme.CardBorder, 1f);
            pe.Graphics.DrawLine(bPen, 0, _pnlHeader.Height - 1, _pnlHeader.Width, _pnlHeader.Height - 1);
        };

        // Logo Kubus Vektor Isometrik 3D & Judul DiskPulse (Mockup Gambar 5)
        var pnlAppLogo = new Panel
        {
            Size = new Size(26, 26),
            Location = new Point(14, 15),
            BackColor = Color.Transparent
        };
        pnlAppLogo.Paint += (_, pe) =>
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float cx = 13f, cy = 13f;
            float r = 10f;
            PointF top = new(cx, cy - r);
            PointF topR = new(cx + r * 0.866f, cy - r * 0.5f);
            PointF botR = new(cx + r * 0.866f, cy + r * 0.5f);
            PointF bot = new(cx, cy + r);
            PointF botL = new(cx - r * 0.866f, cy + r * 0.5f);
            PointF topL = new(cx - r * 0.866f, cy - r * 0.5f);
            PointF center = new(cx, cy);

            // Sisi Atas (Top Face - Cyan Cerah Gradient)
            using (var topBrush = new LinearGradientBrush(top, center, Color.FromArgb(56, 189, 248), Color.FromArgb(6, 182, 212)))
            {
                g.FillPolygon(topBrush, [top, topR, center, topL]);
            }

            // Sisi Kiri (Left Face - Royal Blue Gelap)
            using (var leftBrush = new SolidBrush(Color.FromArgb(14, 45, 85)))
            {
                g.FillPolygon(leftBrush, [topL, center, bot, botL]);
            }

            // Sisi Kanan (Right Face - Teal/Cyan Gelap)
            using (var rightBrush = new SolidBrush(Color.FromArgb(8, 75, 110)))
            {
                g.FillPolygon(rightBrush, [center, topR, botR, bot]);
            }

            // Garis Tepi Halus Neon
            using (var borderPen = new Pen(Color.FromArgb(160, 230, 255), 1.2f))
            {
                g.DrawPolygon(borderPen, [top, topR, botR, bot, botL, topL]);
                g.DrawLine(borderPen, center, top);
                g.DrawLine(borderPen, center, botL);
                g.DrawLine(borderPen, center, botR);
            }

            // Titik Inti Bersinar (Core Dot)
            using (var coreBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(coreBrush, cx - 1.5f, cy - 1.5f, 3f, 3f);
            }
        };

        pnlAppLogo.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left) NativeMethods.DragWindow(Handle);
        };
        pnlAppLogo.DoubleClick += (_, _) => ToggleMaximize();

        var lblAppTitle = new Label
        {
            Text = "DiskPulse",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Location = new Point(45, 12)
        };

        // Badge PRO (Mockup Gambar 5)
        var lblProBadge = new Label
        {
            Text = "PRO",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Theme.AccentCyan,
            BackColor = Color.FromArgb(14, 38, 50),
            Padding = new Padding(5, 2, 5, 2),
            AutoSize = true,
            Location = new Point(145, 17)
        };

        // Dropdown Pilihan Drive (Sleek Dark Style)
        _cmbDrives = new DarkComboBox
        {
            Size = new Size(290, 32),
            Location = new Point(205, 11)
        };
        _cmbDrives.SelectedIndexChanged += async (_, _) => await OnDriveSelectionChangedAsync();

        // Vector Refresh Button (Anti-Emoji, Vector Circle Arrow)
        _btnHeaderRefresh = new DarkButton
        {
            Text = "Refresh",
            Variant = ButtonVariant.RefreshHeader,
            Size = new Size(115, 32),
            Location = new Point(505, 11),
            Font = Theme.BodyBold,
            BackColor = Theme.HeaderBackground
        };
        _btnHeaderRefresh.Click += async (_, _) => await RefreshCurrentActiveViewAsync();

        // Badge ADMIN (Mockup Gambar 5: Hijau Emerald Pill)
        bool isAdmin = CheckIfAdministrator();
        _lblAdminBadge = new Label
        {
            Text = isAdmin ? "ADMIN" : "USER",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = isAdmin ? Theme.AccentEmerald : Theme.AccentAmber,
            BackColor = isAdmin ? Color.FromArgb(16, 42, 32) : Color.FromArgb(45, 38, 20),
            Padding = new Padding(8, 4, 8, 4),
            AutoSize = true,
            Location = new Point(630, 12)
        };

        // Kontrol Jendela Modern Terintegrasi (Minimize, Maximize, Close - Mockup Gambar 5)
        _pnlWindowActions = new Panel
        {
            Size = new Size(114, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_pnlHeader.Width - 124, 13),
            BackColor = Color.Transparent
        };

        _btnWinMinimize = new WindowCaptionButton(CaptionButtonType.Minimize)
        {
            Location = new Point(0, 1)
        };
        _btnWinMinimize.Click += (_, _) => WindowState = FormWindowState.Minimized;

        _btnWinMaximize = new WindowCaptionButton(CaptionButtonType.Maximize)
        {
            Location = new Point(35, 1)
        };
        _btnWinMaximize.Click += (_, _) => ToggleMaximize();

        _btnWinClose = new WindowCaptionButton(CaptionButtonType.Close)
        {
            Location = new Point(72, 1)
        };
        _btnWinClose.Click += (_, _) => Close();

        _pnlWindowActions.Controls.AddRange([_btnWinMinimize, _btnWinMaximize, _btnWinClose]);

        // Drag Window secara native dari Header Bar
        _pnlHeader.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.DragWindow(Handle);
            }
        };
        _pnlHeader.DoubleClick += (_, _) => ToggleMaximize();

        lblAppTitle.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.DragWindow(Handle);
            }
        };
        lblAppTitle.DoubleClick += (_, _) => ToggleMaximize();

        _pnlHeader.Resize += (_, _) =>
        {
            _pnlWindowActions.Location = new Point(_pnlHeader.ClientSize.Width - 124, 13);
        };

        _pnlHeader.Controls.AddRange([pnlAppLogo, lblAppTitle, lblProBadge, _cmbDrives, _btnHeaderRefresh, _lblAdminBadge, _pnlWindowActions]);
    }

    private void BuildSidebar()
    {
        _pnlSidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 210,
            BackColor = Theme.SidebarBackground,
            Padding = new Padding(12, 16, 12, 14)
        };

        _pnlSidebar.Paint += (_, pe) =>
        {
            using var bPen = new Pen(Theme.CardBorder, 1f);
            pe.Graphics.DrawLine(bPen, _pnlSidebar.Width - 1, 0, _pnlSidebar.Width - 1, _pnlSidebar.Height);
        };

        var lblWorkspace = new Label
        {
            Text = "WORKSPACE",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(14, 16)
        };

        // Nav Item 1: Junk Cleaner
        _btnNavCleaner = new DarkButton
        {
            Text = "Junk Cleaner",
            NavIcon = NavIconType.TrashCleaner,
            Variant = ButtonVariant.ElevatedNav,
            Size = new Size(186, 38),
            Location = new Point(12, 40),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(36, 0, 0, 0),
            Font = Theme.BodyBold,
            BackColor = Theme.SidebarBackground
        };
        _btnNavCleaner.Click += (_, _) => SwitchTab(0);

        // Nav Item 2: Folder Analyzer
        _btnNavFolders = new DarkButton
        {
            Text = "Folder Analyzer",
            NavIcon = NavIconType.FolderAnalyzer,
            Variant = ButtonVariant.Ghost,
            Size = new Size(186, 38),
            Location = new Point(12, 84),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(36, 0, 0, 0),
            Font = Theme.BodyBold,
            BackColor = Theme.SidebarBackground
        };
        _btnNavFolders.Click += (_, _) => SwitchTab(1);

        // Nav Item 3: App Manager
        _btnNavApps = new DarkButton
        {
            Text = "App Manager",
            NavIcon = NavIconType.AppGrid,
            Variant = ButtonVariant.Ghost,
            Size = new Size(186, 38),
            Location = new Point(12, 128),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(36, 0, 0, 0),
            Font = Theme.BodyBold,
            BackColor = Theme.SidebarBackground
        };
        _btnNavApps.Click += (_, _) => SwitchTab(2);

        // Sidebar Footer: Drive Status + Update DiskPulse Button
        var pnlSidebarFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 126,
            BackColor = Color.Transparent
        };

        _cardSidebarDisk = new DarkCardPanel
        {
            Dock = DockStyle.Top,
            Height = 84,
            CornerRadius = 8f,
            BackColor = Theme.CardBackground,
            Padding = new Padding(12, 10, 12, 10)
        };

        _lblSidebarStatus = new Label
        {
            Text = "Drive Status",
            Font = Theme.SmallFont,
            ForeColor = Theme.TextSecondary,
            Location = new Point(10, 8),
            AutoSize = true
        };

        _lblSidebarPct = new Label
        {
            Text = "84.3%",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Theme.AccentCyan,
            Location = new Point(130, 8),
            AutoSize = true,
            TextAlign = ContentAlignment.TopRight
        };

        _pnlSidebarBarTrack = new Panel
        {
            Location = new Point(10, 32),
            Size = new Size(164, 5),
            BackColor = Color.FromArgb(28, 34, 48)
        };

        _pnlSidebarBarFill = new Panel
        {
            Dock = DockStyle.Left,
            Width = 0,
            BackColor = Theme.AccentCyan
        };
        _pnlSidebarBarTrack.Controls.Add(_pnlSidebarBarFill);

        _lblSidebarStats = new Label
        {
            Text = "- / - Free",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
            ForeColor = Theme.TextMuted,
            Location = new Point(10, 46),
            AutoSize = true
        };

        _cardSidebarDisk.Controls.AddRange([_lblSidebarStatus, _lblSidebarPct, _pnlSidebarBarTrack, _lblSidebarStats]);

        // Update DiskPulse Button
        _btnUpdateApp = new DarkButton
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Text = "Update DiskPulse",
            NavIcon = NavIconType.UpdateSpark,
            Variant = ButtonVariant.ElevatedNav,
            Font = Theme.SmallBold,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(34, 0, 0, 0),
            BackColor = Theme.SidebarBackground,
            Cursor = Cursors.Hand
        };
        _btnUpdateApp.Click += async (_, _) => await CheckAndApplyUpdateAsync();

        pnlSidebarFooter.Controls.AddRange([_cardSidebarDisk, _btnUpdateApp]);

        _pnlSidebar.Controls.AddRange([lblWorkspace, _btnNavCleaner, _btnNavFolders, _btnNavApps, pnlSidebarFooter]);
    }

    private void BuildDiskCard()
    {
        _cardDisk = new DarkCardPanel
        {
            Dock = DockStyle.Top,
            Height = 180,
            CornerRadius = 12f,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(4),
            BackColor = Theme.CardBackground
        };

        _ringMeter = new ModernRingMeter
        {
            Dock = DockStyle.Fill
        };

        _cardDisk.Controls.Add(_ringMeter);
    }

    private void BuildMainCanvas()
    {
        _pnlMainCanvas = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(16, 12, 18, 12)
        };

        _pnlWorkspace = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background
        };

        _pnlTransitionOverlay = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Visible = false,
            Enabled = false
        };
        typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_pnlTransitionOverlay, true, null);
        _pnlTransitionOverlay.Paint += (_, pe) =>
        {
            if (_transitionSnapshot != null && _transitionAlpha > 0.01f)
            {
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = Math.Clamp(_transitionAlpha, 0f, 1f) };
                using var ia = new System.Drawing.Imaging.ImageAttributes();
                ia.SetColorMatrix(cm);
                pe.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                pe.Graphics.DrawImage(_transitionSnapshot,
                    new Rectangle(0, 0, _pnlTransitionOverlay.Width, _pnlTransitionOverlay.Height),
                    0, 0, _transitionSnapshot.Width, _transitionSnapshot.Height,
                    GraphicsUnit.Pixel, ia);
            }
        };

        _pnlWorkspace.Controls.Add(_pnlTransitionOverlay);
        _pnlWorkspace.Controls.Add(_pnlContentApps);
        _pnlWorkspace.Controls.Add(_pnlContentFolders);
        _pnlWorkspace.Controls.Add(_pnlContentCleaner);

        _pnlMainCanvas.Controls.Add(_pnlWorkspace);
    }

    private void BuildCleanerTab()
    {
        _pnlContentCleaner = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            BackColor = Theme.Background
        };

        // Kartu Aksi Bawah (Mockup Gambar 5: Compact, Sleek, Glowing Button)
        _cardActionsCleaner = new DarkCardPanel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            Padding = new Padding(16, 10, 16, 10),
            CornerRadius = 10f,
            BackColor = Theme.CardBackground
        };

        _chkDryRun = new CheckBox
        {
            Text = "Simulation Mode (Dry Run)",
            Font = Theme.BodyBold,
            ForeColor = Theme.TextSecondary,
            AutoSize = true,
            Checked = false,
            Cursor = Cursors.Hand,
            Location = new Point(14, 16)
        };

        _btnScan = new DarkButton
        {
            Text = "Scan Again",
            Variant = ButtonVariant.Secondary,
            Size = new Size(115, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardActionsCleaner.Width - 365, 14),
            BackColor = Theme.CardBackground
        };
        _btnScan.Click += async (_, _) => await ScanSelectedCategoriesAsync();

        // Clean Now Button with Cyan Glowing Gradient
        _btnClean = new DarkButton
        {
            Text = "Clean Now",
            Variant = ButtonVariant.GradientCyan,
            Size = new Size(220, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardActionsCleaner.Width - 240, 14),
            BackColor = Theme.CardBackground
        };
        _btnClean.Click += async (_, _) => await ExecuteCleaningAsync();

        _btnCancel = new DarkButton
        {
            Text = "Cancel",
            Variant = ButtonVariant.Danger,
            Size = new Size(85, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardActionsCleaner.Width - 460, 14),
            Visible = false,
            BackColor = Theme.CardBackground
        };
        _btnCancel.Click += (_, _) => CancelCurrentOperation();

        _btnToggleLog = new DarkButton
        {
            Text = "Log ▲",
            Variant = ButtonVariant.Ghost,
            Size = new Size(70, 36),
            Font = Theme.SmallFont,
            Location = new Point(230, 14),
            BackColor = Theme.CardBackground
        };
        _btnToggleLog.Click += (_, _) => ToggleLogDrawer();

        _progressBar = new ProgressBar
        {
            Location = new Point(14, 45),
            Size = new Size(270, 4),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25,
            Visible = false
        };

        _lblCleanStatus = new Label
        {
            Text = "Ready to scan system junk files.",
            Font = Theme.SmallFont,
            ForeColor = Theme.AccentCyan,
            Location = new Point(14, 51),
            AutoSize = true,
            Visible = false
        };

        _cardActionsCleaner.Controls.AddRange([
            _chkDryRun,
            _btnScan,
            _btnClean,
            _btnCancel,
            _btnToggleLog,
            _progressBar,
            _lblCleanStatus
        ]);

        // Category Table Card
        _cardTableCleaner = new DarkCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(6),
            CornerRadius = 12f,
            BackColor = Theme.CardBackground
        };

        // Header: Storage Optimization + Badge + Select All / Deselect All
        _pnlCleanerHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(14, 6, 14, 6),
            BackColor = Theme.CardBackground
        };
        _pnlCleanerHeader.Paint += (_, pe) =>
        {
            using var bPen = new Pen(Theme.CardBorder, 1f);
            pe.Graphics.DrawLine(bPen, 0, _pnlCleanerHeader.Height - 1, _pnlCleanerHeader.Width, _pnlCleanerHeader.Height - 1);
        };

        _lblCleanerTitle = new Label
        {
            Text = "Storage Optimization",
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            Location = new Point(10, 8),
            AutoSize = true
        };

        _lblCleanerBadge = new Label
        {
            Text = "Ready to Scan",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Theme.AccentCyan,
            BackColor = Color.FromArgb(20, 36, 48),
            Padding = new Padding(8, 3, 8, 3),
            AutoSize = true,
            Location = new Point(190, 8)
        };

        _lblCleanerSub = new Label
        {
            Text = "Clean temporary files, browser cache, and Windows Update staging without affecting system stability.",
            Font = Theme.SmallFont,
            ForeColor = Theme.TextMuted,
            Location = new Point(10, 30),
            AutoSize = true
        };

        _btnSelectAll = new DarkButton
        {
            Text = "Select All",
            Variant = ButtonVariant.Ghost,
            Size = new Size(86, 26),
            Font = Theme.SmallFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardTableCleaner.Width - 190, 8),
            BackColor = Theme.CardBackground
        };
        _btnSelectAll.Click += (_, _) => SetAllCategoriesCheck(true);

        var lblDotSep = new Label
        {
            Text = "•",
            Font = Theme.SmallFont,
            ForeColor = Theme.TextMuted,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardTableCleaner.Width - 100, 12),
            AutoSize = true
        };

        _btnUnselectAll = new DarkButton
        {
            Text = "Deselect All",
            Variant = ButtonVariant.Ghost,
            Size = new Size(86, 26),
            Font = Theme.SmallFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardTableCleaner.Width - 88, 8),
            BackColor = Theme.CardBackground
        };
        _btnUnselectAll.Click += (_, _) => SetAllCategoriesCheck(false);

        _pnlCleanerHeader.Resize += (_, _) =>
        {
            _btnUnselectAll.Location = new Point(_pnlCleanerHeader.ClientSize.Width - 94, 8);
            lblDotSep.Location = new Point(_pnlCleanerHeader.ClientSize.Width - 104, 12);
            _btnSelectAll.Location = new Point(_pnlCleanerHeader.ClientSize.Width - 194, 8);
        };

        _pnlCleanerHeader.Controls.AddRange([_lblCleanerTitle, _lblCleanerBadge, _lblCleanerSub, _btnSelectAll, lblDotSep, _btnUnselectAll]);

        _lvCategories = new DarkListView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = true,
            Font = Theme.BodyFont
        };

        // Row height 48px via ImageList
        var rowImgList = new ImageList { ImageSize = new Size(1, 48) };
        _lvCategories.SmallImageList = rowImgList;

        _lvCategories.Columns.Add("Select", 44, HorizontalAlignment.Center);
        _lvCategories.Columns.Add("Junk Category", 360, HorizontalAlignment.Left);
        _lvCategories.Columns.Add("Count", 105, HorizontalAlignment.Right);
        _lvCategories.Columns.Add("Size", 125, HorizontalAlignment.Right);
        _lvCategories.FlexibleColumnIndex = 1;

        _lvCategories.ItemChecked += LvCategories_ItemChecked;

        // KUNCI PERBAIKAN DOCKING: _pnlCleanerHeader (Dock=Top) HARUS di-SendToBack()
        // agar mereservasi area atas terlebih dahulu, sehingga _lvCategories (Dock=Fill)
        // mulai tepat di bawahnya tanpa menutupi baris nomor 1!
        _cardTableCleaner.Controls.Add(_pnlCleanerHeader);
        _cardTableCleaner.Controls.Add(_lvCategories);
        _pnlCleanerHeader.SendToBack();
        _lvCategories.BringToFront();

        var diskSpacer = new Panel
        {
            Dock = DockStyle.Top,
            Height = 10,
            BackColor = Theme.Background
        };

        _pnlContentCleaner.Controls.Add(_cardTableCleaner);
        _pnlContentCleaner.Controls.Add(_cardActionsCleaner);
        _pnlContentCleaner.Controls.Add(diskSpacer);
        _pnlContentCleaner.Controls.Add(_cardDisk);
        _cardDisk.SendToBack();
        diskSpacer.SendToBack();
        _cardActionsCleaner.SendToBack();
        _cardTableCleaner.BringToFront();
    }

    private void BuildAppsTab()
    {
        _pnlContentApps = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            BackColor = Theme.Background,
            Visible = false
        };

        _cardTableApps = new DarkCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(8),
            CornerRadius = 12f,
            BackColor = Theme.CardBackground
        };

        var pnlAppsHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            Padding = new Padding(14, 8, 14, 8),
            BackColor = Theme.CardBackground
        };
        pnlAppsHeader.Paint += (_, pe) =>
        {
            using var bPen = new Pen(Theme.CardBorder, 1f);
            pe.Graphics.DrawLine(bPen, 0, pnlAppsHeader.Height - 1, pnlAppsHeader.Width, pnlAppsHeader.Height - 1);
        };

        _lblAppsTitle = new Label
        {
            Text = "Application Management & Uninstaller",
            UseMnemonic = false,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Theme.TextPrimary,
            Location = new Point(10, 10),
            AutoSize = true
        };

        _lblAppsBadge = new Label
        {
            Text = "Loading...",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Theme.AccentCyan,
            BackColor = Color.FromArgb(20, 36, 48),
            Padding = new Padding(8, 3, 8, 3),
            AutoSize = true,
            Location = new Point(410, 10)
        };

        _btnOpenWindowsApps = new DarkButton
        {
            Text = "⚙  Windows Settings",
            Variant = ButtonVariant.Secondary,
            Size = new Size(165, 28),
            Font = Theme.SmallFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardTableApps.Width - 175, 8),
            BackColor = Theme.CardBackground
        };
        _btnOpenWindowsApps.Click += (_, _) => AppUninstallerService.OpenWindowsInstalledApps();

        _btnCheckAppUpdates = new DarkButton
        {
            Text = "⚡ Check Updates",
            Variant = ButtonVariant.Secondary,
            Size = new Size(140, 28),
            Font = Theme.SmallFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(_cardTableApps.Width - 175 - 148, 8),
            BackColor = Theme.CardBackground
        };
        _btnCheckAppUpdates.Click += async (_, _) => await CheckAppUpdatesAsync(showPrompt: true);

        _lblAppsSub = new Label
        {
            Text = "Uninstall heavy applications or upgrade out-of-date packages using official installers via Windows Package Manager.",
            Font = Theme.SmallFont,
            ForeColor = Theme.TextMuted,
            Location = new Point(10, 38),
            AutoSize = true,
            AutoEllipsis = true
        };

        pnlAppsHeader.Controls.AddRange([_lblAppsTitle, _lblAppsBadge, _lblAppsSub, _btnCheckAppUpdates, _btnOpenWindowsApps]);

        pnlAppsHeader.Resize += (_, _) =>
        {
            _btnOpenWindowsApps.Location = new Point(pnlAppsHeader.ClientSize.Width - 175, 8);
            _btnCheckAppUpdates.Location = new Point(pnlAppsHeader.ClientSize.Width - 175 - 148, 8);
            _lblAppsBadge.Location = new Point(_lblAppsTitle.Right + 12, 10);
            _lblAppsSub.MaximumSize = new Size(Math.Max(100, _btnCheckAppUpdates.Left - 20), 0);
        };

        _gridApps = new DarkAppGridControl
        {
            Dock = DockStyle.Fill
        };
        _gridApps.UninstallRequested += OnUninstallAppClicked;
        _gridApps.UpdateRequested += OnUpdateAppClicked;

        // KUNCI PERBAIKAN DOCKING: pnlAppsHeader (Dock=Top) HARUS di-SendToBack()
        // agar mereservasi area atas terlebih dahulu, sehingga _gridApps (Dock=Fill)
        // mulai tepat di bawahnya tanpa menutupi baris kartu aplikasi nomor 1!
        _cardTableApps.Controls.Add(pnlAppsHeader);
        _cardTableApps.Controls.Add(_gridApps);
        pnlAppsHeader.SendToBack();
        _gridApps.BringToFront();

        _pnlContentApps.Controls.Add(_cardTableApps);
    }

    private void BuildFoldersTab()
    {
        _pnlContentFolders = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            BackColor = Theme.Background,
            Visible = false
        };

        _cardTableFolders = new DarkCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(4),
            CornerRadius = 12f,
            BackColor = Theme.CardBackground
        };

        // Navigation header bar (Breadcrumb & Tombol Kembali & Total Summary & Pindai Drive)
        _pnlFolderNav = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(8, 4, 8, 4),
            BackColor = Theme.CardBackground
        };

        _pnlFolderNav.Paint += (_, pe) =>
        {
            using var borderPen = new Pen(Theme.CardBorder, 1f);
            pe.Graphics.DrawLine(borderPen, 0, _pnlFolderNav.Height - 1, _pnlFolderNav.Width, _pnlFolderNav.Height - 1);
        };

        _btnBackFolder = new DarkButton
        {
            Text = "‹",
            Variant = ButtonVariant.Secondary,
            Size = new Size(28, 28),
            Location = new Point(10, 10),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Visible = false,
            BackColor = Theme.CardBackground
        };
        _btnBackFolder.Click += async (_, _) => await NavigateBackFolderAsync();

        _pnlBreadcrumb = new FlowLayoutPanel
        {
            Location = new Point(10, 8),
            Size = new Size(Math.Max(50, _cardTableFolders.Width - 360), 32),
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.Transparent
        };

        _lblFolderSummary = new FolderSummaryBadge
        {
            Size = new Size(230, 26),
            Location = new Point(500, 11)
        };

        _btnScanWholeDrive = new DarkButton
        {
            Text = "Scan Whole Drive",
            Variant = ButtonVariant.Secondary,
            Size = new Size(155, 30),
            Font = Theme.SmallBold,
            Location = new Point(700, 9),
            BackColor = Theme.CardBackground
        };
        _btnScanWholeDrive.Click += async (_, _) => await AnalyzeFoldersAsync();
        _btnAnalyzeFolders = _btnScanWholeDrive;

        _pnlFolderNav.Controls.AddRange([_btnBackFolder, _pnlBreadcrumb, _lblFolderSummary, _btnScanWholeDrive]);

        void UpdateFolderNavLayout()
        {
            if (_pnlFolderNav == null || _btnScanWholeDrive == null || _lblFolderSummary == null || _pnlBreadcrumb == null || _btnBackFolder == null) return;
            _btnScanWholeDrive.Location = new Point(_pnlFolderNav.ClientSize.Width - 165, 9);
            _lblFolderSummary.Location = new Point(_btnScanWholeDrive.Left - _lblFolderSummary.Width - 16, 11);
            int startX = _btnBackFolder.Visible ? 44 : 10;
            _pnlBreadcrumb.Location = new Point(startX, 8);
            _pnlBreadcrumb.Width = Math.Max(50, _lblFolderSummary.Left - startX - 10);
            if (_btnBackFolder.Visible)
            {
                _btnBackFolder.BringToFront();
            }
        }

        _pnlFolderNav.Resize += (_, _) => UpdateFolderNavLayout();
        _btnBackFolder.VisibleChanged += (_, _) => UpdateFolderNavLayout();


        _lvFolders = new DarkListView
        {
            Dock = DockStyle.Fill,
            CheckBoxes = false,
            Font = Theme.BodyFont
        };

        var folderImgList = new ImageList { ImageSize = new Size(1, 52) };
        _lvFolders.SmallImageList = folderImgList;

        ConfigureFolderColumns();

        _lvFolders.DoubleClick += async (_, _) => await OnFolderListViewDoubleClickAsync();
        _lvFolders.RowActionClicked += (_, actionIdx) => OnFolderRowActionClicked(_, actionIdx);
        BuildContextMenu();

        // Overlay Panel for Loading State (Scanning) and Empty State
        _pnlFolderOverlay = new DarkOverlayPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 15, 26),
            Visible = true
        };

        _spinnerFolder = new DarkSpinner
        {
            Size = new Size(52, 52),
            SpinnerColor = Theme.AccentCyan,
            Visible = false
        };

        _lblFolderOverlayTitle = new Label
        {
            Text = "Folder Analysis Not Started",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            AutoSize = true,
            BackColor = Color.Transparent
        };

        _lblFolderOverlayStatus = new Label
        {
            Text = "Scan all drive directories to map the largest folders,\nuncover hidden cache files, and optimize storage usage.",
            Font = Theme.BodyFont,
            ForeColor = Color.FromArgb(148, 163, 184),
            TextAlign = ContentAlignment.TopCenter,
            Size = new Size(540, 48),
            BackColor = Color.Transparent
        };

        _btnFolderOverlayAction = new DarkButton
        {
            Text = "⚡ Start Whole Drive Scan",
            Variant = ButtonVariant.GradientCyan,
            Size = new Size(230, 36),
            Font = Theme.BodyBold,
            BackColor = Theme.CardBackground
        };
        _btnFolderOverlayAction.Click += async (_, _) =>
        {
            if (_btnFolderOverlayAction.Text.Contains("Cancel") || _btnFolderOverlayAction.Text.Contains("Batal"))
            {
                CancelCurrentOperation();
            }
            else
            {
                await AnalyzeFoldersAsync();
            }
        };

        _pnlFolderOverlay.Controls.AddRange([_spinnerFolder, _lblFolderOverlayTitle, _lblFolderOverlayStatus, _btnFolderOverlayAction]);

        _pnlFolderOverlay.Resize += (_, _) =>
        {
            CenterFolderOverlayControls();
        };

        _pnlFolderOverlay.Paint += (_, pe) =>
        {
            if (!_pnlFolderOverlay.IsScanning && !_isScanningFolders && !_isInDetailView && _cachedDriveFolders.Count == 0)
            {

                var g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int cx = _pnlFolderOverlay.ClientSize.Width / 2;
                int iconY = _lblFolderOverlayTitle.Top - 54;
                int iconW = 44, iconH = 32;
                int x = cx - iconW / 2;

                using (var auraBrush = new SolidBrush(Color.FromArgb(18, 0, 242, 254)))
                {
                    g.FillEllipse(auraBrush, cx - 36, iconY - 8, 72, 48);
                }

                using var folderPen = new Pen(Theme.AccentCyan, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                PointF[] pts =
                [
                    new PointF(x, iconY + iconH),
                    new PointF(x, iconY + 4),
                    new PointF(x + 14, iconY + 4),
                    new PointF(x + 20, iconY + 10),
                    new PointF(x + iconW, iconY + 10),
                    new PointF(x + iconW, iconY + iconH)
                ];
                g.DrawPolygon(folderPen, pts);
                g.DrawLine(folderPen, x, iconY + 14, x + iconW, iconY + 14);

                using var lensPen = new Pen(Color.FromArgb(240, 245, 255), 1.6f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawEllipse(lensPen, x + iconW - 13, iconY + iconH - 12, 10, 10);
                g.DrawLine(lensPen, x + iconW - 5, iconY + iconH - 4, x + iconW + 1, iconY + iconH + 2);
            }
        };

        _cardTableFolders.Controls.Add(_pnlFolderNav);
        _cardTableFolders.Controls.Add(_pnlFolderOverlay);
        _cardTableFolders.Controls.Add(_lvFolders);

        _pnlFolderNav.SendToBack();
        _pnlFolderOverlay.BringToFront();

        _pnlContentFolders.Controls.Add(_cardTableFolders);
        _cardTableFolders.BringToFront();
    }

    private void BuildLogDrawer()
    {
        _cardLogDrawer = new DarkCardPanel
        {
            Dock = DockStyle.Bottom,
            Height = 110,
            Padding = new Padding(12),
            BackColor = Theme.CardBackground,
            Visible = false
        };

        _txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.ControlBg,
            ForeColor = Color.FromArgb(180, 220, 255),
            Font = Theme.MonospaceFont,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill
        };

        _cardLogDrawer.Controls.Add(_txtLog);
    }

    private void ToggleLogDrawer()
    {
        _isLogExpanded = !_isLogExpanded;
        _cardLogDrawer.Visible = _isLogExpanded;
        _btnToggleLog.Text = _isLogExpanded ? "Hide Log ▼" : "Show Log ▲";
    }

    private void SwitchTab(bool isCleaner) => SwitchTab(isCleaner ? 0 : 1);

    private void SwitchTab(int tabIndex)
    {
        if (tabIndex == _currentTabIndex) return;

        bool canAnimate = _currentTabIndex >= 0 && _pnlWorkspace.Width > 0 && _pnlWorkspace.Height > 0;

        _transitionTimer?.Stop();

        if (canAnimate)
        {
            try
            {
                _transitionSnapshot?.Dispose();
                _transitionSnapshot = new Bitmap(_pnlWorkspace.Width, _pnlWorkspace.Height);
                _pnlWorkspace.DrawToBitmap(_transitionSnapshot, new Rectangle(0, 0, _pnlWorkspace.Width, _pnlWorkspace.Height));
                _transitionAlpha = 1.0f;
                _pnlTransitionOverlay.BringToFront();
                _pnlTransitionOverlay.Visible = true;
            }
            catch
            {
                _transitionSnapshot = null;
                _pnlTransitionOverlay.Visible = false;
            }
        }

        _pnlContentCleaner.Visible = (tabIndex == 0);
        _pnlContentFolders.Visible = (tabIndex == 1);
        _pnlContentApps.Visible = (tabIndex == 2);
        _currentTabIndex = tabIndex;

        _btnNavCleaner.Variant = (tabIndex == 0) ? ButtonVariant.ElevatedNav : ButtonVariant.Ghost;
        _btnNavFolders.Variant = (tabIndex == 1) ? ButtonVariant.ElevatedNav : ButtonVariant.Ghost;
        _btnNavApps.Variant = (tabIndex == 2) ? ButtonVariant.ElevatedNav : ButtonVariant.Ghost;

        if (tabIndex == 0)
        {
            _lvCategories.AutoFitFlexibleColumn();
        }
        else if (tabIndex == 1)
        {
            if (_isScanningFolders)
            {
                // Tetap menampilkan scanning overlay
            }
            else if (_cachedDriveFolders.Count == 0 && !_isInDetailView)
            {
                ShowFolderEmptyState();
            }
            else
            {
                HideFolderOverlay();
            }
            _lvFolders.AutoFitFlexibleColumn();
        }
        else if (tabIndex == 2)
        {
            LoadInstalledAppsView();
        }

        if (canAnimate && _transitionSnapshot != null)
        {
            _transitionTimer ??= new System.Windows.Forms.Timer { Interval = 16 };
            _transitionTimer.Tick -= OnTransitionTimerTick;
            _transitionTimer.Tick += OnTransitionTimerTick;
            _transitionTimer.Start();
        }
    }

    private void OnTransitionTimerTick(object? sender, EventArgs e)
    {
        _transitionAlpha -= 0.18f;
        if (_transitionAlpha <= 0.02f)
        {
            _transitionTimer?.Stop();
            _pnlTransitionOverlay.Visible = false;
            _transitionSnapshot?.Dispose();
            _transitionSnapshot = null;
        }
        else
        {
            _pnlTransitionOverlay.Invalidate();
        }
    }

    private bool _isLoadingApps;

    private async void LoadInstalledAppsView(bool forceReload = false)
    {
        if (_isLoadingApps) return;
        if (_installedApps.Count > 0 && !forceReload) return;

        _isLoadingApps = true;
        _lblAppsBadge.Text = "Scanning Registry...";
        _lblAppsBadge.ForeColor = Theme.AccentCyan;
        _gridApps.SetLoading(true, "Scanning Installed Applications...", "Reading Windows Registry (32-bit & 64-bit), calculating install sizes, and loading icons...");

        try
        {
            var apps = await Task.Run(() => _uninstallerService.GetInstalledApps(null));
            _installedApps = apps;
            _lblAppsBadge.Text = $"{_installedApps.Count} Applications Detected";
            _lblAppsBadge.ForeColor = Theme.AccentEmerald;
            _gridApps.SetApps(_installedApps);
            _ = CheckAppUpdatesAsync(showPrompt: false);
        }
        catch (Exception ex)
        {
            Log($"[Apps] Failed to scan installed applications: {ex.Message}");
            _lblAppsBadge.Text = "Scan Failed";
            _lblAppsBadge.ForeColor = Theme.AccentRose;
            _gridApps.SetApps([]);
        }
        finally
        {
            _isLoadingApps = false;
            _gridApps.SetLoading(false);
        }
    }

    private async Task CheckAppUpdatesAsync(bool showPrompt)
    {
        if (_isCheckingUpdates || _installedApps.Count == 0) return;

        if (!AppUpdaterService.IsInternetAvailable())
        {
            Log("[Apps] Internet connection is unavailable. Cannot check for updates.");
            if (showPrompt)
            {
                MessageBox.Show(
                    "No active internet connection detected.\n\nPlease connect to the internet to check and download official application updates.",
                    "Offline Mode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }

        _isCheckingUpdates = true;
        _btnCheckAppUpdates.Text = "⚡ Checking...";
        _btnCheckAppUpdates.Enabled = false;
        Log("[Apps] Checking online repositories for application updates (via winget)...");

        try
        {
            var upgrades = await Task.Run(() => AppUpdaterService.CheckAvailableUpgradesAsync());
            int matched = AppUpdaterService.MatchUpgrades(_installedApps, upgrades);

            if (matched > 0)
            {
                _lblAppsBadge.Text = $"{_installedApps.Count} Apps ({matched} Updates Available)";
                _lblAppsBadge.ForeColor = Theme.AccentCyan;
                Log($"[Apps] Update scan completed: {matched} application(s) have new official versions available.");
            }
            else
            {
                _lblAppsBadge.Text = $"{_installedApps.Count} Applications Detected";
                _lblAppsBadge.ForeColor = Theme.AccentEmerald;
                Log("[Apps] All detected applications are up to date.");
                if (showPrompt)
                {
                    MessageBox.Show(
                        "All installed applications recognized by Windows Package Manager are currently up to date!",
                        "Applications Up to Date",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }

            _gridApps.Invalidate();
        }
        catch (Exception ex)
        {
            Log($"[Apps] Failed to check for updates: {ex.Message}");
            if (showPrompt)
            {
                MessageBox.Show($"Failed to query update repositories: {ex.Message}", "Update Check Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _isCheckingUpdates = false;
            _btnCheckAppUpdates.Text = "⚡ Check Updates";
            _btnCheckAppUpdates.Enabled = true;
        }
    }

    private async void OnUpdateAppClicked(AppUninstallInfo app)
    {
        if (app.IsUpdating) return;

        if (!AppUpdaterService.IsInternetAvailable())
        {
            MessageBox.Show(
                "An active internet connection is required to download and install application updates.\nPlease check your network connection and try again.",
                "Internet Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Do you want to download and install the official update for:\n\n" +
            $"• Application: {app.DisplayName}\n" +
            $"• Current Version: {(!string.IsNullOrEmpty(app.DisplayVersion) ? app.DisplayVersion : "Installed")}\n" +
            $"• Available Version: v{app.AvailableVersion}\n" +
            $"• Source: Windows Package Manager (winget)\n\n" +
            $"The update will download and install silently in the background.",
            "Confirm Application Update",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        Log($"[Apps] Starting in-app silent update for: {app.DisplayName} (Target: v{app.AvailableVersion})...");

        app.IsUpdating = true;
        app.UpdateStatusText = "Downloading...";
        _gridApps.NotifyUpdatingStateChanged();

        bool success = await Task.Run(() => AppUpdaterService.UpgradeAppSilentAsync(app, status =>
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() =>
                    {
                        app.UpdateStatusText = status;
                        _gridApps.Invalidate();
                    });
                }
                else
                {
                    app.UpdateStatusText = status;
                    _gridApps.Invalidate();
                }
            }
            catch
            {
                // Mengabaikan pemanggilan jika form ditutup
            }
        }));

        if (success)
        {
            Log($"[Apps] Successfully updated '{app.DisplayName}' to v{app.DisplayVersion}!");
            app.UpdateStatusText = "Updated!";
            _gridApps.Invalidate();

            // Perbarui badge jumlah update yang tersisa
            int remainingUpdates = 0;
            foreach (var a in _installedApps)
            {
                if (a != app && a.HasUpdate) remainingUpdates++;
            }
            _lblAppsBadge.Text = remainingUpdates > 0
                ? $"{_installedApps.Count} Apps ({remainingUpdates} Updates Available)"
                : $"{_installedApps.Count} Applications Detected";
            _lblAppsBadge.ForeColor = remainingUpdates > 0 ? Theme.AccentCyan : Theme.AccentEmerald;

            // Transisi halus: setelah 2.5 detik, bersihkan status update dan sembunyikan tombol update
            await Task.Delay(2500);
            app.IsUpdating = false;
            app.AvailableVersion = null;
            _gridApps.NotifyUpdatingStateChanged();
        }
        else
        {
            Log($"[Apps] Update failed for '{app.DisplayName}'.");
            app.UpdateStatusText = "Failed";
            _gridApps.Invalidate();

            await Task.Delay(3500);
            app.IsUpdating = false;
            _gridApps.NotifyUpdatingStateChanged();
        }
    }

    private void OnUninstallAppClicked(AppUninstallInfo app)
    {
        var confirm = MessageBox.Show(
            $"Do you want to run the official uninstaller for:\n\n" +
            $"• {app.DisplayName}\n" +
            $"• Size: {app.FormattedSize}\n" +
            (string.IsNullOrEmpty(app.Publisher) ? "" : $"• Publisher: {app.Publisher}\n") +
            (string.IsNullOrEmpty(app.InstallLocation) ? "" : $"• Location: {app.InstallLocation}\n") +
            $"\nProceed with uninstallation?",
            "Confirm Application Uninstall",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        Log($"[Apps] Launching uninstaller for: {app.DisplayName}...");
        if (_uninstallerService.LaunchUninstall(app, out string err))
        {
            Log($"[Apps] Uninstaller for '{app.DisplayName}' started successfully.");
        }
        else
        {
            Log($"[Apps] Failed to trigger uninstaller: {err}");
            MessageBox.Show($"Failed to launch app uninstaller: {err}\nDiskPulse will open Windows Settings.", "Uninstall Application", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AppUninstallerService.OpenWindowsInstalledApps();
        }
    }


    private void UpdateSidebarDiskInfo()
    {
        if (_currentDiskUsage == null) return;
        if (_lblSidebarStatus != null) _lblSidebarStatus.Text = $"Drive {_currentDiskUsage.DriveName} Status";
        if (_lblSidebarPct != null) _lblSidebarPct.Text = $"{_currentDiskUsage.UsedPercentage:F1}%";
        if (_lblSidebarStats != null) _lblSidebarStats.Text = $"{_currentDiskUsage.FormattedUsed} • {_currentDiskUsage.FormattedFree} Free";
        if (_pnlSidebarBarTrack != null && _pnlSidebarBarFill != null)
        {
            int trackW = _pnlSidebarBarTrack.Width;
            int fillW = Math.Clamp((int)(trackW * (_currentDiskUsage.UsedPercentage / 100.0)), 2, trackW);
            _pnlSidebarBarFill.Width = fillW;
        }
    }

    private void LoadAvailableDrives(string? targetDriveToPreserve = null)
    {
        string target = targetDriveToPreserve ?? _selectedDrive;
        _availableDrives = _diskMonitor.GetAvailableDrives();
        _cmbDrives.Items.Clear();

        int selectedIndex = 0;
        for (int i = 0; i < _availableDrives.Count; i++)
        {
            var drive = _availableDrives[i];
            string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local" : drive.VolumeLabel;
            string itemText = $"{drive.DriveName} [{label}] - {drive.FormattedFree} free / {drive.FormattedTotal}";
            _cmbDrives.Items.Add(itemText);

            if (string.Equals(drive.DriveName, target, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
            }
        }

        if (_cmbDrives.Items.Count > 0)
        {
            _cmbDrives.SelectedIndex = selectedIndex;
        }
    }

    private async Task OnDriveSelectionChangedAsync()
    {
        if (_suppressDriveSelectionChanged) return;

        int idx = _cmbDrives.SelectedIndex;
        if (idx < 0 || idx >= _availableDrives.Count) return;

        try
        {
            var drive = _availableDrives[idx];
            _selectedDrive = drive.DriveName;
            _currentDiskUsage = _diskMonitor.GetDriveUsage(_selectedDrive);
            _ringMeter.SetDiskUsage(_currentDiskUsage);
            UpdateSidebarDiskInfo();
            _btnAnalyzeFolders.Text = "Scan Whole Drive";
            _cachedDriveFolders.Clear();

            _folderNavHistory.Clear();
            _isInDetailView = false;
            _currentInspectedPath = null;
            _btnBackFolder.Visible = false;
            UpdateFolderNavBreadcrumbs(null, 0, 0);
            ConfigureFolderColumnsForRoot();
            _lvFolders.Items.Clear();

            // Load cleaning categories specific to the selected drive
            _categories = _junkScanner.GetCategoriesForDrive(_selectedDrive);
            UpdateCategoryListView();
            _lvCategories.AutoFitFlexibleColumn();

            await ScanSelectedCategoriesAsync();
            _lvCategories.AutoFitFlexibleColumn();
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to load drive {_selectedDrive}: {ex.Message}");
        }
    }

    private void RefreshDiskInfo()
    {
        _currentDiskUsage = _diskMonitor.GetDriveUsage(_selectedDrive);
        _ringMeter.SetDiskUsage(_currentDiskUsage);
        UpdateSidebarDiskInfo();
    }

    private void UpdateCategoryListView()
    {
        _lvCategories.BeginUpdate();
        _lvCategories.Items.Clear();

        foreach (var cat in _categories)
        {
            var item = new ListViewItem(string.Empty) { Checked = cat.IsSelected, Tag = cat };
            item.SubItems.Add(cat.Name);
            item.SubItems.Add(cat.FileCount > 0 ? $"{cat.FileCount:N0} files" : "-");
            item.SubItems.Add(cat.TotalSizeBytes > 0 ? cat.FormattedSize : "0 B");
            _lvCategories.Items.Add(item);
        }

        _lvCategories.EndUpdate();
        _lvCategories.AutoFitFlexibleColumn();
        UpdateCleanButtonState();
    }

    private void LvCategories_ItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (e.Item?.Tag is JunkItemCategory cat)
        {
            cat.IsSelected = e.Item.Checked;
            UpdateCleanButtonState();
        }
    }

    private void SetAllCategoriesCheck(bool isChecked)
    {
        _lvCategories.BeginUpdate();
        foreach (ListViewItem item in _lvCategories.Items)
        {
            item.Checked = isChecked;
            if (item.Tag is JunkItemCategory cat) cat.IsSelected = isChecked;
        }
        _lvCategories.EndUpdate();
        UpdateCleanButtonState();
    }

    private void UpdateCleanButtonState()
    {
        long totalBytes = _categories.Where(c => c.IsSelected).Sum(c => c.TotalSizeBytes);
        _btnClean.Text = totalBytes > 0
            ? $"Clean ({DiskUsageInfo.FormatBytes(totalBytes)})"
            : "Clean Now";

        if (_lblCleanerBadge != null)
        {
            long allBytes = _categories.Sum(c => c.TotalSizeBytes);
            _lblCleanerBadge.Text = allBytes > 0 ? $"{DiskUsageInfo.FormatBytes(allBytes)} Ready to Free" : "Ready to Scan";
            _lblCleanerBadge.ForeColor = allBytes > 0 ? Theme.AccentEmerald : Theme.AccentCyan;
            _lblCleanerBadge.BackColor = allBytes > 0 ? Color.FromArgb(20, 42, 34) : Color.FromArgb(20, 36, 48);
        }
    }

    private async Task ScanSelectedCategoriesAsync()
    {
        SetBusyState(true, $"Scanning junk on drive {_selectedDrive}...");
        _cts = new CancellationTokenSource();

        try
        {
            Log($"=== Starting Junk Scan on Drive {_selectedDrive} ===");
            var progress = new Progress<string>(msg => _lblCleanStatus.Text = msg);

            foreach (var category in _categories)
            {
                if (_cts.IsCancellationRequested) break;
                await _junkScanner.ScanCategoryAsync(category, _selectedDrive, progress, _cts.Token);
                Log($"[{category.Name}] Found: {category.FileCount:N0} files ({category.FormattedSize})");
            }

            UpdateCategoryListView();
            RefreshDiskInfo();

            long totalJunk = _categories.Sum(c => c.TotalSizeBytes);
            int totalFiles = _categories.Sum(c => c.FileCount);
            _lblCleanStatus.Text = $"Scan complete: {totalFiles:N0} files ({DiskUsageInfo.FormatBytes(totalJunk)}) can be cleaned.";
            Log($"=== Complete: Total {DiskUsageInfo.FormatBytes(totalJunk)} junk on {_selectedDrive} ===");
        }
        catch (OperationCanceledException)
        {
            _lblCleanStatus.Text = "Scan stopped.";
            Log("[INFO] Scan cancelled by user.");
        }
        catch (Exception ex)
        {
            _lblCleanStatus.Text = "Scan error.";
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task ExecuteCleaningAsync()
    {
        var selected = _categories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one junk category.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        long totalBytesToFree = selected.Sum(c => c.TotalSizeBytes);
        bool isDryRun = _chkDryRun.Checked;

        string confirmMsg = isDryRun
            ? $"SIMULATION Mode (Dry Run) active on Drive {_selectedDrive}.\n\nThe application will analyze cleaning {DiskUsageInfo.FormatBytes(totalBytesToFree)} without deleting actual files.\n\nProceed with simulation?"
            : $"CONFIRM CLEANING FOR DRIVE {_selectedDrive}:\n\nYou are about to clean {selected.Count} categories with {DiskUsageInfo.FormatBytes(totalBytesToFree)} potential space to free.\n\nAll selected temporary files will be permanently deleted.\n\nProceed with cleaning?";

        if (MessageBox.Show(this, confirmMsg, isDryRun ? "Cleaning Simulation" : "Confirm Cleaning", MessageBoxButtons.YesNo, isDryRun ? MessageBoxIcon.Information : MessageBoxIcon.Warning) != DialogResult.Yes) return;

        SetBusyState(true, isDryRun ? "Analyzing simulation..." : "Cleaning files...");
        _cts = new CancellationTokenSource();

        try
        {
            Log(isDryRun ? $"=== Starting Simulation (Dry-Run) on {_selectedDrive} ===" : $"=== Starting Real Cleaning on {_selectedDrive} ===");
            var progress = new Progress<(string message, int percent)>(report =>
            {
                _lblCleanStatus.Text = report.message;
                _progressBar.Value = Math.Clamp(report.percent, 0, 100);
            });

            var cleanResult = await _fileCleaner.CleanCategoriesAsync(selected, _selectedDrive, isDryRun, progress, _cts.Token);

            Log(isDryRun
                ? $"[SIMULATION] Files analyzed: {cleanResult.FilesDeleted:N0} ({cleanResult.FormattedBytesFreed})"
                : $"[SUCCESS] Files deleted: {cleanResult.FilesDeleted:N0} ({cleanResult.FormattedBytesFreed})");

            if (cleanResult.FilesSkipped > 0) Log($"[INFO] Active/locked files skipped: {cleanResult.FilesSkipped:N0}");
            Log($"Execution time: {cleanResult.ElapsedTime.TotalSeconds:F2} seconds.");

            RefreshDiskInfo();
            await ScanSelectedCategoriesAsync();

            string titleSummary = isDryRun ? "Cleaning Simulation Results" : "Cleaning Complete";
            string textSummary = isDryRun
                ? $"Simulation Successful on Drive {_selectedDrive}!\n\nPotential space freed: {cleanResult.FormattedBytesFreed}\nFiles: {cleanResult.FilesDeleted:N0}\nSkipped: {cleanResult.FilesSkipped:N0}"
                : $"Cleaning Successful on Drive {_selectedDrive}!\n\nSpace successfully freed: {cleanResult.FormattedBytesFreed}\nFiles deleted: {cleanResult.FilesDeleted:N0}\nSkipped: {cleanResult.FilesSkipped:N0}";

            MessageBox.Show(this, textSummary, titleSummary, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _lblCleanStatus.Text = "Cleaning stopped.";
            Log("[INFO] Cleaning cancelled by user.");
        }
        catch (Exception ex)
        {
            _lblCleanStatus.Text = "An error occurred.";
            Log($"[ERROR] {ex.Message}");
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void ConfigureFolderColumns()
    {
        _lvFolders.BeginUpdate();
        _lvFolders.Columns.Clear();
        _lvFolders.Columns.Add("DIRECTORY / FILE", 320, HorizontalAlignment.Left);
        _lvFolders.Columns.Add("RELATIVE CAPACITY", 160, HorizontalAlignment.Left);
        _lvFolders.Columns.Add("SIZE", 110, HorizontalAlignment.Right);
        _lvFolders.Columns.Add("ACTIONS", 110, HorizontalAlignment.Right);
        _lvFolders.FlexibleColumnIndex = 0;
        _lvFolders.EndUpdate();
        _lvFolders.AutoFitFlexibleColumn();
    }

    private void ConfigureFolderColumnsForRoot() => ConfigureFolderColumns();
    private void ConfigureFolderColumnsForDetail() => ConfigureFolderColumns();

    private void ShowFolderEmptyState()
    {
        _lvFolders.Visible = false;
        _pnlFolderOverlay.IsScanning = false;
        _spinnerFolder.Stop();
        _spinnerFolder.Visible = false;
        _lblFolderOverlayTitle.Text = "Folder Analysis Not Started";
        _lblFolderOverlayStatus.Text = $"Scan all drive directories on {_selectedDrive} to map the largest folders,\nuncover hidden cache files, and optimize storage usage.";
        _btnFolderOverlayAction.Text = $"⚡ Start Whole Drive Scan ({_selectedDrive.TrimEnd('\\')})";
        _btnFolderOverlayAction.Variant = ButtonVariant.GradientCyan;
        _btnFolderOverlayAction.Visible = true;
        _pnlFolderOverlay.Visible = true;
        _pnlFolderOverlay.BringToFront();
        CenterFolderOverlayControls();
    }

    private void ShowFolderScanningState(string title, string status)
    {
        _lvFolders.Visible = false;
        _pnlFolderOverlay.IsScanning = true;
        _spinnerFolder.Visible = true;
        _spinnerFolder.Start();
        _lblFolderOverlayTitle.Text = title;
        _lblFolderOverlayStatus.Text = status;
        _btnFolderOverlayAction.Text = "Cancel Scan";
        _btnFolderOverlayAction.Variant = ButtonVariant.Secondary;
        _btnFolderOverlayAction.Visible = true;
        _pnlFolderOverlay.Visible = true;
        _pnlFolderOverlay.BringToFront();
        CenterFolderOverlayControls();
    }

    private void HideFolderOverlay()
    {
        _pnlFolderOverlay.IsScanning = false;
        _spinnerFolder.Stop();
        _spinnerFolder.Visible = false;
        _pnlFolderOverlay.Visible = false;
        _pnlFolderOverlay.SendToBack();
        _lvFolders.Visible = true;
        _lvFolders.BringToFront();
    }

    private void CenterFolderOverlayControls()
    {
        if (_pnlFolderOverlay.Width > 0 && _pnlFolderOverlay.Height > 0)
        {
            int cx = _pnlFolderOverlay.ClientSize.Width / 2;
            int cy = Math.Max(30, _pnlFolderOverlay.ClientSize.Height / 2 - 70);

            _spinnerFolder.Location = new Point(cx - _spinnerFolder.Width / 2, cy - 40);
            _lblFolderOverlayTitle.Location = new Point(cx - _lblFolderOverlayTitle.Width / 2, cy + 24);
            _lblFolderOverlayStatus.Location = new Point(cx - _lblFolderOverlayStatus.Width / 2, cy + 54);
            _btnFolderOverlayAction.Location = new Point(cx - _btnFolderOverlayAction.Width / 2, cy + 120);
        }
    }

    private async Task AnalyzeFoldersAsync()
    {
        _isScanningFolders = true;
        SetBusyState(true, $"Analyzing all folders on {_selectedDrive}...");
        _cts = new CancellationTokenSource();
        _lvFolders.Items.Clear();
        _cachedDriveFolders.Clear();
        _folderNavHistory.Clear();
        _isInDetailView = false;
        _currentInspectedPath = null;
        _btnBackFolder.Visible = false;
        UpdateFolderNavBreadcrumbs(null, 0, 0);
        _btnScanWholeDrive.Text = "Scanning...";

        ShowFolderScanningState($"Analyzing All Folders ({_selectedDrive})...", "Collecting system directories...");

        ConfigureFolderColumns();

        try
        {
            Log($"=== Starting Whole Folder Analysis on Drive {_selectedDrive} ===");
            var progress = new Progress<string>(msg =>
            {
                if (_lblFolderOverlayStatus != null)
                {
                    _lblFolderOverlayStatus.Text = msg;
                    CenterFolderOverlayControls();
                }
            });

            long totalBytes = _currentDiskUsage?.TotalBytes ?? 1;
            var allFolders = await _folderAnalyzer.AnalyzeAllFoldersAsync(_selectedDrive, totalBytes, progress, _cts.Token);
            _cachedDriveFolders = allFolders;

            ShowRootDriveFoldersView();
            HideFolderOverlay();

            Log($"=== Analysis Complete: {_cachedDriveFolders.Count} folders cataloged on {_selectedDrive} ===");
        }
        catch (OperationCanceledException)
        {
            Log("[INFO] Folder analysis cancelled by user.");
            if (_cachedDriveFolders.Count == 0)
            {
                ShowFolderEmptyState();
            }
            else
            {
                HideFolderOverlay();
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to analyze folders: {ex.Message}");
            if (_cachedDriveFolders.Count == 0)
            {
                ShowFolderEmptyState();
            }
            else
            {
                HideFolderOverlay();
            }
        }
        finally
        {
            _isScanningFolders = false;
            _btnScanWholeDrive.Text = "Scan Whole Drive";
            SetBusyState(false);
        }

    }

    private void ShowRootDriveFoldersView()
    {
        _isInDetailView = false;
        _currentInspectedPath = null;
        _btnBackFolder.Visible = false;

        long maxBytes = _cachedDriveFolders.Count > 0 ? _cachedDriveFolders.Max(f => f.TotalSizeBytes) : 1;
        long totalBytes = _cachedDriveFolders.Sum(f => f.TotalSizeBytes);

        foreach (var f in _cachedDriveFolders)
        {
            f.RelativePercentage = (double)f.TotalSizeBytes / Math.Max(1, maxBytes) * 100.0;
        }

        UpdateFolderNavBreadcrumbs(null, totalBytes, _cachedDriveFolders.Count);
        ConfigureFolderColumns();

        _lvFolders.BeginUpdate();
        _lvFolders.Items.Clear();
        foreach (var f in _cachedDriveFolders)
        {
            var item = new ListViewItem(f.Name) { Tag = f };
            item.SubItems.Add(f.FormattedPercentage);
            item.SubItems.Add(f.FormattedSize);
            item.SubItems.Add(string.Empty);
            _lvFolders.Items.Add(item);
        }
        _lvFolders.EndUpdate();
        _lvFolders.AutoFitFlexibleColumn();
        HideFolderOverlay();
    }

    private async Task InspectFolderAsync(string folderPath, bool addToHistory = true)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            MessageBox.Show($"Directory not found or inaccessible:\n{folderPath}", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (addToHistory)
        {
            _folderNavHistory.Push(folderPath);
        }

        _isInDetailView = true;
        _currentInspectedPath = folderPath;
        _btnBackFolder.Visible = true;
        _btnBackFolder.BringToFront();
        UpdateFolderNavBreadcrumbs(folderPath, 0, 0);

        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

        _isScanningFolders = true;
        SetBusyState(true, $"Reading item details: {folderName}...");
        _cts = new CancellationTokenSource();

        ShowFolderScanningState($"Inspecting Details: {folderName}", "Reading files and subdirectories...");

        try
        {
            Log($"[DETAILS] Inspecting folder contents: {folderPath}");
            var progress = new Progress<string>(msg =>
            {
                if (_lblFolderOverlayStatus != null)
                {
                    _lblFolderOverlayStatus.Text = msg;
                    CenterFolderOverlayControls();
                }
            });

            var items = await _folderAnalyzer.InspectFolderContentsAsync(folderPath, progress, _cts.Token);


            long maxBytes = items.Count > 0 ? items.Max(i => i.TotalSizeBytes) : 1;
            long totalBytes = items.Sum(i => i.TotalSizeBytes);
            foreach (var it in items)
            {
                it.RelativePercentage = (double)it.TotalSizeBytes / Math.Max(1, maxBytes) * 100.0;
            }

            UpdateFolderNavBreadcrumbs(folderPath, totalBytes, items.Count);
            ConfigureFolderColumns();

            _lvFolders.BeginUpdate();
            _lvFolders.Items.Clear();
            foreach (var it in items)
            {
                var lvi = new ListViewItem(it.Name) { Tag = it };
                lvi.SubItems.Add($"{it.RelativePercentage:F1}%");
                lvi.SubItems.Add(it.FormattedSize);
                lvi.SubItems.Add(string.Empty);
                _lvFolders.Items.Add(lvi);
            }
            _lvFolders.EndUpdate();
            _lvFolders.AutoFitFlexibleColumn();

            HideFolderOverlay();
            Log($"[DETAILS] Complete: {items.Count} items ({DiskUsageInfo.FormatBytes(totalBytes)}) detected in {folderPath}");
        }
        catch (OperationCanceledException)
        {
            HideFolderOverlay();
            Log("[INFO] Folder inspection cancelled by user.");
        }
        catch (Exception ex)
        {
            HideFolderOverlay();
            Log($"[ERROR] Failed to read details: {ex.Message}");
        }
        finally
        {
            _isScanningFolders = false;
            SetBusyState(false);
        }

    }

    private async Task NavigateBackFolderAsync()
    {
        if (_folderNavHistory.Count > 0)
        {
            _folderNavHistory.Pop(); // Pop folder saat ini
        }

        if (_folderNavHistory.Count > 0)
        {
            string previousPath = _folderNavHistory.Peek();
            await InspectFolderAsync(previousPath, addToHistory: false);
        }
        else
        {
            ShowRootDriveFoldersView();
        }
    }

    private void UpdateFolderNavBreadcrumbs(string? currentPath, long totalBytes, int itemCount)
    {
        if (_pnlBreadcrumb == null) return;
        _pnlBreadcrumb.SuspendLayout();
        _pnlBreadcrumb.Controls.Clear();

        if (string.IsNullOrEmpty(currentPath))
        {
            _btnBackFolder.Visible = false;
            _pnlBreadcrumb.Location = new Point(10, 8);
            var chipRoot = CreateBreadcrumbChip(_selectedDrive.TrimEnd('\\'), true, null);
            _pnlBreadcrumb.Controls.Add(chipRoot);
        }
        else
        {
            _btnBackFolder.Visible = true;
            _pnlBreadcrumb.Location = new Point(44, 8);
            string normPath = currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] rawParts = normPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            var displayParts = new List<(string text, string path)>();
            string accPath = "";

            for (int i = 0; i < rawParts.Length; i++)
            {
                string part = rawParts[i];
                if (i == 0)
                {
                    accPath = part.EndsWith(Path.VolumeSeparatorChar) ? (part + Path.DirectorySeparatorChar) : part;
                }
                else
                {
                    accPath = Path.Combine(accPath, part);
                }

                // Cek jika dua part terakhir adalah AppData dan subfolder (seperti AppData\Local pada prototype Image 2)
                if (i == rawParts.Length - 2 &&
                    part.Equals("AppData", StringComparison.OrdinalIgnoreCase))
                {
                    string nextPart = rawParts[i + 1];
                    string combinedPath = Path.Combine(accPath, nextPart);
                    displayParts.Add(($"{part}\\{nextPart}", combinedPath));
                    break;
                }
                else
                {
                    displayParts.Add((part, accPath));
                }
            }

            for (int i = 0; i < displayParts.Count; i++)
            {
                var (partText, targetP) = displayParts[i];
                bool isLast = (i == displayParts.Count - 1);

                if (i > 0)
                {
                    var lblSep = new Label
                    {
                        Text = "\\",
                        ForeColor = Color.FromArgb(71, 85, 105),
                        Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                        AutoSize = true,
                        Margin = new Padding(3, 6, 3, 0)
                    };
                    _pnlBreadcrumb.Controls.Add(lblSep);
                }

                var chip = CreateBreadcrumbChip(partText, isLast, isLast ? null : () => _ = InspectFolderAsync(targetP));
                _pnlBreadcrumb.Controls.Add(chip);
            }
        }


        _pnlBreadcrumb.ResumeLayout();

        if (_lblFolderSummary != null)
        {
            _lblFolderSummary.UpdateSummary(totalBytes, itemCount);
        }

        if (_pnlFolderNav != null && _btnScanWholeDrive != null && _lblFolderSummary != null)
        {
            _btnScanWholeDrive.Location = new Point(_pnlFolderNav.ClientSize.Width - 165, 9);
            _lblFolderSummary.Location = new Point(_btnScanWholeDrive.Left - _lblFolderSummary.Width - 16, 11);
            int startX = _btnBackFolder.Visible ? 44 : 10;
            _pnlBreadcrumb.Location = new Point(startX, 8);
            _pnlBreadcrumb.Width = Math.Max(50, _lblFolderSummary.Left - startX - 10);
        }
    }

    private Control CreateBreadcrumbChip(string text, bool isActive, Action? onClick)
    {
        if (isActive)
        {
            var pnl = new Panel
            {
                AutoSize = true,
                Padding = new Padding(8, 2, 8, 2),
                Margin = new Padding(1, 1, 1, 1),
                BackColor = Color.FromArgb(21, 29, 44)
            };
            pnl.Paint += (_, pe) =>
            {
                var g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(0.5f, 0.5f, pnl.Width - 1f, pnl.Height - 1f);
                using var path = Theme.CreateRoundedRectangle(r, 6f);
                using var pen = new Pen(Color.FromArgb(42, 55, 74), 1.2f);
                g.DrawPath(pen, path);
            };
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 250, 252),
                AutoSize = true,
                Location = new Point(8, 2)
            };
            pnl.Controls.Add(lbl);
            return pnl;
        }
        else
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 116, 139),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 0, 1)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(25, 34, 50);
            if (onClick != null) btn.Click += (_, _) => onClick();
            return btn;
        }
    }


    private async void OnFolderRowActionClicked(ListViewItem lvi, int actionIndex)
    {
        string? targetPath = null;
        bool isDirectory = true;

        if (lvi.Tag is FolderSizeItem f)
        {
            targetPath = f.Path;
            isDirectory = true;
        }
        else if (lvi.Tag is FolderDetailItem it)
        {
            targetPath = it.FullPath;
            isDirectory = it.IsDirectory;
        }

        if (string.IsNullOrEmpty(targetPath)) return;

        switch (actionIndex)
        {
            case 0: // ↗ Buka di Explorer
                OpenInExplorer(targetPath, !isDirectory);
                break;

            case 1: // ⚡ Drill-down / Inspect
                if (isDirectory)
                {
                    await InspectFolderAsync(targetPath);
                }
                else
                {
                    OpenInExplorer(targetPath, true);
                }
                break;

            case 2: // 🗑 Hapus ke Recycle Bin
                await DeleteFolderItemSafelyAsync(targetPath, isDirectory);
                break;
        }
    }

    private async Task DeleteFolderItemSafelyAsync(string targetPath, bool isDirectory)
    {
        if (!SystemSafetyGuard.IsSafeForUserManualDelete(targetPath, out string reason))
        {
            MessageBox.Show($"This folder/file is protected by DiskPulse for system security:\n\n{reason}",
                "System Protection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string name = Path.GetFileName(targetPath);
        var confirm = MessageBox.Show(
            $"Are you sure you want to move this {(isDirectory ? "folder" : "file")} to the Recycle Bin?\n\n" +
            $"• Name: {name}\n" +
            $"• Path: {targetPath}\n\n" +
            "Items can be restored from the Recycle Bin if needed.",
            "Confirm Move to Recycle Bin",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        bool success = await Task.Run(() => NativeMethods.SendToRecycleBin(targetPath));
        if (success)
        {
            Log($"[SUCCESS] '{targetPath}' moved to Recycle Bin.");
            if (_isInDetailView && !string.IsNullOrEmpty(_currentInspectedPath))
            {
                await InspectFolderAsync(_currentInspectedPath, addToHistory: false);
            }
            else
            {
                await AnalyzeFoldersAsync();
            }
        }
        else
        {
            Log($"[FAILED] Failed to move '{targetPath}' to Recycle Bin.");
            MessageBox.Show($"Failed to move {(isDirectory ? "folder" : "file")} to Recycle Bin.", "Deletion Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnFolderListViewDoubleClickAsync()
    {
        if (_lvFolders.SelectedItems.Count == 0) return;
        var selected = _lvFolders.SelectedItems[0];

        if (!_isInDetailView)
        {
            string? targetPath = (selected.Tag as FolderSizeItem)?.Path ?? selected.Tag as string;
            if (!string.IsNullOrEmpty(targetPath))
            {
                await InspectFolderAsync(targetPath);
            }
        }
        else
        {
            if (selected.Tag is FolderDetailItem detail)
            {
                if (detail.IsDirectory)
                {
                    await InspectFolderAsync(detail.FullPath);
                }
                else
                {
                    OpenInExplorer(detail.FullPath, isFile: true);
                }
            }
        }
    }

    private async Task OnInspectFolderClickedAsync()
    {
        if (_lvFolders.SelectedItems.Count == 0)
        {
            if (!_isInDetailView && _lvFolders.Items.Count > 0)
            {
                MessageBox.Show("Select a folder in the table to view its contents.", "Select Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        var selected = _lvFolders.SelectedItems[0];
        if (!_isInDetailView)
        {
            string? targetPath = (selected.Tag as FolderSizeItem)?.Path ?? selected.Tag as string;
            if (!string.IsNullOrEmpty(targetPath))
            {
                await InspectFolderAsync(targetPath);
            }
        }
        else
        {
            if (selected.Tag is FolderDetailItem detail)
            {
                if (detail.IsDirectory)
                {
                    await InspectFolderAsync(detail.FullPath);
                }
                else
                {
                    OpenInExplorer(detail.FullPath, isFile: true);
                }
            }
        }
    }

    private void OpenSelectedFolderInExplorer()
    {
        if (_lvFolders.SelectedItems.Count > 0)
        {
            var selected = _lvFolders.SelectedItems[0];
            if (!_isInDetailView)
            {
                string? path = (selected.Tag as FolderSizeItem)?.Path ?? selected.Tag as string;
                if (!string.IsNullOrEmpty(path))
                {
                    OpenInExplorer(path, isFile: false);
                    return;
                }
            }
            else
            {
                if (selected.Tag is FolderDetailItem detail)
                {
                    OpenInExplorer(detail.FullPath, isFile: !detail.IsDirectory);
                    return;
                }
            }
        }

        if (_isInDetailView && !string.IsNullOrEmpty(_currentInspectedPath))
        {
            OpenInExplorer(_currentInspectedPath, isFile: false);
            return;
        }

        OpenInExplorer(_selectedDrive, isFile: false);
    }

    private void OpenInExplorer(string path, bool isFile)
    {
        try
        {
            if (isFile)
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = true
                    });
                    Log($"[EXPLORER] Opening file in Explorer: {path}");
                }
                else if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    Log($"[EXPLORER] Opening folder in Explorer: {path}");
                }
            }
            else
            {
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    Log($"[EXPLORER] Opening folder in Explorer: {path}");
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open Explorer: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log($"[ERROR] Failed to open Explorer: {ex.Message}");
        }
    }

    private void CancelCurrentOperation() => _cts?.Cancel();

    private void SetBusyState(bool isBusy, string message = "")
    {
        _btnScan.Enabled = !isBusy;
        _btnClean.Enabled = !isBusy;
        _btnAnalyzeFolders.Enabled = !isBusy;
        _btnBackFolder.Enabled = !isBusy;
        _btnHeaderRefresh.Enabled = !isBusy;
        if (_btnUpdateApp != null) _btnUpdateApp.Enabled = !isBusy;
        _btnCancel.Visible = isBusy;
        _btnCancel.Enabled = isBusy;
        _progressBar.Visible = isBusy;
        if (isBusy)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 25;
        }
        else
        {
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 0;
        }
        _lblCleanStatus.Visible = isBusy;
        if (!string.IsNullOrEmpty(message)) _lblCleanStatus.Text = message;
    }

    private void Log(string message)
    {
        if (InvokeRequired) { Invoke(() => Log(message)); return; }
        string time = DateTime.Now.ToString("HH:mm:ss");
        _txtLog.AppendText($"[{time}] {message}{Environment.NewLine}");
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    private void BuildContextMenu()
    {
        _ctxFolderMenu = new DarkContextMenu();
        _ctxFolderMenu.Opening += CtxFolderMenu_Opening;
        _lvFolders.ContextMenuStrip = _ctxFolderMenu;

        _lvFolders.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var it = _lvFolders.GetItemAt(e.X, e.Y);
                if (it != null)
                {
                    _lvFolders.SelectedItems.Clear();
                    it.Selected = true;
                }
            }
        };
    }

    private void CtxFolderMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (_lvFolders.SelectedItems.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        var selected = _lvFolders.SelectedItems[0];
        string targetPath = "";
        string name = "";
        string formattedSize = "";
        bool isDirectory = true;

        if (!_isInDetailView)
        {
            var f = selected.Tag as FolderSizeItem;
            targetPath = f?.Path ?? selected.Tag as string ?? "";
            name = f?.Name ?? Path.GetFileName(targetPath);
            formattedSize = f?.FormattedSize ?? "";
            isDirectory = true;
        }
        else
        {
            var d = selected.Tag as FolderDetailItem;
            targetPath = d?.FullPath ?? "";
            name = d?.Name ?? Path.GetFileName(targetPath);
            formattedSize = d?.FormattedSize ?? "";
            isDirectory = d?.IsDirectory ?? Directory.Exists(targetPath);
        }

        if (string.IsNullOrEmpty(targetPath))
        {
            e.Cancel = true;
            return;
        }

        _ctxFolderMenu.Items.Clear();

        // 1. Header Info Baris Item
        string headerIcon = isDirectory ? "📁 " : "📄 ";
        var lblHeader = new ToolStripMenuItem($"{headerIcon}{name} ({formattedSize})")
        {
            Enabled = false,
            Font = Theme.BodyBold
        };
        _ctxFolderMenu.Items.Add(lblHeader);
        _ctxFolderMenu.Items.Add(new ToolStripSeparator());

        // 2. Opsi Uninstall Aplikasi (Selalu tampil untuk folder atau file eksekutabel)
        if (isDirectory || targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var appInfo = _uninstallerService.FindAppForPath(targetPath);
            string appName = !string.IsNullOrWhiteSpace(appInfo.DisplayName) ? appInfo.DisplayName : name;

            if (appInfo.HasDirectUninstaller)
            {
                var mnuUninstall = new ToolStripMenuItem($"⚡ Uninstall \"{appName}\"...")
                {
                    Font = Theme.BodyBold
                };
                mnuUninstall.Click += (_, _) => LaunchAppUninstall(appInfo);
                _ctxFolderMenu.Items.Add(mnuUninstall);
            }
            else
            {
                var mnuSettings = new ToolStripMenuItem($"⚡ Uninstall \"{appName}\" (Windows Settings)...");
                mnuSettings.Click += (_, _) =>
                {
                    MessageBox.Show(this,
                        $"The application/folder '{appName}' does not have a standalone uninstaller executable (such as unins000.exe) within it.\n\n" +
                        $"The Windows Settings 'Installed Apps' page has been opened so you can uninstall it if officially registered on the system.\n\n" +
                        $"Tip: You can also instantly and cleanly delete this folder using the 'Permanent Delete' option in DiskPulse.",
                        "Application Uninstall Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    AppUninstallerService.OpenWindowsInstalledApps();
                };
                _ctxFolderMenu.Items.Add(mnuSettings);
            }
            _ctxFolderMenu.Items.Add(new ToolStripSeparator());
        }

        // 3. Inspect Folder Contents Option if Directory
        if (isDirectory)
        {
            var mnuInspect = new ToolStripMenuItem("🔎 Inspect Folder Contents");
            mnuInspect.Click += async (_, _) => await InspectFolderAsync(targetPath);
            _ctxFolderMenu.Items.Add(mnuInspect);
        }

        // 4. Open in Explorer & Copy Path
        var mnuExplorer = new ToolStripMenuItem("📂 Open in File Explorer");
        mnuExplorer.Click += (_, _) => OpenInExplorer(targetPath, isFile: !isDirectory);
        _ctxFolderMenu.Items.Add(mnuExplorer);

        var mnuCopy = new ToolStripMenuItem("📋 Copy Full Path");
        mnuCopy.Click += (_, _) => CopyPathToClipboard(targetPath);
        _ctxFolderMenu.Items.Add(mnuCopy);

        _ctxFolderMenu.Items.Add(new ToolStripSeparator());

        // 5. Move to Recycle Bin (Safe Option)
        var mnuRecycle = new ToolStripMenuItem("🗑️ Move to Recycle Bin");
        mnuRecycle.Click += async (_, _) => await DeleteSelectedToRecycleBinAsync(targetPath, name, formattedSize, isDirectory);
        _ctxFolderMenu.Items.Add(mnuRecycle);

        // 6. Permanent Delete (With Explicit Confirmation & System Protection)
        var mnuDelete = new ToolStripMenuItem("⚠️ Permanent Delete (Shift+Delete)...")
        {
            ForeColor = Theme.AccentAmber
        };
        mnuDelete.Click += async (_, _) => await DeleteSelectedPermanentAsync(targetPath, name, formattedSize, isDirectory);
        _ctxFolderMenu.Items.Add(mnuDelete);
    }

    private async Task DeleteSelectedPermanentAsync(string targetPath, string name, string formattedSize, bool isDirectory)
    {
        if (!SystemSafetyGuard.IsSafeForUserManualDelete(targetPath, out string reason))
        {
            MessageBox.Show(this, $"Action Denied for System Security:\n\n{reason}\n\nPath: {targetPath}",
                "DiskPulse System Protection", MessageBoxButtons.OK, MessageBoxIcon.Stop);
            return;
        }

        string typeLabel = isDirectory ? "directory folder and all of its contents" : "file";
        string confirmMsg = $"PERMANENT DELETION WARNING:\n\n" +
                            $"You are about to permanently delete this {typeLabel}:\n" +
                            $"• Name: {name}\n" +
                            $"• Size: {formattedSize}\n" +
                            $"• Path: {targetPath}\n\n" +
                            $"All data will be permanently erased from disk and CANNOT be recovered from the Recycle Bin.\n\n" +
                            $"Proceed with permanent deletion?";

        if (MessageBox.Show(this, confirmMsg, "Confirm Permanent Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        SetBusyState(true, $"Permanently deleting: {name}...");

        try
        {
            Log($"[PERMANENT DELETE] Deleting: {targetPath}");
            await Task.Run(() =>
            {
                if (isDirectory)
                {
                    if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, recursive: true);
                    }
                }
                else
                {
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                }
            });

            Log($"[SUCCESS] Permanently deleted: {name} ({formattedSize})");
            RefreshDiskInfo();

            if (_isInDetailView && !string.IsNullOrEmpty(_currentInspectedPath))
            {
                await InspectFolderAsync(_currentInspectedPath, addToHistory: false);
            }
            else
            {
                _cachedDriveFolders.RemoveAll(f => f.Path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
                ShowRootDriveFoldersView();
            }

            MessageBox.Show(this, $"Item was permanently deleted from disk:\n\n{name} ({formattedSize})",
                "Deletion Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to delete: {ex.Message}");
            MessageBox.Show(this, $"Failed to delete item: {ex.Message}\n\nMake sure the item is not currently in use by another application.",
                "Deletion Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task DeleteSelectedToRecycleBinAsync(string targetPath, string name, string formattedSize, bool isDirectory)
    {
        if (!SystemSafetyGuard.IsSafeForUserManualDelete(targetPath, out string reason))
        {
            MessageBox.Show(this, $"Action Denied for System Security:\n\n{reason}\n\nPath: {targetPath}",
                "DiskPulse System Protection", MessageBoxButtons.OK, MessageBoxIcon.Stop);
            return;
        }

        string typeLabel = isDirectory ? "folder" : "file";
        string confirmMsg = $"Move the following {typeLabel} to the Recycle Bin?\n\n" +
                            $"• Name: {name}\n" +
                            $"• Size: {formattedSize}\n" +
                            $"• Path: {targetPath}\n\n" +
                            $"Items can be restored via the Windows Recycle Bin if needed.";

        if (MessageBox.Show(this, confirmMsg, "Move to Recycle Bin", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetBusyState(true, $"Moving to Recycle Bin: {name}...");

        try
        {
            Log($"[RECYCLE BIN] Moving to Recycle Bin: {targetPath}");
            bool success = await Task.Run(() => NativeMethods.SendToRecycleBin(targetPath));

            if (!success)
            {
                throw new IOException("Windows Shell function failed to move item to Recycle Bin.");
            }

            Log($"[SUCCESS] Moved to Recycle Bin: {name} ({formattedSize})");
            RefreshDiskInfo();

            if (_isInDetailView && !string.IsNullOrEmpty(_currentInspectedPath))
            {
                await InspectFolderAsync(_currentInspectedPath, addToHistory: false);
            }
            else
            {
                _cachedDriveFolders.RemoveAll(f => f.Path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
                ShowRootDriveFoldersView();
            }

            MessageBox.Show(this, $"Item successfully moved to Recycle Bin:\n\n{name} ({formattedSize})",
                "Moved Successfully", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to move to Recycle Bin: {ex.Message}");
            MessageBox.Show(this, $"Failed to move to Recycle Bin: {ex.Message}\n\nMake sure the item is not locked by the system or another process.",
                "Failed to Move", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void LaunchAppUninstall(AppUninstallInfo app)
    {
        string prompt = $"Run the official uninstaller for:\n\n" +
                        $"• Application Name: {app.DisplayName}\n" +
                        $"• Version: {app.DisplayVersion ?? "N/A"}\n" +
                        $"• Publisher: {app.Publisher ?? "N/A"}\n" +
                        $"• Location: {app.InstallLocation ?? "N/A"}\n\n" +
                        $"DiskPulse will launch the official uninstaller wizard. Continue?";

        if (MessageBox.Show(this, prompt, "Confirm Application Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        if (_uninstallerService.LaunchUninstall(app, out string err))
        {
            Log($"[UNINSTALL] Launching uninstaller for: {app.DisplayName}");
            MessageBox.Show(this, $"The uninstaller wizard for '{app.DisplayName}' has been started.\n\nPlease follow the uninstaller instructions in the window that opened.",
                "Uninstaller Running", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            Log($"[WARNING] Failed to run uninstaller: {err}");
            MessageBox.Show(this, $"Failed to start automatic uninstaller: {err}\n\nOpening Windows Installed Apps settings...",
                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AppUninstallerService.OpenWindowsInstalledApps();
        }
    }

    private void CopyPathToClipboard(string path)
    {
        try
        {
            Clipboard.SetText(path);
            Log($"[CLIPBOARD] Copied path to clipboard: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshCurrentFolderViewAsync()
    {
        SetBusyState(true, $"Refreshing drive capacity and folder data on {_selectedDrive}...");
        try
        {
            Log($"[REFRESH] Refreshing folder data on drive {_selectedDrive}...");
            RefreshDiskInfo();
            if (_isInDetailView && !string.IsNullOrEmpty(_currentInspectedPath))
            {
                await InspectFolderAsync(_currentInspectedPath, addToHistory: false);
            }
            else
            {
                await AnalyzeFoldersAsync();
            }
            Log($"[REFRESH] Refresh complete for {_selectedDrive}.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to refresh folders: {ex.Message}");
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task RefreshCurrentActiveViewAsync()
    {
        SetBusyState(true, "Refreshing all drives and system status...");
        try
        {
            Log("=== Refreshing All Drive Status and Active View ===");
            _suppressDriveSelectionChanged = true;
            try
            {
                LoadAvailableDrives(_selectedDrive);
            }
            finally
            {
                _suppressDriveSelectionChanged = false;
            }

            RefreshDiskInfo();

            if (_pnlContentCleaner.Visible)
            {
                await ScanSelectedCategoriesAsync();
            }
            else if (_pnlContentFolders.Visible)
            {
                if (_isInDetailView && !string.IsNullOrEmpty(_currentInspectedPath))
                {
                    await InspectFolderAsync(_currentInspectedPath, addToHistory: false);
                }
                else if (_cachedDriveFolders.Count > 0)
                {
                    await AnalyzeFoldersAsync();
                }
            }
            else if (_pnlContentApps.Visible)
            {
                LoadInstalledAppsView(forceReload: true);
            }

            Log("[REFRESH] All drive statuses and active view successfully updated.");
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to refresh: {ex.Message}");
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private async Task CheckAndApplyUpdateAsync()
    {
        if (_btnCancel.Visible || _isScanningFolders || _isLoadingApps)
        {
            MessageBox.Show(
                "The application is currently running an analysis or cleaning task. Please wait until it completes before updating.",
                "Update DiskPulse",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        string originalText = _btnUpdateApp.Text;
        _btnUpdateApp.Enabled = false;
        _btnUpdateApp.Text = "Checking...";
        Cursor = Cursors.WaitCursor;

        try
        {
            Log("[UPDATE] Checking for DiskPulse update packages...");
            await Task.Delay(350); // Smooth visual feedback

            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe) || currentExe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(currentExe))
            {
                try { currentExe = Process.GetCurrentProcess().MainModule?.FileName; } catch { }
            }
            if (string.IsNullOrEmpty(currentExe) || currentExe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(currentExe))
            {
                string rootExe = Path.Combine(Directory.GetCurrentDirectory(), "DiskPulse.exe");
                currentExe = File.Exists(rootExe) ? rootExe : Path.Combine(AppContext.BaseDirectory, "DiskPulse.exe");
            }

            string currentNorm = Path.GetFullPath(currentExe);
            string appDir = AppContext.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();
            string? exeDir = Path.GetDirectoryName(currentNorm);

            string[] candidatePaths = [
                Path.Combine(appDir, "DiskPulse_Latest.exe"),
                Path.Combine(currentDir, "DiskPulse_Latest.exe"),
                Path.Combine(appDir, "publish", "DiskPulse.exe"),
                Path.Combine(currentDir, "publish", "DiskPulse.exe"),
                Path.Combine(appDir, "..", "publish", "DiskPulse.exe"),
                Path.Combine(currentDir, "..", "publish", "DiskPulse.exe"),
                exeDir != null ? Path.Combine(exeDir, "publish", "DiskPulse.exe") : "",
                exeDir != null ? Path.Combine(exeDir, "DiskPulse_Latest.exe") : "",
                Path.Combine(Path.GetTempPath(), "DiskPulse_Update.exe")
            ];

            string? updateSource = null;
            bool isNewer = false;

            foreach (var candidate in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;
                string candidateNorm = Path.GetFullPath(candidate);
                if (string.Equals(candidateNorm, currentNorm, StringComparison.OrdinalIgnoreCase)) continue;

                var srcInfo = new FileInfo(candidateNorm);
                var curInfo = new FileInfo(currentNorm);

                if (srcInfo.LastWriteTimeUtc > curInfo.LastWriteTimeUtc.AddSeconds(2) || srcInfo.Length != curInfo.Length)
                {
                    updateSource = candidateNorm;
                    isNewer = true;
                    break;
                }
                else if (updateSource == null)
                {
                    updateSource = candidateNorm;
                }
            }

            if (updateSource != null && isNewer)
            {
                var srcInfo = new FileInfo(updateSource);
                string prompt =
                    $"DiskPulse Update Ready to Install!\n\n" +
                    $"• Source Package: {Path.GetFileName(updateSource)} ({DiskUsageInfo.FormatBytes(srcInfo.Length)})\n" +
                    $"• Release Date: {srcInfo.LastWriteTime:dd MMM yyyy, HH:mm}\n" +
                    $"• Target File: {Path.GetFileName(currentNorm)}\n\n" +
                    "Would you like to apply the update now?\n" +
                    "The application will automatically restart with the latest version.";

                var choice = MessageBox.Show(prompt, "Update Available - DiskPulse", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (choice == DialogResult.Yes)
                {
                    ApplyUpdateAndRestart(updateSource, currentNorm);
                    return;
                }
            }
            else if (updateSource != null)
            {
                var choice = MessageBox.Show(
                    "Your DiskPulse installation is already running the latest binary (v1.2.0 Pro).\n\n" +
                    "Would you like to reinstall / resynchronize the application binary now?",
                    "DiskPulse Up to Date",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Yes)
                {
                    ApplyUpdateAndRestart(updateSource, currentNorm);
                    return;
                }
            }
            else
            {
                MessageBox.Show(
                    "DiskPulse is already up to date (v1.2.0 Pro)!\n\n" +
                    "No newer update package was detected in the update directory.",
                    "DiskPulse Up to Date",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Failed to check for updates: {ex.Message}");
            MessageBox.Show($"Error while checking for updates: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
            _btnUpdateApp.Text = originalText;
            _btnUpdateApp.Enabled = true;
        }
    }

    private void ApplyUpdateAndRestart(string sourceExe, string targetExe)
    {
        try
        {
            int currentPid = Process.GetCurrentProcess().Id;
            string scriptPath = Path.Combine(Path.GetTempPath(), $"diskpulse_patch_{Guid.NewGuid():N}.cmd");

            string scriptContent = $@"@echo off
timeout /t 1 /nobreak >nul
:waitloop
tasklist /FI ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" >nul
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

:copyloop
copy /Y ""{sourceExe}"" ""{targetExe}"" >nul 2>&1
if errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto copyloop
)

start """" ""{targetExe}""
del ""%~f0""
";

            File.WriteAllText(scriptPath, scriptContent);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{scriptPath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            Log("[UPDATE] Update ready to install. Restarting application now...");
            Application.Exit();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start automatic update process: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool CheckIfAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
