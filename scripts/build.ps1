param(
    [switch]$SkipTests,
    [switch]$CaptureTest,
    [switch]$NoZip,
    [string]$PublishDirectory,
    [ValidateSet('Portable','Compact','Standalone','Both','All')][string]$Edition = 'All'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $localDotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
    $dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root '.tools/packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $ffmpegDirectory = Join-Path $root '.tools/ffmpeg'
    $ffmpeg = Join-Path $ffmpegDirectory 'ffmpeg.exe'
    if (-not (Test-Path -LiteralPath $ffmpeg)) {
        & (Join-Path $PSScriptRoot 'get-ffmpeg.ps1')
    }
    # Source and binary validation remains mandatory even when app tests are skipped.
    & (Join-Path $root 'tests/Test-FFmpegSourceBundle.ps1') -Directory $ffmpegDirectory
    if (-not $SkipTests) {
        $testArgs = @('run', '--project', 'tests/ScreenLossless.Tests.csproj', '-c', 'Release', '--', $ffmpeg,
            ('tests/artifacts/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
        if ($CaptureTest) { $testArgs += '--capture' }
        & $dotnet @testArgs
        if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    }
    $project = Join-Path $root 'src/ScreenLossless.csproj'
    $version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
    $releaseVersion = 'v' + $version.TrimStart('v')
    $baseDirectory = if ($PublishDirectory) { [IO.Path]::GetFullPath($PublishDirectory) } else { Join-Path $root ('releases/' + $releaseVersion) }
    $packageDirectory = Join-Path $root 'releases/packages'
    $source = Join-Path $ffmpegDirectory 'ffmpeg-source'
    $record = Get-Content -LiteralPath (Join-Path $source 'BUILD-RECORD.json') -Raw | ConvertFrom-Json
    $sourceName = 'ScreenLossless_FFmpeg_' + $record.sources.ffmpegVersion + '_Source.zip'
    function Copy-BundleDirectory([string]$SourceDirectory, [string]$Target) {
        New-Item -ItemType Directory -Force -Path $Target | Out-Null
        Get-ChildItem -LiteralPath $SourceDirectory -Force | Copy-Item -Destination $Target -Recurse -Force
    }
    function Write-Checksum([string]$Path) {
        $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath ($Path + '.sha256') -Value ($hash + '  ' + (Split-Path -Leaf $Path)) -Encoding ascii
    }
    if (-not $NoZip) {
        New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
        $sourceZip = Join-Path $packageDirectory $sourceName
        Compress-Archive -Path (Join-Path $source '*') -DestinationPath $sourceZip -CompressionLevel Optimal -Force
        Write-Checksum $sourceZip
        Write-Host "Corresponding FFmpeg source ZIP: $sourceZip"
    }
    $editions = if ($Edition -eq 'All') { @('Portable','Compact','Standalone') } elseif ($Edition -eq 'Both') { @('Portable','Compact') } else { @($Edition) }
    foreach ($kind in $editions) {
        if($kind -eq 'Standalone'){
            & (Join-Path $PSScriptRoot 'build-single-exe.ps1') -SkipTests -NoPackage:$NoZip -SourcePrepared -PublishDirectory (Join-Path $baseDirectory $kind)
            continue
        }
        $publish = Join-Path $baseDirectory $kind
        $targetExe = Join-Path $publish 'ScreenLossless.exe'
        if (Get-Process ScreenLossless -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe }) {
            throw "Close the recorder before updating $publish."
        }
        $selfContained = if ($kind -eq 'Portable') { 'true' } else { 'false' }
        & $dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true "-p:EnableCompressionInSingleFile=$selfContained" -p:DebugType=None -p:DebugSymbols=false -o $publish
        if ($LASTEXITCODE -ne 0) { throw "$kind publish failed." }
        Copy-Item -LiteralPath $ffmpeg,(Join-Path $ffmpegDirectory 'ffprobe.exe') -Destination $publish -Force
        Copy-BundleDirectory (Join-Path $ffmpegDirectory 'ffmpeg-licenses') (Join-Path $publish 'ffmpeg-licenses')
        Copy-Item -LiteralPath (Join-Path $source 'BUILD-RECORD.json') -Destination (Join-Path $publish 'FFmpeg-BUILD-RECORD.json') -Force
        Copy-Item -LiteralPath 'LICENSE' -Destination $publish -Force
        $sourceNotice=(Get-Content -LiteralPath 'docs/FFmpeg-SOURCE.md' -Raw -Encoding UTF8).Replace('](Standalone-README.md)','](docs/Standalone-README.md)')
        [IO.File]::WriteAllText((Join-Path $publish 'FFmpeg-SOURCE.md'),$sourceNotice,[Text.UTF8Encoding]::new($false))
        $readme = if ($kind -eq 'Compact') { 'docs/Compact-README.md' } else { 'docs/Portable-README.md' }
        # This document also lives under docs/ in the source and runtime bundles.
        # Rebase its local guide link only for the copy placed at the package root.
        $readmeText = (Get-Content -LiteralPath $readme -Raw -Encoding UTF8).Replace('](UserGuide.md)', '](docs/UserGuide.md)')
        Set-Content -LiteralPath (Join-Path $publish 'README.md') -Value $readmeText -Encoding UTF8
        Copy-BundleDirectory (Join-Path $root 'docs') (Join-Path $publish 'docs')
        Copy-BundleDirectory (Join-Path $root 'licenses') (Join-Path $publish 'licenses')
        if (-not $NoZip) {
            $zip = Join-Path $packageDirectory ('ScreenLossless_' + $releaseVersion + '_Win64_' + $kind + '.zip')
            Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal -Force
            Write-Checksum $zip
            Write-Host "$kind ZIP: $zip"
        }
        Write-Host "$kind EXE: $targetExe"
    }
    if (-not $NoZip) {
        & (Join-Path $root 'tests/Test-ReleasePackages.ps1') -Directory $packageDirectory -Version $version -Edition $Edition
    }
    Write-Host "Distribute application EXE / ZIP assets together with $sourceName from the same download page."
} finally { Pop-Location }
