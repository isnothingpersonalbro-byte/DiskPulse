using System.Diagnostics;
using DiskPulse.Models;
using Microsoft.Win32;

namespace DiskPulse.Core;

/// <summary>
/// Layanan deteksi aplikasi terpasang di Windows melalui Registry dan berkas uninstaller lokal.
/// Memungkinkan penghapusan aplikasi secara resmi melalui perintah uninstaller bawaan program.
/// </summary>
public class AppUninstallerService
{
    private static readonly string[] RegistryUninstallKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private static readonly string[] LocalUninstallerNames =
    [
        "unins000.exe",
        "unins001.exe",
        "uninstall.exe",
        "Uninstall.exe",
        "uninst.exe",
        "uninstaller.exe",
        "setup.exe"
    ];

    /// <summary>
    /// Mencari apakah folder atau berkas yang dipilih merupakan bagian dari aplikasi yang terdaftar di Windows.
    /// </summary>
    public AppUninstallInfo FindAppForPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return new AppUninstallInfo();

        string normTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 1. Cek keberadaan berkas uninstaller lokal di dalam direktori
        string? localUninstaller = FindLocalUninstaller(normTarget);

        // 2. Cek registri Windows (HKLM dan HKCU)
        var regApp = ScanRegistryForPath(normTarget);
        if (regApp != null)
        {
            if (string.IsNullOrEmpty(regApp.LocalUninstallerPath) && !string.IsNullOrEmpty(localUninstaller))
            {
                regApp.LocalUninstallerPath = localUninstaller;
            }
            return regApp;
        }

        // 3. Jika tidak ditemukan di Registry namun memiliki berkas uninstaller lokal
        if (!string.IsNullOrEmpty(localUninstaller))
        {
            string folderName = Path.GetFileName(normTarget);
            return new AppUninstallInfo
            {
                IsRecognizedApp = true,
                DisplayName = folderName,
                InstallLocation = normTarget,
                LocalUninstallerPath = localUninstaller
            };
        }

        // 4. Deteksi apakah folder ini berisi berkas eksekutabel (.exe) atau merupakan folder software/game
        string name = Path.GetFileName(normTarget);
        if (string.IsNullOrEmpty(name)) name = normTarget;

        bool hasExe = HasExecutableFiles(normTarget);
        bool isAppDir = normTarget.Contains(@"\Program Files", StringComparison.OrdinalIgnoreCase) ||
                        normTarget.Contains(@"\AppData\Local\Programs", StringComparison.OrdinalIgnoreCase) ||
                        normTarget.Contains(@"\ProgramData", StringComparison.OrdinalIgnoreCase) ||
                        hasExe;

        if (isAppDir && Directory.Exists(normTarget))
        {
            return new AppUninstallInfo
            {
                IsRecognizedApp = true,
                DisplayName = name,
                InstallLocation = normTarget
            };
        }

