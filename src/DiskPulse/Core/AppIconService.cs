using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DiskPulse.Models;
using DiskPulse.UI;

namespace DiskPulse.Core;

/// <summary>
/// Layanan ekstraksi dan caching logo/ikon asli aplikasi terpasang di Windows.
/// Mengambil ikon dari DisplayIcon Registry, direktori instalasi, Start Menu shortcuts (.lnk),
/// dan berkas eksekutabel (.exe/.ico).
/// </summary>
public static class AppIconService
{
    private static readonly ConcurrentDictionary<string, Image?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Mengambil gambar logo asli aplikasi (36x36 px) dengan caching cepat.
    /// </summary>
    public static Image? GetAppIcon(AppUninstallInfo app)
    {
        string cacheKey = !string.IsNullOrWhiteSpace(app.DisplayIcon)
            ? app.DisplayIcon
            : (!string.IsNullOrWhiteSpace(app.InstallLocation) ? app.InstallLocation : app.DisplayName);

        if (IconCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        Image? extracted = ExtractIconInternal(app);
        IconCache[cacheKey] = extracted;
        return extracted;
    }

    private static Image? ExtractIconInternal(AppUninstallInfo app)
    {
        // 1. Coba dari DisplayIcon registri (sumber utama paling akurat di Windows)
        if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
        {
            var img = ExtractFromDisplayIcon(app.DisplayIcon);
            if (img != null) return img;
        }

        // 2. Coba dari direktori InstallLocation
        if (!string.IsNullOrWhiteSpace(app.InstallLocation) && Directory.Exists(app.InstallLocation))
        {
            var img = ExtractFromInstallLocation(app.InstallLocation, app.DisplayName);
            if (img != null) return img;
        }

        // 3. Coba dari pencarian direktori penerbit (Publisher) & nama aplikasi
        var pubImg = ExtractFromPublisherAndNameSearch(app);
        if (pubImg != null) return pubImg;

        // 4. Coba dari direktori brand ternama (Epic Games, Steam, dll.)
        var brandImg = ExtractFromKnownBrandLocations(app);
        if (brandImg != null) return brandImg;

        // 5. Coba cari dari Start Menu Shortcuts (.lnk)
        var startMenuImg = ExtractFromStartMenu(app.DisplayName);
        if (startMenuImg != null) return startMenuImg;

        // 6. Coba cari dari Windows Installer Cache (MSI packages)
        var msiImg = ExtractFromWindowsInstallerCache(app);
        if (msiImg != null) return msiImg;

        // 7. Coba dari LocalUninstallerPath jika ada
        if (!string.IsNullOrWhiteSpace(app.LocalUninstallerPath) && File.Exists(app.LocalUninstallerPath))
        {
            try
            {
                using var ico = Icon.ExtractAssociatedIcon(app.LocalUninstallerPath);
                if (ico != null) return ResizeImage(ico.ToBitmap(), 36, 36);
            }
            catch { }
        }

        // 8. Coba dari UninstallString
        if (!string.IsNullOrWhiteSpace(app.UninstallString))
        {
            string? exe = ExtractExeFromCommand(app.UninstallString);
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                try
                {
                    using var ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) return ResizeImage(ico.ToBitmap(), 36, 36);
                }
                catch { }
            }
        }

