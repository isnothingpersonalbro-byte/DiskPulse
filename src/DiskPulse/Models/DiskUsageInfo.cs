namespace DiskPulse.Models;

/// <summary>
/// Menyimpan informasi kapasitas dan utilisasi ruang penyimpanan disk.
/// </summary>
public class DiskUsageInfo
{
    public string DriveName { get; set; } = "C:\\";
    public string VolumeLabel { get; set; } = string.Empty;
    public string DriveFormat { get; set; } = "NTFS";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedPercentage => TotalBytes > 0 ? ((double)UsedBytes / TotalBytes) * 100.0 : 0.0;
    public double FreePercentage => TotalBytes > 0 ? ((double)FreeBytes / TotalBytes) * 100.0 : 0.0;

    public string FormattedTotal => FormatBytes(TotalBytes);
    public string FormattedFree => FormatBytes(FreeBytes);
    public string FormattedUsed => FormatBytes(UsedBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        int counter = 0;
        decimal number = bytes;

        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:n2} {suffixes[counter]}";
    }
}
