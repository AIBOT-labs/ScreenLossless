param([string]$Ffmpeg, [switch]$Capture, [switch]$Themes, [switch]$Bundle, [switch]$Resources, [switch]$ResourceProfile, [switch]$Scrollbars, [switch]$VisualPolish, [switch]$Localization, [switch]$LanguageContext, [switch]$Audit, [switch]$HevcLossless, [switch]$SecondAudit, [switch]$MediaOnly, [switch]$MainContext, [switch]$Features, [switch]$RegionMemory, [switch]$Completion, [switch]$Profiles, [switch]$EncoderChoices, [switch]$Output, [switch]$LongSplit, [switch]$Stability, [switch]$AudioTimeline, [switch]$Platform, [ValidateRange(5,21600)][int]$Seconds = 60)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $localDotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
    $dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
    if (-not $Ffmpeg) {
        $Ffmpeg = Join-Path $root 'ffmpeg.exe'
        if (-not (Test-Path -LiteralPath $Ffmpeg)) { $Ffmpeg = Join-Path $root '.tools/ffmpeg/ffmpeg.exe' }
    }
    $env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root '.tools/packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $arguments = @('run','--project','tests/ScreenLossless.Tests.csproj','-c','Release','--', $Ffmpeg,
        ('tests/artifacts/run-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
    if ($Resources) { $arguments += '--resources' }
    if ($ResourceProfile) { $arguments += '--resource-profile' }
    if ($Bundle) { $arguments += '--bundle' }
    if ($Themes) { $arguments += '--themes' }
    if ($VisualPolish) { $arguments += '--visual-polish' }
    if ($Scrollbars) { $arguments += '--scrollbars' }
    if ($Localization) { $arguments += '--localization' }
    if ($LanguageContext) { $arguments += '--language-context' }
    if ($Platform) { $arguments += '--platform'; $env:SCREENLOSSLESS_TEST_SECONDS = $Seconds.ToString() }
    if ($Capture) { $arguments += '--capture' }
    if ($Audit) { $arguments += '--audit' }
    if ($HevcLossless) { $arguments += '--hevc-lossless' }
    if ($SecondAudit) { $arguments += '--second-audit' }
    if ($MediaOnly) { $arguments += '--media-only' }
    if ($AudioTimeline) { $arguments += "--audio-timeline" }
    if ($Stability) { $arguments += "--stability" }
    if ($LongSplit) { $arguments += "--long-split" }
    if ($Output) { $arguments += "--output" }
    if ($Profiles) { $arguments += "--profiles" }
    if ($EncoderChoices) { $arguments += "--encoder-choices" }
    if ($Completion) { $arguments += "--completion" }
    if ($RegionMemory) { $arguments += "--region-memory" }
    if ($Features) { $arguments += "--features" }
    if ($MainContext) { $arguments += '--main-context' }
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
} finally { if ($Platform) { Remove-Item Env:SCREENLOSSLESS_TEST_SECONDS -ErrorAction SilentlyContinue }; Pop-Location }
