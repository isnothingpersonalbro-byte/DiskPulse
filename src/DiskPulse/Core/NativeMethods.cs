using System.Runtime.InteropServices;

namespace DiskPulse.Core;

/// <summary>
/// Definisi P/Invoke untuk fungsi-fungsi Win32 API native tingkat rendah.
/// </summary>
public static class NativeMethods
{
    // DWM (Desktop Window Manager) API untuk Dark Mode Titlebar di Windows 10/11
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    // Shell API untuk Recycle Bin (Tempat Sampah Windows)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public const uint SHERB_NOCONFIRMATION = 0x00000001;
    public const uint SHERB_NOPROGRESSUI   = 0x00000002;
    public const uint SHERB_NOSOUND        = 0x00000004;

    // SHFileOperation untuk memindahkan berkas/folder ke Recycle Bin secara aman
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public const uint FO_DELETE = 0x0003;
    public const ushort FOF_ALLOWUNDO = 0x0040;
    public const ushort FOF_NOCONFIRMATION = 0x0010;
    public const ushort FOF_SILENT = 0x0004;

    /// <summary>
    /// Memindahkan berkas atau direktori ke Windows Recycle Bin (dapat di-restore oleh pengguna).
    /// </summary>
    public static bool SendToRecycleBin(string path)
    {
        try
        {
            string doubleNull = path + "\0\0";
            var op = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = doubleNull,
                pTo = null,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = null
            };
            int ret = SHFileOperation(ref op);
            return ret == 0 && !op.fAnyOperationsAborted;
        }
        catch
        {
            return false;
        }
    }

    // Win32 Disk Free Space API untuk akurasi cluster drive
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    /// <summary>
    /// Mengaktifkan mode gelap pada titlebar jendela Win32/WinForms.
    /// </summary>
    public static void EnableImmersiveDarkMode(IntPtr handle)
    {
        if (Environment.OSVersion.Version.Major >= 10)
        {
            int useImmersiveDarkMode = 1;
            // Coba atribut Windows 10 build 19041+ / Windows 11 (20)
            int hr = DwmSetWindowAttribute(
                handle,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref useImmersiveDarkMode,
                sizeof(int));

            if (hr != 0)
            {
                // Fallback untuk build Windows 10 awal (19)
                DwmSetWindowAttribute(
                    handle,
                    DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1,
                    ref useImmersiveDarkMode,
                    sizeof(int));
            }
        }
    }

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;

    /// <summary>
    /// Memungkinkan pemindahan jendela (drag window) secara native melalui event mouse down pada custom titlebar.
    /// </summary>
    public static void DragWindow(IntPtr handle)
    {
        ReleaseCapture();
        SendMessage(handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
    }

    /// <summary>
    /// Mengaktifkan sudut membulat modern (Rounded Corners) pada Windows 11 untuk jendela borderless.
    /// </summary>
    public static void EnableWindowRounding(IntPtr handle)
    {
        try
        {
            if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000)
            {
                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            }
        }
        catch { }
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
    public static extern IntPtr CreateRoundRectRgn(
        int nLeftRect,
        int nTopRect,
        int nRightRect,
        int nBottomRect,
        int nWidthEllipse,
        int nHeightEllipse);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    public static extern bool DeleteObject(IntPtr hObject);

    // UXTheme Dark Mode Support untuk Win32 Common Controls & Scrollbars di Windows 10 & 11
    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    public static void EnableDarkModeForApp()
    {
        try
        {
            // 2 = ForceDark (Windows 10 1809+ / Windows 11)
            SetPreferredAppMode(2);
        }
        catch { }
    }
}

