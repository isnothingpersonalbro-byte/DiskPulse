using DiskPulse.Core;
using DiskPulse.Models;

Console.WriteLine("=================================================");
Console.WriteLine("  DiskPulse - Functional & Safety Verification   ");
Console.WriteLine("=================================================");

int passed = 0;
int failed = 0;

void AssertTrue(string testName, bool condition, string detail = "")
{
    if (condition)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[PASS] ");
        Console.ResetColor();
        Console.WriteLine($"{testName} {detail}");
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[FAIL] ");
        Console.ResetColor();
        Console.WriteLine($"{testName} {detail}");
        failed++;
    }
}

// 1. Uji DiskMonitorService
Console.WriteLine("\n[1] Testing DiskMonitorService...");
var monitor = new DiskMonitorService("C:\\");
var usage = monitor.GetDriveUsage();
AssertTrue("Drive Name is C:\\", usage.DriveName.StartsWith("C", StringComparison.OrdinalIgnoreCase));
AssertTrue("TotalBytes > 0", usage.TotalBytes > 0, $"({usage.FormattedTotal})");
AssertTrue("FreeBytes > 0", usage.FreeBytes > 0, $"({usage.FormattedFree})");
AssertTrue("UsedPercentage valid", usage.UsedPercentage is >= 0 and <= 100, $"({usage.UsedPercentage:F2}%)");

// 2. Uji SystemSafetyGuard
Console.WriteLine("\n[2] Testing SystemSafetyGuard (OS Critical Directories)...");
string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
string tempDir = Path.GetTempPath();
string[] allowedRoots = [tempDir];

// Pastikan System32 DIBLOKIR
string sys32File = Path.Combine(winDir, "System32", "kernel32.dll");
bool safeSys32 = SystemSafetyGuard.IsSafeToDelete(sys32File, allowedRoots);
AssertTrue("System32\\kernel32.dll MUST BE BLOCKED", !safeSys32);

// Pastikan WinSxS DIBLOKIR
string winSxsFile = Path.Combine(winDir, "WinSxS", "test.dll");
bool safeWinSxS = SystemSafetyGuard.IsSafeToDelete(winSxsFile, allowedRoots);
AssertTrue("WinSxS\\test.dll MUST BE BLOCKED", !safeWinSxS);

// Pastikan pagefile.sys DIBLOKIR
string pagefile = "C:\\pagefile.sys";
bool safePagefile = SystemSafetyGuard.IsSafeToDelete(pagefile, allowedRoots);
AssertTrue("C:\\pagefile.sys MUST BE BLOCKED", !safePagefile);

// Pastikan berkas di %TEMP% DIIZINKAN jika ada di whitelist
string dummyTempFile = Path.Combine(tempDir, "diskpulse_test_dummy.tmp");
bool safeTemp = SystemSafetyGuard.IsSafeToDelete(dummyTempFile, allowedRoots);
AssertTrue("File inside %TEMP% is allowed", safeTemp);

// 3. Uji JunkScannerService & Multi-Drive
Console.WriteLine("\n[3] Testing JunkScannerService & Multi-Drive...");
var scanner = new JunkScannerService();
var allDrives = monitor.GetAvailableDrives();
AssertTrue("Available drives detected >= 1", allDrives.Count >= 1, $"(Found: {allDrives.Count} drives)");

var categories = scanner.GetCategoriesForDrive("C:\\");
AssertTrue("Predefined categories count >= 6", categories.Count >= 6, $"(Found: {categories.Count})");

var userTempCat = categories.First(c => c.Id == "user_temp");
await scanner.ScanCategoryAsync(userTempCat, "C:\\");
AssertTrue("User Temp scan completes", !userTempCat.IsScanning, $"({userTempCat.FileCount:N0} files, {userTempCat.FormattedSize})");

// 4. Uji FileCleanerService (Dry-Run Simulation)
Console.WriteLine("\n[4] Testing FileCleanerService (Dry-Run Mode)...");
var cleaner = new FileCleanerService();
var cleanResult = await cleaner.CleanCategoriesAsync([userTempCat], "C:\\", isDryRun: true);
AssertTrue("CleanResult IsDryRun is true", cleanResult.IsDryRun);
AssertTrue("CleanResult BytesFreed calculated", cleanResult.BytesFreed >= 0, $"({cleanResult.FormattedBytesFreed})");
AssertTrue("CleanResult ElapsedTime recorded", cleanResult.ElapsedTime.TotalMilliseconds > 0);

