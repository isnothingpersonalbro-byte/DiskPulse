namespace DiskPulse.Models;

/// <summary>
/// Model data untuk informasi aplikasi terpasang di Windows beserta perintah uninstaller-nya.
/// </summary>
public class AppUninstallInfo
{
    public bool IsRecognizedApp { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayVersion { get; set; }
    public string? Publisher { get; set; }
    public string? InstallLocation { get; set; }
    public string? UninstallString { get; set; }
    public string? LocalUninstallerPath { get; set; }
    public string? DisplayIcon { get; set; }
    public long EstimatedSizeBytes { get; set; }

    public string FormattedSize => EstimatedSizeBytes > 0 ? DiskUsageInfo.FormatBytes(EstimatedSizeBytes) : "Size Varies";

    public bool HasDirectUninstaller =>
        !string.IsNullOrEmpty(UninstallString) || !string.IsNullOrEmpty(LocalUninstallerPath);

    public string? AvailableVersion { get; set; }
    public string? WingetId { get; set; }
    public bool HasUpdate => !string.IsNullOrEmpty(AvailableVersion);

    public bool IsUpdating { get; set; }
    public string UpdateStatusText { get; set; } = string.Empty;
}
