param([string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) '.tools/ffmpeg'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiled = Join-Path $root '.tools/ffmpeg-screenlossless'
# Reuse the installed matching bundle after build caches have been cleaned.
if (-not (Test-Path -LiteralPath (Join-Path $compiled 'ffmpeg-source/BUILD-RECORD.json'))) {
    $installed = Join-Path $root '.tools/ffmpeg'
    if (Test-Path -LiteralPath (Join-Path $installed 'ffmpeg-source/BUILD-RECORD.json')) { $compiled = $installed }
}
if (-not (Test-Path -LiteralPath (Join-Path $compiled 'ffmpeg-source/BUILD-RECORD.json'))) {
    Write-Host 'Building the pinned ScreenLossless FFmpeg with corresponding sources (Docker Linux is required for this first build).'
    & (Join-Path $PSScriptRoot 'build-ffmpeg.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg source build failed.' }
}
$record = Get-Content -LiteralPath (Join-Path $compiled 'ffmpeg-source/BUILD-RECORD.json') -Raw | ConvertFrom-Json
foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
    $expected = $record.binaries.$name.sha256
    if ((Get-FileHash -LiteralPath (Join-Path $compiled $name) -Algorithm SHA256).Hash -ne $expected) { throw "FFmpeg binary/source record mismatch: $name" }
}
if ([IO.Path]::GetFullPath($Destination).TrimEnd('\') -eq [IO.Path]::GetFullPath($compiled).TrimEnd('\')) {
    Write-Host "FFmpeg and its matching source bundle are already installed: $Destination"
    return
}
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Copy-Item -LiteralPath (Join-Path $compiled 'ffmpeg.exe'),(Join-Path $compiled 'ffprobe.exe') -Destination $Destination -Force
$licenseDirectory = if (Test-Path -LiteralPath (Join-Path $compiled 'ffmpeg-licenses')) { 'ffmpeg-licenses' } else { 'licenses' }
foreach ($pair in @(@($licenseDirectory,'ffmpeg-licenses'),@('ffmpeg-source','ffmpeg-source'))) {
    $target = Join-Path $Destination $pair[1]
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $compiled $pair[0]) -Force | Copy-Item -Destination $target -Recurse -Force
}
Write-Host "Installed ScreenLossless FFmpeg with matching sources and licenses: $Destination"
