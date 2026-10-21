@echo off
if not exist "%~dp0releases\v1.0.0\Portable\ScreenLossless.exe" (
    echo Portable application not found. Download and extract the latest GitHub Release,
    echo or run scripts\build-windows-x64.bat to build it from source.
    pause
    exit /b 1
)
start "" "%~dp0releases\v1.0.0\Portable\ScreenLossless.exe"
