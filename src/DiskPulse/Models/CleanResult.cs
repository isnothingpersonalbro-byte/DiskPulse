namespace DiskPulse.Models;

/// <summary>
/// Hasil ringkasan dari operasi pembersihan berkas (nyata maupun simulasi).
/// </summary>
public class CleanResult
{
    public bool IsDryRun { get; set; }
    public long BytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesSkipped { get; set; }
    public int ErrorsCount { get; set; }
    public List<string> SkippedReasons { get; set; } = [];
    public TimeSpan ElapsedTime { get; set; }

    public string FormattedBytesFreed => DiskUsageInfo.FormatBytes(BytesFreed);
}
