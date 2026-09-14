Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$proc = Start-Process -FilePath "publish\DiskPulse.exe" -PassThru
Start-Sleep -Seconds 3

# Ambil screenshot tampilan awal
$screen = [System.Windows.Forms.Screen]::PrimaryScreen
$bitmap = New-Object System.Drawing.Bitmap($screen.Bounds.Width, $screen.Bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($screen.Bounds.Location, [System.Drawing.Point]::Empty, $screen.Bounds.Size)

$artifactPath = "C:\Users\karma\.gemini\antigravity\brain\0496b8d3-6a0f-46f8-a886-92fb5afaa054\verify_actual_screen.png"
$bitmap.Save($artifactPath, [System.Drawing.Imaging.ImageFormat]::Png)

$graphics.Dispose()
$bitmap.Dispose()

Stop-Process -Id $proc.Id -Force
Write-Output "Screenshot saved to $artifactPath"
