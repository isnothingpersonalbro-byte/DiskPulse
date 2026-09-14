using System.Runtime.InteropServices;
using DiskPulse.Models;

namespace DiskPulse.Core;

/// <summary>
/// Layanan pemindai berkas sampah cerdas dengan dukungan multi-drive.
/// </summary>
public class JunkScannerService
{
    public List<JunkItemCategory> GetCategoriesForDrive(string driveLetter)
    {
        string drive = driveLetter.EndsWith('\\') ? driveLetter : driveLetter + "\\";
        bool isSystemDrive = drive.StartsWith("C", StringComparison.OrdinalIgnoreCase);

        if (isSystemDrive)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            return
            [
                new JunkItemCategory
                {
                    Id = "user_temp",
                    Name = "User Temporary Files (%TEMP%)",
                    Description = "Temporary files left by user applications in %TEMP%",
                    TargetDirectories = [Path.GetTempPath()]
                },
                new JunkItemCategory
                {
                    Id = "win_temp",
                    Name = "System Temporary Files (Windows Temp)",
                    Description = "Temporary files created by system services and installers",
                    TargetDirectories = [Path.Combine(winDir, "Temp")]
                },
                new JunkItemCategory
                {
                    Id = "wu_cache",
                    Name = "Windows Update Download Cache",
                    Description = "Outdated installation packages applied by Windows Update",
                    TargetDirectories = [Path.Combine(winDir, "SoftwareDistribution", "Download")]
                },
                new JunkItemCategory
                {
                    Id = "thumb_cache",
                    Name = "Windows Explorer Thumbnail Cache",
                    Description = "Image and video preview database cache files (thumbcache_*.db)",
                    TargetDirectories = [Path.Combine(localAppData, "Microsoft", "Windows", "Explorer")]
                },
                new JunkItemCategory
                {
                    Id = "shader_cache",
                    Name = "DirectX / GPU Shader Cache",
                    Description = "Compiled graphics shaders cache (Direct3D, NVIDIA, AMD)",
                    TargetDirectories =
                    [
                        Path.Combine(localAppData, "D3DSCache"),
                        Path.Combine(localAppData, "NVIDIA", "DXCache"),
                        Path.Combine(localAppData, "AMD", "DxCache")
                    ]
                },
                new JunkItemCategory
                {
                    Id = "crash_dumps",
                    Name = "Crash Dumps & Error Reports",
                    Description = "Application crash memory dumps and Windows Error Reporting logs",
                    TargetDirectories =
                    [
                        Path.Combine(localAppData, "CrashDumps"),
                        Path.Combine(commonAppData, "Microsoft", "Windows", "WER", "ReportArchive"),
                        Path.Combine(commonAppData, "Microsoft", "Windows", "WER", "ReportQueue")
                    ]
                },
                new JunkItemCategory
                {
                    Id = "delivery_opt",
                    Name = "Delivery Optimization Cache",
                    Description = "Local network delivery optimization update cache",
                    TargetDirectories = [Path.Combine(winDir, "SoftwareDistribution", "DeliveryOptimization")]
                },
                new JunkItemCategory
                {
                    Id = "recycle_bin",
                    Name = $"Recycle Bin ({drive})",
                    Description = $"Deleted files currently in the Recycle Bin on {drive}",
                    IsRecycleBin = true,
                    TargetDirectories = [drive]
                }
            ];
        }
        else
        {
            // For secondary drives (D:, E:, etc.)
            return
            [
                new JunkItemCategory
                {
                    Id = "recycle_bin",
                    Name = $"Recycle Bin ({drive})",
                    Description = $"Deleted files currently in the Recycle Bin on {drive}",
                    IsRecycleBin = true,
                    TargetDirectories = [drive]
                },
                new JunkItemCategory
                {
                    Id = "drive_temp",
                    Name = $"Temporary Files ({drive}Temp)",
                    Description = $"Temporary files and installer leftovers on {drive}",
                    TargetDirectories =
                    [
                        Path.Combine(drive, "Temp"),
                        Path.Combine(drive, "tmp"),
                        Path.Combine(drive, "Found.000")
                    ]
                },
                new JunkItemCategory
                {
                    Id = "drive_cache",
                    Name = $"Cache & Download Logs ({drive})",
                    Description = $"Old download logs and application cache on {drive}",
                    TargetDirectories =
                    [
                        Path.Combine(drive, "Logs"),
                        Path.Combine(drive, "Cache"),
                        Path.Combine(drive, "Downloads", "Temp")
                    ]
                }
            ];
        }
    }

    public async Task ScanCategoryAsync(
        JunkItemCategory category,
        string driveRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        category.TotalSizeBytes = 0;
        category.FileCount = 0;
        category.IsScanning = true;

        await Task.Run(() =>
        {
            try
            {
                if (category.IsRecycleBin)
                {
                    progress?.Report($"Checking Recycle Bin ({driveRoot})...");
                    ScanRecycleBin(category, driveRoot);
                    return;
                }

                foreach (var dirPath in category.TargetDirectories)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (!Directory.Exists(dirPath)) continue;

                    progress?.Report($"Scanning: {dirPath}");
                    ScanDirectory(dirPath, category, cancellationToken);
                }
            }
            finally
            {
                category.IsScanning = false;
            }
        }, cancellationToken);
    }

    private void ScanDirectory(string dirPath, JunkItemCategory category, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(dirPath);

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) return;
            string currentDir = stack.Pop();

            try
            {
                var dirInfo = new DirectoryInfo(currentDir);
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                FileInfo[] files = (category.Id == "thumb_cache")
                    ? dirInfo.GetFiles("thumbcache_*.db")
                    : dirInfo.GetFiles();

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    try
                    {
                        category.TotalSizeBytes += file.Length;
                        category.FileCount++;
                    }
                    catch { }
                }

                string[] subDirs = Directory.GetDirectories(currentDir);
                foreach (var sub in subDirs)
                {
                    try
                    {
                        var subInfo = new DirectoryInfo(sub);
                        if (!subInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            stack.Push(sub);
                        }
                    }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
        }
    }

    private void ScanRecycleBin(JunkItemCategory category, string driveRoot)
    {
        try
        {
            var rbInfo = new NativeMethods.SHQUERYRBINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>()
            };

            int hr = NativeMethods.SHQueryRecycleBin(driveRoot, ref rbInfo);
            if (hr == 0)
            {
                category.TotalSizeBytes = rbInfo.i64Size;
                category.FileCount = (int)Math.Min(rbInfo.i64NumItems, int.MaxValue);
            }
        }
        catch
        {
            category.TotalSizeBytes = 0;
            category.FileCount = 0;
        }
    }
}
