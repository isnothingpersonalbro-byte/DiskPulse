using System.Diagnostics;
using DiskPulse.Models;

namespace DiskPulse.Core;

/// <summary>
/// Layanan penganalisis folder berat dengan pemindaian seluruh folder,
/// fitur drill-down rincian subfolder & file besar, serta proteksi anti-stuck.
/// </summary>
public class FolderAnalyzerService
{
    private string _driveRoot;

    public string DriveRoot
    {
        get => _driveRoot;
        set => _driveRoot = value.EndsWith('\\') ? value : value + "\\";
    }

    public FolderAnalyzerService(string driveRoot = "C:\\")
    {
        _driveRoot = driveRoot.EndsWith('\\') ? driveRoot : driveRoot + "\\";
    }

    /// <summary>
    /// Memindai drive target dan mengembalikan SEMUA folder (diurutkan dari ukuran terbesar ke terkecil).
    /// Tidak dibatasi hanya 10 besar saja.
    /// </summary>
    public async Task<List<FolderSizeItem>> AnalyzeAllFoldersAsync(
        string driveRoot,
        long totalDriveBytes,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _driveRoot = driveRoot.EndsWith('\\') ? driveRoot : driveRoot + "\\";

        return await Task.Run(() =>
        {
            var folderCandidates = CollectCandidateFolders(_driveRoot, progress, cancellationToken);
            var results = new List<FolderSizeItem>();

            int total = folderCandidates.Count;
            int count = 0;

            foreach (var folderPath in folderCandidates)
            {
                if (cancellationToken.IsCancellationRequested) break;

                count++;
                string folderName = Path.GetFileName(folderPath);
                if (string.IsNullOrEmpty(folderName)) folderName = folderPath;

                string statusPrefix = $"Analyzing ({count}/{total}): {folderName}";
                progress?.Report(statusPrefix);

                var (size, files) = CalculateDirectorySize(folderPath, statusPrefix, progress, cancellationToken);
                if (size > 0)
                {
                    double pct = totalDriveBytes > 0 ? ((double)size / totalDriveBytes) * 100.0 : 0;
                    results.Add(new FolderSizeItem
                    {
                        Path = folderPath,
                        Name = GetDisplayFolderName(folderPath),
                        TotalSizeBytes = size,
                        FileCount = files,
                        PercentageOfDrive = pct
                    });
                }
            }

            // Urutkan SEMUA folder dari yang terbesar ke terkecil
            var allSorted = results
                .OrderByDescending(f => f.TotalSizeBytes)
                .ToList();

            for (int i = 0; i < allSorted.Count; i++)
            {
                allSorted[i].Rank = i + 1;
            }

            return allSorted;
        }, cancellationToken);
    }

    // Alias untuk kompatibilitas
    public Task<List<FolderSizeItem>> AnalyzeTopFoldersAsync(
        string driveRoot,
        long totalDriveBytes,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return AnalyzeAllFoldersAsync(driveRoot, totalDriveBytes, progress, cancellationToken);
    }

    /// <summary>
    /// Fitur Drill-Down: Menganalisis rincian subfolder dan berkas-berkas besar langsung
    /// di dalam folder yang dipilih (misal AppData\Local) dan mengurutkannya dari yang terbesar.
    /// </summary>
    public async Task<List<FolderDetailItem>> InspectFolderContentsAsync(
        string folderPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var items = new List<FolderDetailItem>();

            try
            {
                var dirInfo = new DirectoryInfo(folderPath);
                if (!dirInfo.Exists) return items;

                progress?.Report($"Reading contents: {dirInfo.Name}...");

                // 1. Direct Files
                try
                {
                    foreach (var file in dirInfo.EnumerateFiles())
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        try
                        {
                            string ext = file.Extension.ToLowerInvariant();
                            string typeLabel = string.IsNullOrEmpty(ext) ? "File" : $"File ({ext})";

                            items.Add(new FolderDetailItem
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                IsDirectory = false,
                                TotalSizeBytes = file.Length,
                                FileCount = 1,
                                ItemType = typeLabel
                            });
                        }
                        catch { }
                    }
                }
                catch { }

                // 2. Direct Subfolders
                try
                {
                    var subDirs = dirInfo.GetDirectories();
                    int subIndex = 0;
                    int subTotal = subDirs.Length;

                    foreach (var sub in subDirs)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        subIndex++;

                        if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                            sub.Attributes.HasFlag(FileAttributes.Offline))
                        {
                            continue;
                        }

                        string prefix = $"Details ({subIndex}/{subTotal}): {sub.Name}";
                        progress?.Report(prefix);

                        var (subSize, fileCount) = CalculateDirectorySize(sub.FullName, prefix, progress, cancellationToken, 5000);
                        if (subSize > 0 || fileCount > 0)
                        {
                            items.Add(new FolderDetailItem
                            {
                                Name = sub.Name,
                                FullPath = sub.FullName,
                                IsDirectory = true,
                                TotalSizeBytes = subSize,
                                FileCount = fileCount,
                                ItemType = "Directory Folder"
                            });
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                progress?.Report($"Warning reading folder: {ex.Message}");
            }

