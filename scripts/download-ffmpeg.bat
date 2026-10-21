@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0.."
echo Installing ScreenLossless FFmpeg with matching source and license files.
echo If no local bundle exists, the source build requires Linux Docker and Python.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0get-ffmpeg.ps1"
if errorlevel 1 (
    echo [ERROR] FFmpeg installation failed. See the error above.
    pause
    exit /b 1
)
echo [OK] FFmpeg, sources and licenses are ready.
pause
