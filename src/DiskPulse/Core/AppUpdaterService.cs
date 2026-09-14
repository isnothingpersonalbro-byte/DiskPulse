using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiskPulse.Models;

namespace DiskPulse.Core;

public class WingetUpgradeItem
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Layanan pendeteksian dan pembaruan aplikasi terpasang di Windows berbasis Windows Package Manager (winget).
/// Menyertakan verifikasi konektivitas internet, pemindaian rilis baru secara asinkron,
/// pencocokan pintar terhadap registry aplikasi lokal, dan eksekusi upgrade 1-klik resmi.
/// </summary>
public static class AppUpdaterService
{
    /// <summary>
    /// Memeriksa apakah komputer terhubung ke jaringan internet aktif.
    /// </summary>
    public static bool IsInternetAvailable()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return false;
            }

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                {
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count > 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Fallback aman
        }

        return NetworkInterface.GetIsNetworkAvailable();
    }

    /// <summary>
    /// Memeriksa ketersediaan pembaruan aplikasi secara online melalui perintah winget upgrade.
    /// </summary>
    public static async Task<List<WingetUpgradeItem>> CheckAvailableUpgradesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<WingetUpgradeItem>();

        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = "upgrade --include-unknown",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            string output = await outputTask;
            result = ParseWingetOutput(output);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppUpdaterService] winget check failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Mem-parsing output tabular dari 'winget upgrade' secara akurat berbasis indeks kolom.
    /// </summary>
    public static List<WingetUpgradeItem> ParseWingetOutput(string output)
    {
        var list = new List<WingetUpgradeItem>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        int idCol = -1;
        int verCol = -1;
        int availCol = -1;
        int sourceCol = -1;
        bool headerFound = false;

        foreach (var rawLine in lines)
        {
            string line = rawLine;

            // 1. Temukan baris header kolom
            if (!headerFound)
            {
                if (line.Contains("Name") && line.Contains("Id") && line.Contains("Version") && line.Contains("Available"))
                {
                    idCol = line.IndexOf("Id", StringComparison.Ordinal);
                    verCol = line.IndexOf("Version", StringComparison.Ordinal);
                    availCol = line.IndexOf("Available", StringComparison.Ordinal);
                    sourceCol = line.IndexOf("Source", StringComparison.Ordinal);

                    if (idCol > 0 && verCol > idCol && availCol > verCol)
                    {
                        headerFound = true;
                    }
                }
                continue;
            }

            // 2. Lewati garis pembatas pemisah (-----)
            if (line.StartsWith("---") || line.StartsWith("==="))
            {
                continue;
            }

            // 3. Lewati baris ringkasan di akhir
            if (line.Contains("upgrades available") || line.Contains("upgrade available") || line.Contains("package(s) have"))
            {
                continue;
            }

            // 4. Pastikan panjang baris cukup untuk membaca kolom
            if (line.Length <= availCol)
            {
                continue;
            }

            try
            {
                string name = line.Substring(0, idCol).Trim();
                string id = (verCol > idCol && line.Length >= verCol)
                    ? line.Substring(idCol, verCol - idCol).Trim()
                    : line.Substring(idCol).Trim();

                string ver = (availCol > verCol && line.Length >= availCol)
                    ? line.Substring(verCol, availCol - verCol).Trim()
                    : "";

                string avail = (sourceCol > availCol && line.Length >= sourceCol)
                    ? line.Substring(availCol, sourceCol - availCol).Trim()
                    : (line.Length > availCol ? line.Substring(availCol).Trim() : "");

                string source = (sourceCol > 0 && line.Length > sourceCol)
                    ? line.Substring(sourceCol).Trim()
                    : "";

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(avail))
                {
                    list.Add(new WingetUpgradeItem
                    {
                        Name = name,
                        Id = id,
                        Version = ver,
                        AvailableVersion = avail,
                        Source = source
                    });
                }
            }
            catch
            {
                // Lewati baris yang tidak sesuai format
            }
        }

        return list;
    }

    /// <summary>
    /// Mencocokkan daftar pembaruan winget dengan aplikasi terpasang di sistem.
    /// Mengembalikan jumlah aplikasi yang memiliki pembaruan tersedia.
    /// </summary>
    public static int MatchUpgrades(List<AppUninstallInfo> apps, List<WingetUpgradeItem> upgrades)
    {
        if (apps.Count == 0 || upgrades.Count == 0) return 0;

        int matchedCount = 0;

        foreach (var app in apps)
        {
            // Reset status pembaruan sebelumnya
            app.AvailableVersion = null;
            app.WingetId = null;

            string appNorm = NormalizeName(app.DisplayName);

            foreach (var upg in upgrades)
            {
                string upgNorm = NormalizeName(upg.Name);

                bool isMatch = false;

                // 1. Pencocokan nama persis (normalized)
                if (string.Equals(appNorm, upgNorm, StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = true;
                }
                // 2. Pencocokan ID terhadap DisplayName atau Publisher
                else if (!string.IsNullOrEmpty(upg.Id) &&
                         (app.DisplayName.Contains(upg.Id, StringComparison.OrdinalIgnoreCase) ||
                          (!string.IsNullOrEmpty(app.Publisher) && upg.Id.Contains(app.Publisher, StringComparison.OrdinalIgnoreCase))))
                {
                    isMatch = true;
                }
                // 3. Pencocokan awalan / substring yang kuat
                else if (appNorm.Length >= 4 && upgNorm.Length >= 4)
                {
                    if (appNorm.StartsWith(upgNorm, StringComparison.OrdinalIgnoreCase) ||
                        upgNorm.StartsWith(appNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                    }
                }

                if (isMatch)
                {
                    app.AvailableVersion = upg.AvailableVersion;
                    app.WingetId = upg.Id;
                    matchedCount++;
                    break;
                }
            }
        }

        return matchedCount;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Bersihkan tanda arsitektur, tanda kurung, dan kata pengisi umum
        string cleaned = name
            .Replace("(x64)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(x86)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(64-bit)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(32-bit)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(User)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("en-US", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return cleaned;
    }

    /// <summary>
    /// Menjalankan pembaruan aplikasi secara senyap (in-app silent update) tanpa jendela CMD,
    /// dengan streaming status progres unduh dan instalasi secara real-time.
    /// </summary>
    public static async Task<bool> UpgradeAppSilentAsync(
        AppUninstallInfo app,
        Action<string> onStatusChanged,
        CancellationToken cancellationToken = default)
    {
        string targetId = !string.IsNullOrEmpty(app.WingetId) ? app.WingetId : app.DisplayName;

        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = $"upgrade --id \"{targetId}\" --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        app.IsUpdating = true;
        app.UpdateStatusText = "Downloading...";
        onStatusChanged("Downloading...");

        try
        {
            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                string line = e.Data.Trim();
                Debug.WriteLine($"[winget update] {line}");

                if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains(" MB /", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("%", StringComparison.OrdinalIgnoreCase))
                {
                    app.UpdateStatusText = "Downloading...";
                    onStatusChanged("Downloading...");
                }
                else if (line.Contains("Installing", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Starting package install", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Applying", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Verifying", StringComparison.OrdinalIgnoreCase))
                {
                    app.UpdateStatusText = "Installing...";
                    onStatusChanged("Installing...");
                }
                else if (line.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Success", StringComparison.OrdinalIgnoreCase))
                {
                    app.UpdateStatusText = "Updated!";
                    onStatusChanged("Updated!");
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Debug.WriteLine($"[winget err] {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            bool success = process.ExitCode == 0;
            if (success)
            {
                app.UpdateStatusText = "Updated!";
                onStatusChanged("Updated!");
                if (!string.IsNullOrEmpty(app.AvailableVersion))
                {
                    app.DisplayVersion = app.AvailableVersion;
                }
            }
            else
            {
                app.UpdateStatusText = "Failed";
                onStatusChanged("Failed");
            }

            return success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppUpdaterService] UpgradeAppSilentAsync failed: {ex.Message}");
            app.UpdateStatusText = "Failed";
            onStatusChanged("Failed");
            return false;
        }
    }

    /// <summary>
    /// Menjalankan pembaruan aplikasi resmi melalui winget upgrade secara aman di latar belakang.
    /// </summary>
    public static Process? LaunchAppUpgrade(AppUninstallInfo app)
    {
        if (string.IsNullOrEmpty(app.WingetId))
        {
            // Jika ID tidak ada, gunakan DisplayName
            app.WingetId = app.DisplayName;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = $"upgrade --id \"{app.WingetId}\" --accept-source-agreements --accept-package-agreements",
            UseShellExecute = true, // Memungkinkan dialog UAC resmi muncul bila diperlukan hak administrator
            CreateNoWindow = false
        };

        try
        {
            return Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppUpdaterService] LaunchAppUpgrade failed: {ex.Message}");
            return null;
        }
    }
}
