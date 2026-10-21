param([string]$Ffmpeg = (Join-Path (Split-Path -Parent $PSScriptRoot) '.tools/ffmpeg/ffmpeg.exe'))
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Ffmpeg)) {throw "FFmpeg missing: $Ffmpeg"}
Write-Host 'Encoder check:'
& $Ffmpeg -hide_banner -encoders | Select-String 'h264_nvenc|libx264rgb|ffv1'
Write-Host 'DXGI capture filter:'
& $Ffmpeg -hide_banner -filters | Select-String 'ddagrab'
Write-Host 'CPU RGB lossless encode smoke test:'
& $Ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=128x72:rate=30:duration=1' -c:v libx264rgb -preset ultrafast -crf 0 -pix_fmt bgr0 -f matroska "$env:TEMP\ScreenLossless_x264rgb_test.mkv"
if ($LASTEXITCODE -ne 0) {throw 'CPU RGB smoke test failed'}
Write-Host 'CPU RGB smoke test: PASS'
Write-Host 'To test NVIDIA on your own PC, run:'
Write-Host 'ffmpeg -f lavfi -i testsrc2=s=128x72:r=1 -frames:v 1 -pix_fmt yuv444p -c:v h264_nvenc -tune lossless -profile:v high444p -rc constqp -qp 0 -f null NUL'
