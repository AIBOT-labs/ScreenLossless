param([switch]$SkipImageBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'third_party/ffmpeg/screenlossless-9.0.2'
$output = Join-Path $root '.tools/ffmpeg-screenlossless'
New-Item -ItemType Directory -Force -Path $output | Out-Null
python (Join-Path $PSScriptRoot 'prepare-ffmpeg-sources.py')
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg source verification failed.' }
if (-not $SkipImageBuild) {
    docker build --progress plain -t screenlossless-ffmpeg-toolchain:20261012 $source
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg toolchain image build failed.' }
}
docker run --rm --name screenlossless-ffmpeg-build --cpus=6 --mount "type=bind,source=$source,target=/sources,readonly" --mount "type=bind,source=$output,target=/output" screenlossless-ffmpeg-toolchain:20261012 bash /sources/build.sh
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg compilation failed; original binaries were not replaced.' }
Write-Host "Compiled FFmpeg and build records: $output"
