using DiskPulse.Models;

namespace DiskPulse.Core;

/// <summary>
/// Layanan pemantauan kapasitas ruang disk sistem dengan dukungan multi-drive (Drive C, D, E, dll.).
/// </summary>
public class DiskMonitorService
{
    private string _currentDriveLetter;

    public string CurrentDriveLetter
    {
        get => _currentDriveLetter;
        set => _currentDriveLetter = value.EndsWith('\\') ? value : value + "\\";
    }

    public DiskMonitorService(string defaultDrive = "C:\\")
    {
        _currentDriveLetter = defaultDrive.EndsWith('\\') ? defaultDrive : defaultDrive + "\\";
    }

    /// <summary>
    /// Mengambil seluruh drive penyimpanan yang aktif dan siap digunakan pada sistem komputer.
    /// </summary>
    public List<DiskUsageInfo> GetAvailableDrives()
    {
        var list = new List<DiskUsageInfo>();

        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                try
                {
                    if (drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable))
                    {
                        var info = new DiskUsageInfo
                        {
                            DriveName = drive.Name,
                            VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                            DriveFormat = drive.DriveFormat,
                            TotalBytes = drive.TotalSize,
                            FreeBytes = drive.AvailableFreeSpace
                        };
                        list.Add(info);
                    }
                }
                catch { }
            }
        }
        catch { }

        if (list.Count == 0)
        {
            list.Add(GetDriveUsage("C:\\"));
        }

        return list;
    }

    /// <summary>
    /// Mengambil informasi kapasitas untuk drive tertentu (misal "C:\", "D:\", "E:\").
    /// </summary>
    public DiskUsageInfo GetDriveUsage(string? driveLetter = null)
    {
        string target = string.IsNullOrWhiteSpace(driveLetter) ? _currentDriveLetter : driveLetter;
        target = target.EndsWith('\\') ? target : target + "\\";

        var info = new DiskUsageInfo
        {
            DriveName = target
        };

        try
        {
            var drive = new DriveInfo(target);
            if (drive.IsReady)
            {
                info.VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                info.DriveFormat = drive.DriveFormat;
                info.TotalBytes = drive.TotalSize;
                info.FreeBytes = drive.AvailableFreeSpace;
                return info;
            }
        }
        catch { }

        if (NativeMethods.GetDiskFreeSpaceEx(target, out ulong freeBytes, out ulong totalBytes, out _))
        {
            info.TotalBytes = (long)totalBytes;
            info.FreeBytes = (long)freeBytes;
            info.VolumeLabel = "Local Disk";
        }

        return info;
    }
}
