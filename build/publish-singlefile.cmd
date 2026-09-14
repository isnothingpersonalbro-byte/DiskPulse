@echo off
echo =====================================================================
echo  DiskPulse - Kompilasi Executable Tunggal Ringkas (Framework-Dependent)
echo  Ukuran file: ~1.5 MB | RAM: ~18-25 MB | CPU Idle: 0%%
echo =====================================================================

cd /d "%~dp0\.."

echo Membersihkan direktori build lama...
if exist "bin\SingleFile" rmdir /s /q "bin\SingleFile"

echo Mengompilasi DiskPulse.exe...
dotnet publish src\DiskPulse\DiskPulse.csproj ^
    -c Release ^
    -r win-x64 ^
    -p:PublishSingleFile=true ^
    --self-contained false ^
    -o bin\SingleFile

if %ERRORLEVEL% NEQ 0 (
    echo [GAGAL] Kompilasi mengalami kegagalan.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo =====================================================================
echo  [SUKSES] Binary berhasil dibuat!
echo  Lokasi File: %~dp0..\bin\SingleFile\DiskPulse.exe
echo =====================================================================
dir "%~dp0..\bin\SingleFile\DiskPulse.exe"
echo.
pause
