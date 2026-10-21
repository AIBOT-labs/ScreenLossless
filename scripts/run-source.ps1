$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $localDotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
    $dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_HOME = Join-Path $root '.tools/dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $root '.tools/packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $ffmpegDirectory = Join-Path $root '.tools/ffmpeg'
    if (Test-Path -LiteralPath (Join-Path $ffmpegDirectory 'ffmpeg.exe')) {
        $env:PATH = $ffmpegDirectory + [IO.Path]::PathSeparator + $env:PATH
    }
    & $dotnet run --project 'src/ScreenLossless.csproj'
    if ($LASTEXITCODE -ne 0) { throw 'Source launch failed.' }
} finally { Pop-Location }