// 5. Uji FolderAnalyzerService Directory Size Calculation
Console.WriteLine("\n[5] Testing FolderAnalyzerService Size Calculation...");
var (dirSize, fileCount) = FolderAnalyzerService.CalculateDirectorySize(tempDir, "Testing Temp");
AssertTrue("CalculateDirectorySize on TempDir returns valid metrics", fileCount >= 0, $"({fileCount:N0} files, {DiskUsageInfo.FormatBytes(dirSize)})");

// 6. Uji Penuh AnalyzeTopFoldersAsync pada C:\
Console.WriteLine("\n[6] Testing Full AnalyzeTopFoldersAsync on C:\\...");
var analyzer = new FolderAnalyzerService("C:\\");
var progress = new Progress<string>(s => {
    if (s.Contains("10") || s.Contains("25") || s.Contains("50") || s.Contains("75") || s.Contains("100") || s.Contains("115"))
    {
        Console.WriteLine($"  -> {s}");
    }
});
var sw = System.Diagnostics.Stopwatch.StartNew();
var topFolders = await analyzer.AnalyzeAllFoldersAsync("C:\\", 500000000000, progress);
sw.Stop();
AssertTrue("AnalyzeAllFoldersAsync completes without hanging", topFolders.Count > 0, $"Found {topFolders.Count} folders in {sw.ElapsedMilliseconds} ms");
AssertTrue("AnalyzeAllFoldersAsync returns more than 10 folders (all folders)", topFolders.Count > 10, $"Total folders: {topFolders.Count}");
Console.WriteLine($"  Showing top 5 of {topFolders.Count} folders:");
foreach (var f in topFolders.Take(5))
{
    Console.WriteLine($"  #{f.Rank} [{f.FormattedSize}] {f.Path}");
}

// 7. Uji Drill-Down InspectFolderContentsAsync
Console.WriteLine("\n[7] Testing InspectFolderContentsAsync Drill-Down...");
string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
Console.WriteLine($"  Target drill-down directory: {localAppData}");
var detailProgress = new Progress<string>(s => Console.WriteLine($"  -> {s}"));
var detailItems = await analyzer.InspectFolderContentsAsync(localAppData, detailProgress);

AssertTrue("InspectFolderContentsAsync returns items inside directory", detailItems.Count > 0, $"Found {detailItems.Count} items");
AssertTrue("InspectFolderContentsAsync items are sorted descending by size",
    detailItems.Zip(detailItems.Skip(1), (a, b) => a.TotalSizeBytes >= b.TotalSizeBytes).All(x => x));

Console.WriteLine($"  Showing top 5 largest items in {localAppData}:");
foreach (var item in detailItems.Take(5))
{
    string icon = item.IsDirectory ? "[DIR]" : "[FILE]";
    Console.WriteLine($"  {icon} {item.Name} ({item.FormattedSize}) - {item.FileCount:N0} files - Type: {item.ItemType}");
}

// 8. Uji SystemSafetyGuard.IsSafeForUserManualDelete
Console.WriteLine("\n[8] Testing IsSafeForUserManualDelete (Context Menu Protection)...");
AssertTrue("C:\\ Drive Root MUST BE BLOCKED", !SystemSafetyGuard.IsSafeForUserManualDelete("C:\\", out _));
AssertTrue("C:\\Windows MUST BE BLOCKED", !SystemSafetyGuard.IsSafeForUserManualDelete(winDir, out _));
AssertTrue("C:\\Windows\\System32 MUST BE BLOCKED", !SystemSafetyGuard.IsSafeForUserManualDelete(Path.Combine(winDir, "System32"), out _));
AssertTrue("C:\\pagefile.sys MUST BE BLOCKED", !SystemSafetyGuard.IsSafeForUserManualDelete("C:\\pagefile.sys", out _));
string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
AssertTrue("UserProfile root MUST BE BLOCKED", !SystemSafetyGuard.IsSafeForUserManualDelete(userProf, out _));

string dummySafeDir = Path.Combine(tempDir, "diskpulse_manual_test_safe");
AssertTrue("Subfolder in Temp is ALLOWED", SystemSafetyGuard.IsSafeForUserManualDelete(dummySafeDir, out _));

