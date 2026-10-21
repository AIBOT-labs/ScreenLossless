@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0.."
title ScreenLossless Windows x64 Build
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
    echo [ERROR] Build failed. Install .NET 8 SDK or check the error above.
    pause
    exit /b 1
)
echo Build completed. Use Start-ScreenLossless.bat or releases\v1.0.0\Portable\ScreenLossless.exe to start.
pause
