param([switch]$SkipTests,[switch]$NoPackage,[switch]$SourcePrepared,[string]$PublishDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
    if(-not(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
    $env:DOTNET_CLI_HOME=Join-Path $root '.tools/dotnet-home'
    $env:NUGET_PACKAGES=Join-Path $root '.tools/packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
    $ffmpegDirectory=Join-Path $root '.tools/ffmpeg'
    if(-not(Test-Path -LiteralPath (Join-Path $ffmpegDirectory 'ffmpeg.exe'))){& (Join-Path $PSScriptRoot 'get-ffmpeg.ps1')}
    & (Join-Path $root 'tests/Test-FFmpegSourceBundle.ps1') -Directory $ffmpegDirectory
    $project=Join-Path $root 'src/ScreenLossless.csproj'
    $version=([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version.TrimStart('v')
    $publish=if($PublishDirectory){[IO.Path]::GetFullPath($PublishDirectory)}else{Join-Path $root ('releases/v'+$version+'/Standalone')}
    $target=Join-Path $publish 'ScreenLossless.exe'
    if(Get-Process ScreenLossless -ErrorAction SilentlyContinue | Where-Object Path -eq $target){throw "Close the recorder before updating $publish."}
    $payloadRoot=Join-Path $root '.tools/standalone-payload'
    New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null
    $stage=Join-Path $payloadRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        Copy-Item -LiteralPath (Join-Path $ffmpegDirectory 'ffmpeg.exe'),(Join-Path $ffmpegDirectory 'ffprobe.exe') -Destination $stage
        foreach($name in @('ffmpeg-licenses','licenses','docs')){
            $from=if($name -eq 'ffmpeg-licenses'){Join-Path $ffmpegDirectory $name}else{Join-Path $root $name}
            Copy-Item -LiteralPath $from -Destination $stage -Recurse
        }
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $stage
        $sourceNotice=(Get-Content -LiteralPath (Join-Path $root 'docs/FFmpeg-SOURCE.md') -Raw -Encoding UTF8).Replace('](Standalone-README.md)','](docs/Standalone-README.md)')
        [IO.File]::WriteAllText((Join-Path $stage 'FFmpeg-SOURCE.md'),$sourceNotice,[Text.UTF8Encoding]::new($false))
        $source=Join-Path $ffmpegDirectory 'ffmpeg-source'
        Copy-Item -LiteralPath (Join-Path $source 'BUILD-RECORD.json') -Destination (Join-Path $stage 'FFmpeg-BUILD-RECORD.json')
        $files=@(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
            [ordered]@{Name=$_.FullName.Substring($stage.Length+1).Replace('\','/');Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
        })
        $json=[ordered]@{FormatVersion=1;Files=$files}|ConvertTo-Json -Depth 5
        [IO.File]::WriteAllText((Join-Path $stage 'bundle-manifest.json'),$json,[Text.UTF8Encoding]::new($false))
        $archive=Join-Path $payloadRoot 'tools.zip'
        $pending=$archive+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
        try{
            # Windows PowerShell / .NET Framework may otherwise write backslashes
            # in ZIP entry names. The embedded index and .NET 8 reader use '/'.
            $zip=[IO.Compression.ZipFile]::Open($pending,[IO.Compression.ZipArchiveMode]::Create)
            try{
                foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $stage $file.Name),$file.Name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $stage 'bundle-manifest.json'),'bundle-manifest.json',[IO.Compression.CompressionLevel]::Optimal)|Out-Null
            }finally{$zip.Dispose()}
            Move-Item -LiteralPath $pending -Destination $archive -Force
        }
        finally{if(Test-Path -LiteralPath $pending){Remove-Item -LiteralPath $pending -Force}}
    } finally {
        $resolved=[IO.Path]::GetFullPath($stage)
        if(-not $resolved.StartsWith($payloadRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Payload cleanup escaped its staging directory.'}
        $links=@(Get-Item -LiteralPath $resolved -Force;Get-ChildItem -LiteralPath $resolved -Recurse -Force)|Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}
        if($links){throw 'Payload staging contains a reparse point.'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    if(-not $SkipTests){& (Join-Path $root 'tests/Run-Regression.ps1') -Bundle; if($LASTEXITCODE -ne 0){throw 'Bundle regression checks failed.'}}
    & $dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false "-p:StandaloneToolsArchive=$archive" -o $publish
    if($LASTEXITCODE -ne 0){throw 'Standalone publish failed.'}
    $outputs=@(Get-ChildItem -LiteralPath $publish -Force)
    if($outputs.Count -ne 1 -or $outputs[0].Name -ne 'ScreenLossless.exe'){throw 'Standalone output must contain exactly one executable and no sidecars.'}
    Write-Host "Standalone EXE: $target ($([math]::Round((Get-Item -LiteralPath $target).Length/1MB,2)) MiB)"
    if(-not $NoPackage){
        $packages=Join-Path $root 'releases/packages';New-Item -ItemType Directory -Force -Path $packages|Out-Null
        $asset=Join-Path $packages ('ScreenLossless_v'+$version+'_Win64_Standalone.exe')
        Copy-Item -LiteralPath $target -Destination $asset -Force
        Set-Content -LiteralPath ($asset+'.sha256') -Value ((Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($asset)) -Encoding ascii
        if(-not $SourcePrepared){
            $record=Get-Content -LiteralPath (Join-Path $source 'BUILD-RECORD.json') -Raw|ConvertFrom-Json
            $sourceZip=Join-Path $packages ('ScreenLossless_FFmpeg_'+$record.sources.ffmpegVersion+'_Source.zip')
            Compress-Archive -Path (Join-Path $source '*') -DestinationPath $sourceZip -CompressionLevel Optimal -Force
            Set-Content -LiteralPath ($sourceZip+'.sha256') -Value ((Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($sourceZip)) -Encoding ascii
        }
        Write-Host "Standalone download asset: $asset"
    }
} finally {Pop-Location}