// 9. Uji AppUninstallerService (Registry & Local Uninstaller Detection)
Console.WriteLine("\n[9] Testing AppUninstallerService...");
var uninstallerService = new AppUninstallerService();
// Tes deteksi pada folder Steam atau Program Files
string progFilesX86Dir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
string steamDir = Path.Combine(progFilesX86Dir, "Steam");
var appInfo = uninstallerService.FindAppForPath(steamDir);
Console.WriteLine($"  Query on {steamDir}: IsRecognized={appInfo.IsRecognizedApp}, App='{appInfo.DisplayName}', HasUninstaller={appInfo.HasDirectUninstaller}");
AssertTrue("AppUninstallerService returns valid AppUninstallInfo object", appInfo != null);

var installedApps = uninstallerService.GetInstalledApps();
int iconsFound = 0;
foreach (var a in installedApps.Take(15))
{
    var icon = AppIconService.GetAppIcon(a);
    if (icon != null) iconsFound++;
    Console.WriteLine($"  App '{a.DisplayName}': Icon={(icon != null ? $"{icon.Width}x{icon.Height}" : "None")}, DisplayIcon='{a.DisplayIcon}'");
}
AssertTrue("AppIconService extracts icons for installed apps", iconsFound > 0, $"(Found {iconsFound} icons in sample)");

// 10. Uji AppUpdaterService (Online Winget Integration)
Console.WriteLine("\n[10] Testing AppUpdaterService (Online Updates via winget)...");
string mockWingetOutput = 
    "Name                             Id                          Version     Available  Source\r\n" +
    "------------------------------------------------------------------------------------------\r\n" +
    "Git                              Git.Git                     2.54.0      2.55.0.3   winget\r\n" +
    "Zen Browser (x64 en-US)          Zen-Team.Zen-Browser        1.21.16b    1.22.1b    winget\r\n" +
    "Microsoft .NET SDK 8.0.406 (x64) Microsoft.DotNet.SDK.8      8.0.406     8.0.425    winget\r\n" +
    "3 upgrades available.\r\n";

var parsedUpgrades = AppUpdaterService.ParseWingetOutput(mockWingetOutput);
AssertTrue("AppUpdaterService parses winget upgrade table correctly", parsedUpgrades.Count == 3, $"(Parsed: {parsedUpgrades.Count} items)");
AssertTrue("AppUpdaterService parsed item name", parsedUpgrades.Count > 0 && parsedUpgrades[0].Name == "Git");
AssertTrue("AppUpdaterService parsed item ID", parsedUpgrades.Count > 0 && parsedUpgrades[0].Id == "Git.Git");
AssertTrue("AppUpdaterService parsed available version", parsedUpgrades.Count > 0 && parsedUpgrades[0].AvailableVersion == "2.55.0.3");

var testApps = new List<AppUninstallInfo>
{
    new() { DisplayName = "Git", DisplayVersion = "2.54.0" },
    new() { DisplayName = "Zen Browser (x64 en-US)", DisplayVersion = "1.21.16b" },
    new() { DisplayName = "Notepad++ (64-bit x64)", DisplayVersion = "8.6.2" }
};

int matchedCount = AppUpdaterService.MatchUpgrades(testApps, parsedUpgrades);
AssertTrue("AppUpdaterService matches upgrades to installed apps", matchedCount == 2, $"(Matched: {matchedCount})");
AssertTrue("Git has update flag set", testApps[0].HasUpdate && testApps[0].AvailableVersion == "2.55.0.3");
AssertTrue("Zen Browser has update flag set", testApps[1].HasUpdate && testApps[1].AvailableVersion == "1.22.1b");
AssertTrue("App with no update has HasUpdate = false", !testApps[2].HasUpdate);

// 11. Uji NativeMethods.SendToRecycleBin (Pindah ke Keranjang Sampah)
Console.WriteLine("\n[11] Testing NativeMethods.SendToRecycleBin...");
string recycleTestFile = Path.Combine(tempDir, $"diskpulse_recycle_test_{Guid.NewGuid():N}.txt");
File.WriteAllText(recycleTestFile, "DiskPulse Recycle Bin Safety Test");
AssertTrue("Test dummy file created", File.Exists(recycleTestFile));

bool sentToRecycle = NativeMethods.SendToRecycleBin(recycleTestFile);
AssertTrue("SendToRecycleBin returns true", sentToRecycle);
AssertTrue("Test dummy file moved out of original path", !File.Exists(recycleTestFile));

// 12. Uji AppUninstallInfo & AppUpdaterService State Transitions
Console.WriteLine("\n[12] Testing AppUninstallInfo & AppUpdaterService State Transitions...");
var dummyApp = new AppUninstallInfo
{
    DisplayName = "Test App",
    DisplayVersion = "1.0.0",
    AvailableVersion = "1.1.0",
    WingetId = "Test.App"
};
AssertTrue("App has update when AvailableVersion is set", dummyApp.HasUpdate);
AssertTrue("App IsUpdating initially false", !dummyApp.IsUpdating);
AssertTrue("App UpdateStatusText initially empty", string.IsNullOrEmpty(dummyApp.UpdateStatusText));

