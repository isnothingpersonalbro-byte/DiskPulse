namespace DiskPulse.Models;

/// <summary>
/// Model untuk merepresentasikan item rincian (subfolder atau berkas besar)
/// saat pengguna menelusuri (drill-down) isi suatu folder.
/// </summary>
public class FolderDetailItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long TotalSizeBytes { get; set; }
    public int FileCount { get; set; }
    public string ItemType { get; set; } = "Direktori";
    public double RelativePercentage { get; set; }

    public string FormattedSize => DiskUsageInfo.FormatBytes(TotalSizeBytes);
}
