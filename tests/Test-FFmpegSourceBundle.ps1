param([string]$Directory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.tools/ffmpeg'))
$ErrorActionPreference = 'Stop'
$source = Join-Path $Directory 'ffmpeg-source'
$record = Get-Content -LiteralPath (Join-Path $source 'BUILD-RECORD.json') -Raw | ConvertFrom-Json
$count = 0
foreach ($dependency in $record.sources.dependencies) {
    if ([IO.Path]::GetFileName($dependency.file) -ne $dependency.file) { throw 'Invalid source archive filename.' }
    $archive = Join-Path (Join-Path $source 'sources') $dependency.file
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dependency.sha256) { throw "Source mismatch: $($dependency.name)" }
    Write-Host "PASS source checksum: $($dependency.name)"
    $count++
}
foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
    if ((Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash -ne $record.binaries.$name.sha256) { throw "Binary/source mismatch: $name" }
    Write-Host "PASS matching binary: $name"
    $count++
}
foreach ($name in @('Dockerfile','build.sh','mingw-toolchain.cmake','record-build.py','README.md','toolchain-packages.txt','build-config.mak','licenses/ffmpeg-COPYING.GPLv3.txt','licenses/x264-COPYING.txt','licenses/nv-codec-headers-NOTICES.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name))) { throw "Missing source material: $name" }
    $count++
}
$ffmpeg = Join-Path $Directory 'ffmpeg.exe'
$configuration = (& $ffmpeg -version 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0 -or $configuration -notmatch '9\.0\.2-ScreenLossless' -or $configuration -match '--enable-nonfree') { throw 'Unexpected bundled FFmpeg configuration.' }
$count++
$encoders = (& $ffmpeg -hide_banner -encoders 2>&1) -join "`n"
foreach ($name in @('libx264','libx264rgb','h264_nvenc','hevc_nvenc','h264_qsv','h264_amf','ffv1','mpeg4','mjpeg','flac','aac','png')) {
    if ($encoders -notmatch "\b$name\b") { throw "Required encoder missing: $name" }
    $count++
}
$filters = (& $ffmpeg -hide_banner -filters 2>&1) -join "`n"
foreach ($name in @('ddagrab','gfxcapture')) {
    if ($filters -notmatch "\b$name\b") { throw "Required capture filter missing: $name" }
    $count++
}
# Gate the complete recording/muxing pipeline when FFmpeg is built with an allowlist.
$required = @{
    decoders = @('h264','hevc','ffv1','mpeg4','mjpeg','png','bmp','flac','aac','pcm_s16le','pcm_s24le','pcm_s32le','pcm_f32le')
    muxers = @('matroska','mp4','segment','wav','image2pipe','null','framemd5')
    demuxers = @('matroska','mov','wav','concat')
    filters = @('scale','pad','format','fps','hwdownload','aresample','aformat','volume','apad','amix','anullsrc','color')
    devices = @('gdigrab','lavfi')
}
foreach ($category in $required.Keys) {
    $listing = (& $ffmpeg -hide_banner ("-" + $category) 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect FFmpeg $category" }
    foreach ($name in $required[$category]) {
        if ($listing -notmatch "\b$name\b") { throw "Required $category component missing: $name" }
        $count++
    }
}
Write-Host "ALL $count SOURCE / BUILD CHECKS PASSED"