            // Urutkan dari item yang memakan kapasitas paling besar
            return items
                .OrderByDescending(i => i.TotalSizeBytes)
                .ToList();
        }, cancellationToken);
    }

    private List<string> CollectCandidateFolders(string targetDrive, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            progress?.Report($"Collecting directories on {targetDrive}...");

            var rootDir = new DirectoryInfo(targetDrive);
            if (!rootDir.Exists) return candidates.ToList();

            bool isSystemDrive = targetDrive.StartsWith("C", StringComparison.OrdinalIgnoreCase);

            foreach (var dir in rootDir.GetDirectories())
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (dir.Name.StartsWith('$')) continue;
                if (dir.Name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;

                string fullPath = dir.FullName;
                string lowerName = dir.Name.ToLowerInvariant();

                if (isSystemDrive)
                {
                    if (lowerName == "users")
                    {
                        CollectUserSubFolders(fullPath, candidates, cancellationToken);
                    }
                    else if (lowerName == "program files" || lowerName == "program files (x86)")
                    {
                        CollectAppFolders(fullPath, candidates, cancellationToken);
                    }
                    else if (lowerName == "programdata")
                    {
                        CollectAppFolders(fullPath, candidates, cancellationToken);
                    }
                    else if (lowerName == "windows")
                    {
                        CollectWindowsSubFolders(fullPath, candidates, cancellationToken);
                    }
                    else
                    {
                        candidates.Add(fullPath);
                    }
                }
                else
                {
                    candidates.Add(fullPath);

                    try
                    {
                        foreach (var sub in dir.GetDirectories())
                        {
                            if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint) && !sub.Name.StartsWith('.'))
                            {
                                candidates.Add(sub.FullName);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"Peringatan: {ex.Message}");
        }

        return candidates.ToList();
    }

    private void CollectUserSubFolders(string usersPath, HashSet<string> candidates, CancellationToken cancellationToken)
    {
        try
        {
            var usersDir = new DirectoryInfo(usersPath);
            foreach (var userDir in usersDir.GetDirectories())
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (userDir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (userDir.Name.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                    userDir.Name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                    userDir.Name.Equals("Default User", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var sub in userDir.GetDirectories())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                    if (sub.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
                    {
                        string local = Path.Combine(sub.FullName, "Local");
                        if (Directory.Exists(local)) candidates.Add(local);
                        string roaming = Path.Combine(sub.FullName, "Roaming");
                        if (Directory.Exists(roaming)) candidates.Add(roaming);
                    }
                    else
                    {
                        candidates.Add(sub.FullName);
                    }
                }
            }
        }
        catch { }
    }

    private void CollectAppFolders(string basePath, HashSet<string> candidates, CancellationToken cancellationToken)
    {
        try
        {
            var dir = new DirectoryInfo(basePath);
            foreach (var sub in dir.GetDirectories())
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                candidates.Add(sub.FullName);
            }
        }
        catch { }
    }

    private void CollectWindowsSubFolders(string winPath, HashSet<string> candidates, CancellationToken cancellationToken)
    {
        try
        {
            string[] winBigDirs = ["SoftwareDistribution", "Installer", "Temp", "Logs"];
            foreach (var item in winBigDirs)
            {
                string p = Path.Combine(winPath, item);
                if (Directory.Exists(p)) candidates.Add(p);
            }
        }
        catch { }
    }

    public static (long totalBytes, int fileCount) CalculateDirectorySize(
        string rootPath,
        string statusPrefix = "",
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        int timeoutMs = 8000)
    {
        long totalSize = 0;
        int fileCount = 0;

        var stack = new Stack<string>();
        stack.Push(rootPath);

        var stopwatch = Stopwatch.StartNew();
        int lastReportCount = 0;

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (stopwatch.ElapsedMilliseconds > timeoutMs) break;

            string current = stack.Pop();

            try
            {
                var dir = new DirectoryInfo(current);

                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    dir.Attributes.HasFlag(FileAttributes.Offline))
                {
                    continue;
                }

                foreach (var f in dir.EnumerateFiles())
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (stopwatch.ElapsedMilliseconds > 8000) break;

                    try
                    {
                        totalSize += f.Length;
                        fileCount++;
                    }
                    catch { }
                }

                if (fileCount - lastReportCount >= 600 && progress != null && !string.IsNullOrEmpty(statusPrefix))
                {
                    lastReportCount = fileCount;
                    progress.Report($"{statusPrefix} [{fileCount:N0} files...]");
                }

                foreach (var s in dir.EnumerateDirectories())
                {
                    try
                    {
                        if (!s.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                            !s.Attributes.HasFlag(FileAttributes.Offline))
                        {
                            stack.Push(s.FullName);
                        }
                    }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
            catch (PathTooLongException) { }
            catch (IOException) { }
        }

        return (totalSize, fileCount);
    }

    private string GetDisplayFolderName(string fullPath)
    {
        try
        {
            return fullPath.Replace(_driveRoot, "", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return Path.GetFileName(fullPath);
        }
    }
}
