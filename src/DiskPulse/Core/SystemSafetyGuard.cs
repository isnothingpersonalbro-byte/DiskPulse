namespace DiskPulse.Core;

/// <summary>
/// Pelindung keamanan sistem tingkat tinggi yang mencegah akses dan penghapusan
/// terhadap berkas dan direktori kritis sistem operasi Windows.
/// </summary>
public static class SystemSafetyGuard
{
    private static readonly HashSet<string> ProtectedCriticalPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ProtectedSystemFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys",
        "swapfile.sys",
        "hiberfil.sys",
        "dumpstack.log",
        "bootmgr",
        "BOOTNXT",
        "NTLDR",
        "autoexec.bat",
        "config.sys"
    };

    static SystemSafetyGuard()
    {
        // Daftarkan jalur-jalur kritis sistem operasi
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string sysX86 = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86);
        string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AddProtectedPath(winDir);
        AddProtectedPath(sys32);
        if (!string.IsNullOrEmpty(sysX86)) AddProtectedPath(sysX86);
        AddProtectedPath(Path.Combine(winDir, "WinSxS"));
        AddProtectedPath(Path.Combine(winDir, "Boot"));
        AddProtectedPath(Path.Combine(winDir, "assembly"));
        AddProtectedPath(Path.Combine(winDir, "Microsoft.NET"));
        AddProtectedPath(Path.Combine(winDir, "inf"));
        AddProtectedPath(Path.Combine(winDir, "Fonts"));

        AddProtectedPath(progFiles);
        if (!string.IsNullOrEmpty(progFilesX86)) AddProtectedPath(progFilesX86);
        AddProtectedPath(userProfile);

        // Drive Root
        string driveRoot = Path.GetPathRoot(winDir) ?? "C:\\";
        AddProtectedPath(driveRoot);
    }

    private static void AddProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            ProtectedCriticalPaths.Add(normalized);
        }
        catch
        {
            // Abaikan error path invalid saat inisialisasi
        }
    }

    /// <summary>
    /// Memeriksa apakah suatu file atau direktori aman untuk dihapus.
    /// Mengembalikan false jika terdeteksi merupakan direktori kritis atau berkas boot/sistem operasi.
    /// </summary>
    public static bool IsSafeToDelete(string targetPath, IEnumerable<string> allowedRootDirectories)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return false;

        try
        {
            string fullPath = Path.GetFullPath(targetPath);
            string fileName = Path.GetFileName(fullPath);

            // 1. Cek berkas sistem boot khusus
            if (ProtectedSystemFiles.Contains(fileName))
            {
                return false;
            }

            string normalizedTarget = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 2. Cek apakah target merupakan root atau jalur kritis yang diblacklist secara tepat
            if (ProtectedCriticalPaths.Contains(normalizedTarget))
            {
                return false;
            }

            // 3. Pastikan tidak berada di dalam System32, SysWOW64, atau WinSxS
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys32 = Path.Combine(winDir, "System32");
            string sysWow64 = Path.Combine(winDir, "SysWOW64");
            string winSxS = Path.Combine(winDir, "WinSxS");

            if (IsSubdirectoryOf(normalizedTarget, sys32) ||
                IsSubdirectoryOf(normalizedTarget, sysWow64) ||
                IsSubdirectoryOf(normalizedTarget, winSxS))
            {
                return false;
            }

            // 4. Whitelist check: target HARUS berada di dalam salah satu direktori target yang diizinkan
            bool insideAllowedRoot = false;
            foreach (var allowed in allowedRootDirectories)
            {
                if (string.IsNullOrWhiteSpace(allowed)) continue;
                string normalizedAllowed = Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (IsSubdirectoryOf(normalizedTarget, normalizedAllowed))
                {
                    insideAllowedRoot = true;
                    break;
                }
            }

            return insideAllowedRoot;
        }
        catch
        {
            // Jika terjadi error saat memvalidasi path, tolak demi keamanan
            return false;
        }
    }

    /// <summary>
    /// Mengecek apakah childPath berada di dalam parentPath.
    /// </summary>
    public static bool IsSubdirectoryOf(string childPath, string parentPath)
    {
        string normChild = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normChild.StartsWith(normParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Memeriksa apakah suatu target file atau direktori aman untuk dihapus manual oleh pengguna.
    /// Memastikan folder sistem Windows (Windows, System32, WinSxS), drive root (C:\),
    /// serta profil pengguna induk tidak dapat dihapus secara tidak sengaja.
    /// </summary>
    public static bool IsSafeForUserManualDelete(string targetPath, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            reason = "Invalid target path.";
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(targetPath);
            string normalizedTarget = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(fullPath) ?? "";

            // 1. Check drive root (C:\, D:\, etc.)
            if (normalizedTarget.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                reason = "Drive root (e.g. C:\\) cannot be deleted.";
                return false;
            }

            // 2. Check special boot system files
            string fileName = Path.GetFileName(fullPath);
            if (ProtectedSystemFiles.Contains(fileName))
            {
                reason = $"Critical system file ({fileName}) is protected by Windows.";
                return false;
            }

            // 3. Check critical root directories (Windows, Program Files, Users, etc.)
            if (ProtectedCriticalPaths.Contains(normalizedTarget))
            {
                reason = $"System directory ({Path.GetFileName(normalizedTarget)}) is protected by Windows.";
                return false;
            }

            // 4. Ensure not Windows folder or subfolder inside Windows
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalizedTarget.Equals(winDir, StringComparison.OrdinalIgnoreCase) || IsSubdirectoryOf(normalizedTarget, winDir))
            {
                reason = "Windows system folder cannot be deleted to protect OS integrity.";
                return false;
            }

            // 5. Ensure not primary user profile folder
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalizedTarget.Equals(userProfile, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Primary user profile folder cannot be deleted.";
                return false;
            }

            string usersRoot = Path.GetDirectoryName(userProfile) ?? "";
            if (!string.IsNullOrEmpty(usersRoot) && normalizedTarget.Equals(usersRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                reason = "Users directory cannot be deleted.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            reason = $"Security validation error: {ex.Message}";
            return false;
        }
    }
}