        return new AppUninstallInfo
        {
            DisplayName = name,
            InstallLocation = normTarget
        };
    }

    private static bool HasExecutableFiles(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath)) return false;
            var dir = new DirectoryInfo(dirPath);
            foreach (var f in dir.EnumerateFiles("*.exe"))
            {
                return true;
            }
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var f in sub.EnumerateFiles("*.exe"))
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private string? FindLocalUninstaller(string dirPath)
    {
        try
        {
            if (!Directory.Exists(dirPath))
            {
                string? parent = Path.GetDirectoryName(dirPath);
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return null;
                dirPath = parent;
            }

            var dir = new DirectoryInfo(dirPath);

            // 1. Cek langsung nama-nama uninstaller populer
            foreach (var name in LocalUninstallerNames)
            {
                string p = Path.Combine(dirPath, name);
                if (File.Exists(p)) return p;
            }

            // 2. Cek berkas dengan pola *unins*.exe di direktori ini
            foreach (var file in dir.EnumerateFiles("*unins*.exe"))
            {
                return file.FullName;
            }

            // 3. Cek subdirektori 1 tingkat ke dalam (misal Support, bin, etc.)
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var name in LocalUninstallerNames)
                {
                    string p = Path.Combine(sub.FullName, name);
                    if (File.Exists(p)) return p;
                }
                foreach (var file in sub.EnumerateFiles("*unins*.exe"))
                {
                    return file.FullName;
                }
            }
        }
        catch { }

        return null;
    }

    private AppUninstallInfo? ScanRegistryForPath(string normTarget)
    {
        try
        {
            // Cek HKLM (64-bit & 32-bit WOW64)
            foreach (var regPath in RegistryUninstallKeys)
            {
                var app = CheckRegistryKey(Registry.LocalMachine, regPath, normTarget);
                if (app != null) return app;
            }

            // Cek HKCU (User Current Version)
            foreach (var regPath in RegistryUninstallKeys)
            {
                var app = CheckRegistryKey(Registry.CurrentUser, regPath, normTarget);
                if (app != null) return app;
            }
        }
        catch { }

        return null;
    }

    private AppUninstallInfo? CheckRegistryKey(RegistryKey rootKey, string subKeyPath, string normTarget)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath);
            if (key == null) return null;

            foreach (var subName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subName);
                    if (subKey == null) continue;

                    string? displayName = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    string? installLoc = subKey.GetValue("InstallLocation") as string;
                    string? uninstallStr = subKey.GetValue("UninstallString") as string;
                    string? quietUninstallStr = subKey.GetValue("QuietUninstallString") as string;
                    string? displayIcon = subKey.GetValue("DisplayIcon") as string;
                    string? version = subKey.GetValue("DisplayVersion") as string;
                    string? publisher = subKey.GetValue("Publisher") as string;

                    // Normalisasi InstallLocation
                    if (!string.IsNullOrWhiteSpace(installLoc))
                    {
                        string normInstall = Path.GetFullPath(installLoc).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        if (normTarget.Equals(normInstall, StringComparison.OrdinalIgnoreCase) ||
                            normTarget.StartsWith(normInstall + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                            normInstall.StartsWith(normTarget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            return new AppUninstallInfo
                            {
                                IsRecognizedApp = true,
                                DisplayName = displayName,
                                DisplayVersion = version,
                                Publisher = publisher,
                                InstallLocation = normInstall,
                                DisplayIcon = displayIcon,
                                UninstallString = !string.IsNullOrWhiteSpace(uninstallStr) ? uninstallStr : quietUninstallStr
                            };
                        }
                    }

                    // Cek DisplayIcon atau UninstallString yang merujuk ke folder target
                    if (!string.IsNullOrWhiteSpace(displayIcon) && displayIcon.Contains(normTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        return new AppUninstallInfo
                        {
                            IsRecognizedApp = true,
                            DisplayName = displayName,
                            DisplayVersion = version,
                            Publisher = publisher,
                            InstallLocation = normTarget,
                            DisplayIcon = displayIcon,
                            UninstallString = !string.IsNullOrWhiteSpace(uninstallStr) ? uninstallStr : quietUninstallStr
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(uninstallStr) && uninstallStr.Contains(normTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        return new AppUninstallInfo
                        {
                            IsRecognizedApp = true,
                            DisplayName = displayName,
                            DisplayVersion = version,
                            Publisher = publisher,
                            InstallLocation = normTarget,
                            DisplayIcon = displayIcon,
                            UninstallString = uninstallStr
                        };
                    }
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Menjalankan uninstaller resmi dari aplikasi.
    /// </summary>
    public bool LaunchUninstall(AppUninstallInfo appInfo, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            // 1. Jalankan uninstaller lokal jika ada
            if (!string.IsNullOrEmpty(appInfo.LocalUninstallerPath) && File.Exists(appInfo.LocalUninstallerPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = appInfo.LocalUninstallerPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(appInfo.LocalUninstallerPath)
                };
                Process.Start(psi);
                return true;
            }

            // 2. Jalankan UninstallString dari Registry
            if (!string.IsNullOrEmpty(appInfo.UninstallString))
            {
                string raw = appInfo.UninstallString.Trim();
                string exe;
                string args = "";

                if (raw.StartsWith('\"'))
                {
                    int endQuote = raw.IndexOf('\"', 1);
                    if (endQuote > 1)
                    {
                        exe = raw.Substring(1, endQuote - 1);
                        args = raw.Substring(endQuote + 1).Trim();
                    }
                    else
                    {
                        exe = raw.Trim('\"');
                    }
                }
                else
                {
                    int spaceIdx = raw.IndexOf(' ');
                    if (spaceIdx > 0 && (raw.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase) || raw.Contains(':')))
                    {
                        exe = raw.Substring(0, spaceIdx);
                        args = raw.Substring(spaceIdx + 1).Trim();
                    }
                    else
                    {
                        exe = raw;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = true
                };
                Process.Start(psi);
                return true;
            }

            // 3. Jika tidak ada uninstaller langsung, buka pengaturan aplikasi Windows
            OpenWindowsInstalledApps();
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Membuka halaman "Installed Apps" pada Windows Settings atau Program & Features.
    /// </summary>
    public static void OpenWindowsInstalledApps()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:appsfeatures",
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "control.exe",
                    Arguments = "appwiz.cpl",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    /// <summary>
    /// Mengambil daftar seluruh aplikasi terpasang di sistem dari Registry Windows (HKLM & HKCU).
    /// </summary>
    public List<AppUninstallInfo> GetInstalledApps(string? targetDrive = null)
    {
        var list = new Dictionary<string, AppUninstallInfo>(StringComparer.OrdinalIgnoreCase);

        void ScanKey(RegistryKey rootKey, string subKeyPath)
        {
            try
            {
                using var key = rootKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subName);
                        if (subKey == null) continue;

                        string? displayName = subKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;

                        // Abaikan update, hotfix, atau komponen internal yang bukan aplikasi pengguna
                        if (displayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && displayName.Length < 12) continue;
                        if (subKey.GetValue("SystemComponent") is int sysComp && sysComp == 1) continue;
                        if (subKey.GetValue("ParentKeyName") != null) continue;

                        string? installLoc = subKey.GetValue("InstallLocation") as string;
                        string? unins = subKey.GetValue("UninstallString") as string;
                        string? pub = subKey.GetValue("Publisher") as string;
                        string? ver = subKey.GetValue("DisplayVersion") as string;
                        string? displayIcon = subKey.GetValue("DisplayIcon") as string;

                        long estBytes = 0;
                        if (subKey.GetValue("EstimatedSize") is int estKb && estKb > 0)
                        {
                            estBytes = estKb * 1024L;
                        }
                        else if (subKey.GetValue("EstimatedSize") is long estLong && estLong > 0)
                        {
                            estBytes = estLong * 1024L;
                        }

                        // Filter drive jika ditentukan
                        if (!string.IsNullOrEmpty(targetDrive) && !string.IsNullOrEmpty(installLoc))
                        {
                            if (!installLoc.StartsWith(targetDrive, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }

                        if (!list.TryGetValue(displayName, out var app))
                        {
                            app = new AppUninstallInfo
                            {
                                IsRecognizedApp = true,
                                DisplayName = displayName.Trim(),
                                DisplayVersion = ver,
                                Publisher = pub,
                                InstallLocation = installLoc,
                                DisplayIcon = displayIcon,
                                UninstallString = unins,
                                EstimatedSizeBytes = estBytes
                            };
                            list[displayName] = app;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(app.DisplayIcon) && !string.IsNullOrEmpty(displayIcon)) app.DisplayIcon = displayIcon;
                            if (string.IsNullOrEmpty(app.UninstallString) && !string.IsNullOrEmpty(unins)) app.UninstallString = unins;
                            if (string.IsNullOrEmpty(app.InstallLocation) && !string.IsNullOrEmpty(installLoc)) app.InstallLocation = installLoc;
                            if (app.EstimatedSizeBytes == 0 && estBytes > 0) app.EstimatedSizeBytes = estBytes;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        foreach (var regPath in RegistryUninstallKeys)
        {
            ScanKey(Registry.LocalMachine, regPath);
            ScanKey(Registry.CurrentUser, regPath);
        }

        return list.Values
            .OrderByDescending(a => a.EstimatedSizeBytes)
            .ThenBy(a => a.DisplayName)
            .ToList();
    }
}
