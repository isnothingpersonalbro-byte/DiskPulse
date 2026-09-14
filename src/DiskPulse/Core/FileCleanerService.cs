using System.Diagnostics;
using DiskPulse.Models;

namespace DiskPulse.Core;

/// <summary>
/// Layanan pembersihan berkas aman dengan dukungan Dry-Run (simulasi),
/// pencegahan kerusakan direktori sistem operasi, dan penanganan berkas terkunci.
/// </summary>
public class FileCleanerService
{
    /// <summary>
    /// Membersihkan kategori-kategori yang dipilih secara asinkron.
    /// </summary>
    public async Task<CleanResult> CleanCategoriesAsync(
        IEnumerable<JunkItemCategory> categories,
        string driveRoot,
        bool isDryRun,
        IProgress<(string message, int progressPercentage)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new CleanResult
        {
            IsDryRun = isDryRun
        };

        var stopwatch = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            var selectedCategories = categories.Where(c => c.IsSelected).ToList();
            int totalCategories = selectedCategories.Count;
            int currentCatIndex = 0;

            foreach (var category in selectedCategories)
            {
                if (cancellationToken.IsCancellationRequested) break;

                currentCatIndex++;
                int baseProgress = (int)(((double)(currentCatIndex - 1) / totalCategories) * 100);
                progress?.Report(($"Processing: {category.Name}...", baseProgress));

                if (category.IsRecycleBin)
                {
                    CleanRecycleBin(category, driveRoot, isDryRun, result, progress);
                    continue;
                }

                CleanDirectoryCategory(category, isDryRun, result, progress, cancellationToken);
            }

            stopwatch.Stop();
            result.ElapsedTime = stopwatch.Elapsed;
        }, cancellationToken);

        return result;
    }

    private void CleanRecycleBin(
        JunkItemCategory category,
        string driveRoot,
        bool isDryRun,
        CleanResult result,
        IProgress<(string message, int progressPercentage)>? progress)
    {
        if (isDryRun)
        {
            progress?.Report(($"[SIMULATION] Calculating Recycle Bin {driveRoot}...", 50));
            result.BytesFreed += category.TotalSizeBytes;
            result.FilesDeleted += category.FileCount;
            return;
        }

        try
        {
            progress?.Report(($"Emptying Recycle Bin {driveRoot}...", 50));
            uint flags = NativeMethods.SHERB_NOCONFIRMATION |
                         NativeMethods.SHERB_NOPROGRESSUI |
                         NativeMethods.SHERB_NOSOUND;

            int hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, driveRoot, flags);
            if (hr == 0)
            {
                result.BytesFreed += category.TotalSizeBytes;
                result.FilesDeleted += category.FileCount;
                category.TotalSizeBytes = 0;
                category.FileCount = 0;
            }
            else
            {
                result.ErrorsCount++;
                result.SkippedReasons.Add("Failed to empty Recycle Bin (Win32 HRESULT: " + hr + ")");
            }
        }
        catch (Exception ex)
        {
            result.ErrorsCount++;
            result.SkippedReasons.Add($"Recycle Bin Error: {ex.Message}");
        }
    }

    private void CleanDirectoryCategory(
        JunkItemCategory category,
        bool isDryRun,
        CleanResult result,
        IProgress<(string message, int progressPercentage)>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var rootDir in category.TargetDirectories)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (!Directory.Exists(rootDir)) continue;

            // Kumpulkan semua file di direktori
            var files = new List<FileInfo>();
            CollectFiles(rootDir, files, cancellationToken);

            int fileIndex = 0;
            int totalFiles = files.Count;

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                fileIndex++;

                // System Safety: Check whitelist and blacklist
                if (!SystemSafetyGuard.IsSafeToDelete(file.FullName, category.TargetDirectories))
                {
                    result.FilesSkipped++;
                    result.SkippedReasons.Add($"[PROTECTED] Skipped protected system file: {file.Name}");
                    continue;
                }

                long fileLength = 0;
                try { fileLength = file.Length; } catch { }

                if (isDryRun)
                {
                    result.BytesFreed += fileLength;
                    result.FilesDeleted++;
                    continue;
                }

                // Execute real deletion with locked file handling
                try
                {
                    // Remove Read-Only attribute if present
                    if (file.IsReadOnly)
                    {
                        file.Attributes = FileAttributes.Normal;
                    }

                    file.Delete();
                    result.BytesFreed += fileLength;
                    result.FilesDeleted++;
                }
                catch (IOException)
                {
                    // File is locked by another running process (e.g. Chrome, Word, Service)
                    result.FilesSkipped++;
                }
                catch (UnauthorizedAccessException)
                {
                    // Restricted system permissions
                    result.FilesSkipped++;
                }
                catch (Exception ex)
                {
                    result.ErrorsCount++;
                    result.SkippedReasons.Add($"Failed to delete {file.Name}: {ex.Message}");
                }
            }

            // Hapus subdirektori kosong jika bukan mode simulasi
            if (!isDryRun && !cancellationToken.IsCancellationRequested)
            {
                DeleteEmptySubDirectories(rootDir, category.TargetDirectories);
            }
        }
    }

    private void CollectFiles(string dirPath, List<FileInfo> files, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(dirPath);

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) break;
            string current = stack.Pop();

            try
            {
                var dir = new DirectoryInfo(current);
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                files.AddRange(dir.GetFiles());

                foreach (var sub in dir.GetDirectories())
                {
                    if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        stack.Push(sub.FullName);
                    }
                }
            }
            catch { }
        }
    }

    private void DeleteEmptySubDirectories(string currentDir, List<string> allowedRoots)
    {
        try
        {
            foreach (var subDir in Directory.GetDirectories(currentDir))
            {
                DeleteEmptySubDirectories(subDir, allowedRoots);

                if (SystemSafetyGuard.IsSafeToDelete(subDir, allowedRoots))
                {
                    try
                    {
                        // Hapus jika direktori kosong
                        if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                        {
                            Directory.Delete(subDir, false);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }
}
