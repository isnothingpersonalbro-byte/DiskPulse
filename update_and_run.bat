@echo off
echo ===================================================
echo   DiskPulse - Update and Launch
echo ===================================================
echo Menutup proses lama jika masih berjalan...
taskkill /F /IM DiskPulse.exe >nul 2>&1
timeout /t 1 >nul

echo Menyalin binary terbaru...
copy /Y publish\DiskPulse.exe DiskPulse.exe >nul 2>&1
if exist DiskPulse.exe (
    echo [OK] DiskPulse.exe berhasil diperbarui!
    echo Memulai DiskPulse...
    start DiskPulse.exe
) else (
    echo Menjalankan DiskPulse_Latest.exe...
    start DiskPulse_Latest.exe
)
