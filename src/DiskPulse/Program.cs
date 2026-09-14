using DiskPulse.UI;

namespace DiskPulse;

internal static class Program
{
    /// <summary>
    /// Titik masuk utama (Main Entry Point) untuk aplikasi DiskPulse.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        // 1. Inisialisasi konfigurasi visual Windows Forms & High-DPI
        ApplicationConfiguration.Initialize();
        Core.NativeMethods.EnableDarkModeForApp();

        // 2. Global Exception Handling untuk mencegah crash yang tidak diinginkan
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Terjadi kesalahan kritis tak terduga:\n{ex.Message}\n\nDetail:\n{ex.StackTrace}",
                    "DiskPulse - Fatal Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        Application.ThreadException += (sender, args) =>
        {
            MessageBox.Show(
                $"Kesalahan proses:\n{args.Exception.Message}\n\nDetail:\n{args.Exception}",
                "DiskPulse - System Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        };

        // 3. Jalankan antarmuka utama
        Application.Run(new MainForm());
    }
}
