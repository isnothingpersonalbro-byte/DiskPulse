namespace DiskPulse.Models;

/// <summary>
/// Model untuk menampilkan analisis folder berukuran besar di disk.
/// </summary>
public class FolderSizeItem
{
    public int Rank { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public int FileCount { get; set; }
    public double PercentageOfDrive { get; set; }
    public double RelativePercentage { get; set; }

    public string FormattedSize => DiskUsageInfo.FormatBytes(TotalSizeBytes);
    public string FormattedPercentage => $"{PercentageOfDrive:F1}%";
}