        // 9. Jika tidak ditemukan berkas biner di disk, buat logo vektor grafis berkualitas tinggi
        return GenerateAppVectorLogo(app);
    }

    private static Image? ExtractFromDisplayIcon(string rawDisplayIcon)
    {
        try
        {
            string cleaned = rawDisplayIcon.Trim('"', ' ');
            string filePath = cleaned;
            int iconIndex = 0;

            // Jika ada tanda koma indeks seperti C:\Path\app.exe,0 atau ...,-101
            int commaIdx = cleaned.LastIndexOf(',');
            if (commaIdx > 0 && commaIdx < cleaned.Length - 1)
            {
                string pathPart = cleaned.Substring(0, commaIdx).Trim('"', ' ');
                string idxPart = cleaned.Substring(commaIdx + 1).Trim();
                if (int.TryParse(idxPart, out int parsedIdx))
                {
                    iconIndex = parsedIdx;
                    filePath = pathPart;
                }
            }

            if (!File.Exists(filePath))
            {
                // Cek apakah ekstensi hilang (misal path ke icon tanpa .ico)
                if (File.Exists(filePath + ".ico")) filePath += ".ico";
                else if (File.Exists(filePath + ".exe")) filePath += ".exe";
                else return null;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Berkas .ico murni
            if (ext == ".ico")
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ico = new Icon(stream, new Size(48, 48));
                return ResizeImage(ico.ToBitmap(), 36, 36);
            }

            // Ekstraksi via Win32 ExtractIcon untuk executable/dll dengan icon index
            if (iconIndex != 0)
            {
                IntPtr hIcon = ExtractIcon(IntPtr.Zero, filePath, iconIndex >= 0 ? iconIndex : 0);
                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        using var ico = Icon.FromHandle(hIcon);
                        return ResizeImage(ico.ToBitmap(), 36, 36);
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }

            // Fallback ke Icon.ExtractAssociatedIcon
            using var assocIcon = Icon.ExtractAssociatedIcon(filePath);
            if (assocIcon != null)
            {
                return ResizeImage(assocIcon.ToBitmap(), 36, 36);
            }
        }
        catch { }

        return null;
    }

    private static Image? ExtractFromInstallLocation(string dirPath, string appName)
    {
        try
        {
            var dir = new DirectoryInfo(dirPath);

            // 1. Cek file .ico langsung (app.ico, icon.ico, favicon.ico, dll.)
            foreach (var icoFile in dir.EnumerateFiles("*.ico", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var stream = new FileStream(icoFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var ico = new Icon(stream, new Size(48, 48));
                    return ResizeImage(ico.ToBitmap(), 36, 36);
                }
                catch { }
            }

            // 2. Cek file .exe yang namanya cocok dengan nama aplikasi
            string cleanAppName = new string(appName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

            FileInfo? bestExe = null;
            foreach (var exeFile in dir.EnumerateFiles("*.exe", SearchOption.TopDirectoryOnly))
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(exeFile.Name).ToLowerInvariant();
                if (nameWithoutExt.StartsWith("unins") || nameWithoutExt.Contains("uninstall") || nameWithoutExt.Contains("update"))
                    continue;

                string cleanExe = new string(nameWithoutExt.Where(char.IsLetterOrDigit).ToArray());
                if (cleanExe.Contains(cleanAppName) || cleanAppName.Contains(cleanExe))
                {
                    bestExe = exeFile;
                    break;
                }
                bestExe ??= exeFile;
            }

            // Cek subfolder 1 tingkat ke dalam (misal bin, app-1.0.x, etc.)
            if (bestExe == null)
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                    // Cek .ico di subfolder
                    var subIco = sub.GetFiles("*.ico").FirstOrDefault();
                    if (subIco != null)
                    {
                        try
                        {
                            using var stream = new FileStream(subIco.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var ico = new Icon(stream, new Size(48, 48));
                            return ResizeImage(ico.ToBitmap(), 36, 36);
                        }
                        catch { }
                    }

                    foreach (var exeFile in sub.EnumerateFiles("*.exe"))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(exeFile.Name).ToLowerInvariant();
                        if (nameWithoutExt.StartsWith("unins") || nameWithoutExt.Contains("uninstall")) continue;

                        string cleanExe = new string(nameWithoutExt.Where(char.IsLetterOrDigit).ToArray());
                        if (cleanExe.Contains(cleanAppName) || cleanAppName.Contains(cleanExe))
                        {
                            bestExe = exeFile;
                            break;
                        }
                        bestExe ??= exeFile;
                    }
                    if (bestExe != null) break;
                }
            }

            if (bestExe != null)
            {
                using var ico = Icon.ExtractAssociatedIcon(bestExe.FullName);
                if (ico != null) return ResizeImage(ico.ToBitmap(), 36, 36);
            }
        }
        catch { }

        return null;
    }

    private static Image? ExtractFromStartMenu(string appName)
    {
        try
        {
            string[] searchDirs =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
            ];

            string cleanName = new string(appName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (cleanName.Length < 2) return null;

            foreach (var baseDir in searchDirs)
            {
                if (!Directory.Exists(baseDir)) continue;

                foreach (var lnk in Directory.EnumerateFiles(baseDir, "*.lnk", SearchOption.AllDirectories))
                {
                    string lnkName = Path.GetFileNameWithoutExtension(lnk).ToLowerInvariant();
                    string cleanLnk = new string(lnkName.Where(char.IsLetterOrDigit).ToArray());

                    if (cleanLnk.Equals(cleanName, StringComparison.OrdinalIgnoreCase) ||
                        cleanLnk.StartsWith(cleanName, StringComparison.OrdinalIgnoreCase) ||
                        cleanName.StartsWith(cleanLnk, StringComparison.OrdinalIgnoreCase))
                    {
                        using var ico = Icon.ExtractAssociatedIcon(lnk);
                        if (ico != null) return ResizeImage(ico.ToBitmap(), 36, 36);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static string? ExtractExeFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        string trimmed = command.Trim();
        if (trimmed.StartsWith('\"'))
        {
            int endQuote = trimmed.IndexOf('\"', 1);
            if (endQuote > 1) return trimmed.Substring(1, endQuote - 1);
        }

        int spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0 && trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Substring(0, spaceIdx);
        }

        return trimmed;
    }

    public static Bitmap ResizeImage(Image source, int width, int height)
    {
        var dest = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dest);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        // Pertahankan aspect ratio
        float ratio = Math.Min((float)width / source.Width, (float)height / source.Height);
        int targetW = Math.Max(1, (int)(source.Width * ratio));
        int targetH = Math.Max(1, (int)(source.Height * ratio));
        int posX = (width - targetW) / 2;
        int posY = (height - targetH) / 2;

        g.DrawImage(source, new Rectangle(posX, posY, targetW, targetH));
        return dest;
    }

    private static readonly string[] SearchRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
    ];

    private static Image? ExtractFromPublisherAndNameSearch(AppUninstallInfo app)
    {
        try
        {
            var candidateFolderNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(app.Publisher))
            {
                string cleanPub = app.Publisher.Replace(", Inc.", "").Replace(" Inc.", "").Replace(", LLC", "").Replace(" LLC", "").Replace(" Corporation", "").Trim();
                if (cleanPub.Length >= 3) candidateFolderNames.Add(cleanPub);
            }
            if (!string.IsNullOrWhiteSpace(app.DisplayName))
            {
                candidateFolderNames.Add(app.DisplayName);
                string firstTwoWords = string.Join(" ", app.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
                if (firstTwoWords.Length >= 3 && !candidateFolderNames.Contains(firstTwoWords)) candidateFolderNames.Add(firstTwoWords);
            }

            foreach (var root in SearchRoots)
            {
                if (!Directory.Exists(root)) continue;

                foreach (var cand in candidateFolderNames)
                {
                    string targetDir = Path.Combine(root, cand);
                    if (Directory.Exists(targetDir))
                    {
                        var img = ExtractFromInstallLocation(targetDir, app.DisplayName);
                        if (img != null) return img;
                    }

                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(root, $"*{cand}*", SearchOption.TopDirectoryOnly))
                        {
                            var img = ExtractFromInstallLocation(sub, app.DisplayName);
                            if (img != null) return img;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
    }

    private static Image? ExtractFromKnownBrandLocations(AppUninstallInfo app)
    {
        string name = (app.DisplayName + " " + app.Publisher).ToLowerInvariant();

        if (name.Contains("epic"))
        {
            string[] epicPaths =
            [
                @"C:\Program Files (x86)\Epic Games\Epic Online Services\EpicOnlineServicesUIHelper.exe",
                @"C:\Program Files (x86)\Epic Games\Epic Online Services\service\EpicOnlineServicesHost.exe",
                @"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
                @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe"
            ];
            foreach (var p in epicPaths)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        using var ico = Icon.ExtractAssociatedIcon(p);
                        if (ico != null) return ResizeImage(ico.ToBitmap(), 36, 36);
                    }
                    catch { }
                }
            }
        }

        if (name.Contains("steam"))
        {
            string steamPath = @"C:\Program Files (x86)\Steam\steam.exe";
            if (File.Exists(steamPath))
            {
                try
                {
                    using var ico = Icon.ExtractAssociatedIcon(steamPath);
                    if (ico != null) return ResizeImage(ico.ToBitmap(), 36, 36);
                }
                catch { }
            }
        }

        return null;
    }

    private static Image? ExtractFromWindowsInstallerCache(AppUninstallInfo app)
    {
        if (string.IsNullOrWhiteSpace(app.UninstallString)) return null;

        var match = System.Text.RegularExpressions.Regex.Match(app.UninstallString, @"\{[A-Fa-f0-9\-]{36}\}");
        if (!match.Success) return null;

        string guid = match.Value;
        string installerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");
        if (!Directory.Exists(installerDir)) return null;

        try
        {
            foreach (var file in Directory.EnumerateFiles(installerDir, $"*{guid}*"))
            {
                if (file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    using var ico = Icon.ExtractAssociatedIcon(file);
                    if (ico != null) return ResizeImage(ico.ToBitmap(), 36, 36);
                }
            }
        }
        catch { }

        return null;
    }

    public static Bitmap GenerateAppVectorLogo(AppUninstallInfo app)
    {
        var bmp = new Bitmap(36, 36, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        int hash = Math.Abs((app.DisplayName ?? "App").GetHashCode());
        Color gradTop, gradBottom, strokeColor;
        switch (hash % 5)
        {
            case 0:
                gradTop = Color.FromArgb(14, 165, 233);
                gradBottom = Color.FromArgb(30, 58, 138);
                strokeColor = Color.FromArgb(125, 211, 252);
                break;
            case 1:
                gradTop = Color.FromArgb(16, 185, 129);
                gradBottom = Color.FromArgb(6, 78, 59);
                strokeColor = Color.FromArgb(110, 231, 183);
                break;
            case 2:
                gradTop = Color.FromArgb(168, 85, 247);
                gradBottom = Color.FromArgb(88, 28, 135);
                strokeColor = Color.FromArgb(216, 180, 254);
                break;
            case 3:
                gradTop = Color.FromArgb(245, 158, 11);
                gradBottom = Color.FromArgb(120, 53, 15);
                strokeColor = Color.FromArgb(252, 211, 77);
                break;
            default:
                gradTop = Color.FromArgb(244, 63, 94);
                gradBottom = Color.FromArgb(136, 19, 55);
                strokeColor = Color.FromArgb(253, 164, 175);
                break;
        }

        var rect = new RectangleF(1f, 1f, 34f, 34f);
        using (var path = Theme.CreateRoundedRectangle(rect, 8f))
        {
            using (var lgb = new LinearGradientBrush(rect, gradTop, gradBottom, LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(lgb, path);
            }
            using var borderPen = new Pen(Color.FromArgb(180, strokeColor), 1.2f);
            g.DrawPath(borderPen, path);
        }

        string lower = (app.DisplayName ?? "").ToLowerInvariant();
        using var glyphPen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fillBrush = new SolidBrush(Color.FromArgb(220, Color.White));

        if (lower.Contains("game") || lower.Contains("epic") || lower.Contains("play") || lower.Contains("steam"))
        {
            PointF[] shield = [
                new PointF(18f, 9f),
                new PointF(26f, 12f),
                new PointF(24f, 22f),
                new PointF(18f, 27f),
                new PointF(12f, 22f),
                new PointF(10f, 12f),
                new PointF(18f, 9f)
            ];
            g.DrawLines(glyphPen, shield);
            g.FillPolygon(fillBrush, [
                new PointF(18f, 12f),
                new PointF(23f, 14f),
                new PointF(21f, 21f),
                new PointF(18f, 24f),
                new PointF(15f, 21f),
                new PointF(13f, 14f)
            ]);
        }
        else if (lower.Contains("code") || lower.Contains("dev") || lower.Contains("python") || lower.Contains("git") || lower.Contains("node"))
        {
            PointF[] leftBracket = [new PointF(15f, 13f), new PointF(11f, 18f), new PointF(15f, 23f)];
            PointF[] rightBracket = [new PointF(21f, 13f), new PointF(25f, 18f), new PointF(21f, 23f)];
            g.DrawLines(glyphPen, leftBracket);
            g.DrawLines(glyphPen, rightBracket);
            g.DrawLine(glyphPen, 19.5f, 13f, 16.5f, 23f);
        }
        else
        {
            float sq = 5.2f, gap = 2.4f, sx = 11.5f, sy = 11.5f;
            using var p1 = Theme.CreateRoundedRectangle(new RectangleF(sx, sy, sq, sq), 1.2f);
            using var p2 = Theme.CreateRoundedRectangle(new RectangleF(sx + sq + gap, sy, sq, sq), 1.2f);
            using var p3 = Theme.CreateRoundedRectangle(new RectangleF(sx, sy + sq + gap, sq, sq), 1.2f);
            using var p4 = Theme.CreateRoundedRectangle(new RectangleF(sx + sq + gap, sy + sq + gap, sq, sq), 1.2f);
            g.FillPath(fillBrush, p1);
            g.FillPath(fillBrush, p2);
            g.FillPath(fillBrush, p3);
            g.FillPath(fillBrush, p4);
        }

        return bmp;
    }
}
