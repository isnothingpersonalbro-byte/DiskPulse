namespace DiskPulse.Models;

/// <summary>
/// Kategori item berkas sampah yang dapat dipindai dan dibersihkan.
/// </summary>
public class JunkItemCategory
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> TargetDirectories { get; set; } = [];
    public bool IsRecycleBin { get; set; }
    public bool IsSelected { get; set; } = true;
    public long TotalSizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsScanning { get; set; }
    public bool IsCleaned { get; set; }

    public string FormattedSize => DiskUsageInfo.FormatBytes(TotalSizeBytes);
}