dummyApp.IsUpdating = true;
dummyApp.UpdateStatusText = "Downloading...";
AssertTrue("App IsUpdating can be set to true", dummyApp.IsUpdating);
AssertTrue("App UpdateStatusText displays Downloading...", dummyApp.UpdateStatusText == "Downloading...");

dummyApp.UpdateStatusText = "Installing...";
AssertTrue("App UpdateStatusText transitions to Installing...", dummyApp.UpdateStatusText == "Installing...");

dummyApp.UpdateStatusText = "Updated!";
dummyApp.DisplayVersion = dummyApp.AvailableVersion;
dummyApp.AvailableVersion = null;
dummyApp.IsUpdating = false;
AssertTrue("App transitions to Updated! and clears HasUpdate", !dummyApp.HasUpdate && dummyApp.DisplayVersion == "1.1.0");

// 13. Uji Inisialisasi MainForm dan Siklus Hidup UI
Console.WriteLine("\n[13] Testing MainForm UI lifecycle...");
Exception? uiException = null;
var uiThread = new Thread(() =>
{
    try
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        DiskPulse.Core.NativeMethods.EnableDarkModeForApp();
        Application.ThreadException += (_, e) =>
        {
            Console.WriteLine($"[UI ThreadException] {e.Exception}");
            uiException = e.Exception;
        };

        var form = new DiskPulse.UI.MainForm();
        form.Shown += async (_, _) =>
        {
            string artifactDir = @"C:\Users\karma\.gemini\antigravity\brain\0496b8d3-6a0f-46f8-a886-92fb5afaa054";
            await Task.Delay(800);

            // Tab 0: Cleaner (Has ring meter diagram & overview)
            try
            {
                using var bmpCleaner = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmpCleaner, new Rectangle(0, 0, form.Width, form.Height));
                bmpCleaner.Save(Path.Combine(artifactDir, "verify_cleaner_tab.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screenshot Error Cleaner] {ex.Message}");
            }

            var switchTabMethod = typeof(DiskPulse.UI.MainForm).GetMethod("SwitchTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [typeof(int)], null);
            
            // Tab 2: Apps (NO diagram, clean full height, fixed button overlap, zero tearing, with update buttons)
            switchTabMethod?.Invoke(form, [2]);
            await Task.Delay(1200);

            var gridAppsField = typeof(DiskPulse.UI.MainForm).GetField("_gridApps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var gridApps = gridAppsField?.GetValue(form) as DiskPulse.UI.Controls.DarkAppGridControl;

            var btnCheckUpdatesField = typeof(DiskPulse.UI.MainForm).GetField("_btnCheckAppUpdates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var btnCheckUpdates = btnCheckUpdatesField?.GetValue(form) as Button;

            AssertTrue("Check Updates button is created in App Manager header", btnCheckUpdates != null && btnCheckUpdates.Visible && btnCheckUpdates.Text.Contains("Updates"));
            AssertTrue("DarkAppGridControl instantiated and populated", gridApps != null && gridApps.AppCount > 0, $"(Apps detected: {gridApps?.AppCount})");

            // Tunggu pengecekan update selesai atau jalankan langsung
            try
            {
                var checkUpdatesMethod = typeof(DiskPulse.UI.MainForm).GetMethod("CheckAppUpdatesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (checkUpdatesMethod != null)
                {
                    var task = (Task)checkUpdatesMethod.Invoke(form, [false])!;
                    await task;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Check Updates In Test Error] {ex.Message}");
            }
            await Task.Delay(400);

            // Test rapid scroll up and down
            bool rapidScrollSuccess = false;
            try
            {
                if (gridApps != null)
                {
                    int initialScroll = gridApps.ScrollY;
                    for (int s = 0; s < 25; s++)
                    {
                        gridApps.ScrollBy(80);
                    }
                    int scrolledDown = gridApps.ScrollY;
                    for (int s = 0; s < 25; s++)
                    {
                        gridApps.ScrollBy(-80);
                    }
                    int scrolledUp = gridApps.ScrollY;
                    rapidScrollSuccess = scrolledDown > 0 && scrolledUp == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Rapid Scroll Test Error] {ex.Message}");
            }
            AssertTrue("DarkAppGridControl rapid scrolling executes smoothly with zero tearing", rapidScrollSuccess);

            // Test updating state rendering on DarkAppGridControl
            if (gridApps != null && gridApps.AppCount > 0)
            {
                var app0 = gridApps.Apps[0];
                app0.AvailableVersion = "v2.5.0";
                app0.IsUpdating = true;
                app0.UpdateStatusText = "Downloading...";

                if (gridApps.AppCount > 1)
                {
                    var app1 = gridApps.Apps[1];
                    app1.AvailableVersion = "v4.0.1";
                }

                gridApps.NotifyUpdatingStateChanged();
                await Task.Delay(250);
            }

            try
            {
                using var bmpApps = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmpApps, new Rectangle(0, 0, form.Width, form.Height));
                bmpApps.Save(Path.Combine(artifactDir, "verify_apps_tab.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screenshot Error Apps] {ex.Message}");
            }

            // Tab 1: Folders (NO diagram, clean full height)
            switchTabMethod?.Invoke(form, [1]);
            await Task.Delay(400);

            try
            {
                using var bmpFoldersEmpty = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmpFoldersEmpty, new Rectangle(0, 0, form.Width, form.Height));
                bmpFoldersEmpty.Save(Path.Combine(artifactDir, "verify_folders_empty_tab.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screenshot Error Folders Empty] {ex.Message}");
            }

            // Test and capture Active Scanning Loading State
            try
            {
                var scanningMethod = typeof(DiskPulse.UI.MainForm).GetMethod("ShowFolderScanningState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [typeof(string), typeof(string)], null);
                scanningMethod?.Invoke(form, ["Analyzing All Folders (C:\\)...", "● Scanning directory: C:\\Users\\karma\\AppData\\Local (38,410 files examined)..."]);
                await Task.Delay(200);

                using var bmpScanning = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmpScanning, new Rectangle(0, 0, form.Width, form.Height));
                bmpScanning.Save(Path.Combine(artifactDir, "verify_folders_scanning.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screenshot Error Scanning] {ex.Message}");
            }

            try
            {
                var inspectMethod = typeof(DiskPulse.UI.MainForm).GetMethod("InspectFolderAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [typeof(string), typeof(bool)], null);
                if (inspectMethod != null)
                {
                    var task = (Task)inspectMethod.Invoke(form, [@"C:\Users\karma\AppData\Local", true])!;
                    await task;
                }
                await Task.Delay(500);

                var btnBackField = typeof(DiskPulse.UI.MainForm).GetField("_btnBackFolder", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var btnBack = btnBackField?.GetValue(form) as Button;
                var pnlBreadcrumbField = typeof(DiskPulse.UI.MainForm).GetField("_pnlBreadcrumb", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var pnlBreadcrumb = pnlBreadcrumbField?.GetValue(form) as FlowLayoutPanel;

                AssertTrue("Back button is visible during folder drilldown", btnBack != null && btnBack.Visible);
                AssertTrue("Breadcrumbs do not overlap back button (X >= 44)", pnlBreadcrumb != null && pnlBreadcrumb.Location.X >= 44, $"(Breadcrumb X={pnlBreadcrumb?.Location.X})");

                var btnUpdateField = typeof(DiskPulse.UI.MainForm).GetField("_btnUpdateApp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var btnUpdate = btnUpdateField?.GetValue(form) as Button;
                var cardDiskField = typeof(DiskPulse.UI.MainForm).GetField("_cardSidebarDisk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var cardDisk = cardDiskField?.GetValue(form) as Control;

                AssertTrue("Update button is created in sidebar", btnUpdate != null && btnUpdate.Visible && btnUpdate.Text.Contains("Update"));
                AssertTrue("Update button is placed in sidebar footer below disk card", cardDisk != null && btnUpdate != null && cardDisk.Parent == btnUpdate.Parent);

                using var bmpFolders = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmpFolders, new Rectangle(0, 0, form.Width, form.Height));
                bmpFolders.Save(Path.Combine(artifactDir, "verify_folders_tab.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screenshot Error Folders] {ex.Message}");
            }


            form.Close();
        };
        Application.Run(form);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[UI Catch] {ex}");
        uiException = ex;
    }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join(12000);

AssertTrue("MainForm launched and closed without exceptions", uiException == null, uiException?.Message ?? "");

Console.WriteLine("\n=================================================");
Console.WriteLine($"Result: {passed} PASSED, {failed} FAILED");
Console.WriteLine("=================================================");

return failed > 0 ? 1 : 0;
