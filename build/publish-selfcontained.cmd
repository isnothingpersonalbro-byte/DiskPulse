@echo off
echo =====================================================================
echo  DiskPulse - Kompilasi Executable Mandiri Penuh (Self-Contained Trimmed)
echo  Tidak memerlukan .NET terinstal di komputer target
echo =====================================================================

cd /d "%~dp0\.."

echo Membersihkan direktori build lama...
if exist "bin\SelfContained" rmdir /s /q "bin\SelfContained"

echo Mengompilasi DiskPulse.exe mandiri...
dotnet publish src\DiskPulse\DiskPulse.csproj ^
    -c Release ^
    -r win-x64 ^
    -p:PublishSingleFile=true ^
    --self-contained true ^
    -p:PublishTrimmed=true ^
    -p:_SuppressWinFormsTrimError=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:TrimMode=partial ^
    -o bin\SelfContained

if %ERRORLEVEL% NEQ 0 (
    echo [GAGAL] Kompilasi mengalami kegagalan.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo =====================================================================
echo  [SUKSES] Binary mandiri berhasil dibuat!
echo  Lokasi File: %~dp0..\bin\SelfContained\DiskPulse.exe
echo =====================================================================
dir "%~dp0..\bin\SelfContained\DiskPulse.exe"
echo.
pause
