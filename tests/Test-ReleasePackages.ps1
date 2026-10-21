param(
    [string]$Directory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'releases/packages'),
    [string]$Version,
    [ValidateSet('Portable','Compact','Standalone','Both','All')][string]$Edition = 'All'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $Version) {
    $project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/ScreenLossless.csproj'
    $Version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
}
$Version = 'v' + $Version.TrimStart('v')
function Read-Entry([IO.Compression.ZipArchive]$Zip, [string]$Name) {
    $entry = $Zip.Entries | Where-Object { $_.FullName.Replace('\','/') -eq $Name } | Select-Object -First 1
    if (-not $entry) { throw "Missing ZIP entry: $Name" }
    return $entry
}
function Read-Text([IO.Compression.ZipArchive]$Zip, [string]$Name) {
    $reader = [IO.StreamReader]::new((Read-Entry $Zip $Name).Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Verify-DocumentLinks([IO.Compression.ZipArchive]$Zip) {
    $names = @($Zip.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    foreach ($name in $names | Where-Object { $_.EndsWith('.md') }) {
        $markdown = Read-Text $Zip $name
        foreach ($match in [regex]::Matches($markdown, '!?\[[^\]]*\]\(([^\s)]+)(?:\s+[^)]*)?\)')) {
            $target = $match.Groups[1].Value
            if ($target -match '^([a-zA-Z][a-zA-Z0-9+.-]*:|#)') { continue }
            $resolved = [uri]::new([uri]("https://package.invalid/" + $name), $target)
            $relative = [uri]::UnescapeDataString($resolved.AbsolutePath).TrimStart('/')
            if ($names -cnotcontains $relative) { throw "Broken package document link: $name -> $target" }
        }
    }
}
function Entry-Hash([IO.Compression.ZipArchiveEntry]$Entry) {
    $stream = $Entry.Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Verify-Checksum([string]$Path) {
    $expected = (Get-Content -LiteralPath ($Path + '.sha256') -Raw).Split(' ')[0].Trim()
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $expected) { throw "ZIP checksum mismatch: $Path" }
}
$sourceName = 'ScreenLossless_FFmpeg_9.0.2_Source.zip'
$sourcePath = Join-Path $Directory $sourceName
Verify-Checksum $sourcePath
$source = [IO.Compression.ZipFile]::OpenRead($sourcePath)
try {
    $recordText = Read-Text $source 'BUILD-RECORD.json'
    $record = $recordText | ConvertFrom-Json
    foreach ($dependency in $record.sources.dependencies) {
        if ((Entry-Hash (Read-Entry $source ('sources/' + $dependency.file))) -ne $dependency.sha256) { throw "Source archive mismatch: $($dependency.name)" }
    }
    foreach ($name in @('Dockerfile','build.sh','mingw-toolchain.cmake','record-build.py','build-config.mak','toolchain-packages.txt','sources.lock.json','licenses/ffmpeg-COPYING.GPLv3.txt')) {
        [void](Read-Entry $source $name)
    }
    if ((Read-Text $source 'build.sh') -notmatch '--disable-everything') { throw 'Source recipe does not match the lean build.' }
    Write-Host 'PASS complete FFmpeg source ZIP and all seven archive hashes'
    $editions = if ($Edition -in @('Both','All')) { @('Portable','Compact') } elseif ($Edition -eq 'Standalone') { @() } else { @($Edition) }
    foreach ($kind in $editions) {
        $zipPath = Join-Path $Directory ('ScreenLossless_' + $Version + '_Win64_' + $kind + '.zip')
        Verify-Checksum $zipPath
        $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            if ((Read-Text $zip 'FFmpeg-BUILD-RECORD.json') -ne $recordText) { throw "$kind references another source build." }
            foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
                if ((Entry-Hash (Read-Entry $zip $name)) -ne $record.binaries.$name.sha256) { throw "$kind has mismatched $name" }
            }
            foreach ($name in @('ScreenLossless.exe','LICENSE','licenses/NAudio-MIT.txt','ffmpeg-licenses/ffmpeg-COPYING.GPLv3.txt','README.md','FFmpeg-SOURCE.md','docs/UserGuide.md')) {
                [void](Read-Entry $zip $name)
            }
            if ($zip.Entries | Where-Object { $_.FullName.Replace('\','/') -match '(?i)(^|/)(AUDIT-[^/]+|CHANGELOG)\.md$|\.log$' }) { throw "$kind contains historical reports or logs." }
            $sourceNotice = Read-Text $zip 'FFmpeg-SOURCE.md'
            if (-not $sourceNotice.Contains($sourceName)) { throw "$kind is missing corresponding source directions." }
            if ($zip.Entries | Where-Object { $_.FullName.Replace('\','/') -match '^(ffmpeg-source|src|tests|.tools)/' }) { throw "$kind contains redundant source or development output." }
            Verify-DocumentLinks $zip
            $templateName = if ($kind -eq 'Compact') { 'Compact-README.md' } else { 'Portable-README.md' }
            $templatePath = Join-Path (Split-Path -Parent $PSScriptRoot) ('docs/' + $templateName)
            $expectedReadme = (Get-Content -LiteralPath $templatePath -Raw -Encoding UTF8).Replace('](UserGuide.md)', '](docs/UserGuide.md)').Trim()
            if ((Read-Text $zip 'README.md').Trim() -cne $expectedReadme) { throw "$kind README differs from the UTF-8 source template." }
            $exe = Read-Entry $zip 'ScreenLossless.exe'
            if ($kind -eq 'Compact') {
                if ($exe.Length -ge 5MB) { throw 'Compact unexpectedly embeds a large runtime.' }
                if ((Read-Text $zip 'README.md') -notmatch '\.NET 8 Desktop Runtime x64') { throw 'Compact runtime requirement is missing.' }
            } elseif ($exe.Length -ge 100MB -or $exe.Length -lt 10MB) { throw 'Portable compression/runtime size is unexpected.' }
            Write-Host ("PASS {0}: matching binaries, source directions, licenses and package size ({1:N2} MiB)" -f $kind, ((Get-Item -LiteralPath $zipPath).Length / 1MB))
        } finally { $zip.Dispose() }
    }
} finally { $source.Dispose() }
if($Edition -in @('Standalone','All')){
    $single=Join-Path $Directory ('ScreenLossless_'+$Version+'_Win64_Standalone.exe')
    if(-not(Test-Path -LiteralPath $single)){throw 'Standalone asset is missing.'}
    $checksum=(Get-Content -LiteralPath ($single+'.sha256') -Raw).Split(' ')[0].Trim()
    if((Get-FileHash -LiteralPath $single -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum){throw 'Standalone checksum differs.'}
    $stream=[IO.File]::OpenRead($single)
    try{if($stream.ReadByte() -ne 77 -or $stream.ReadByte() -ne 90){throw 'Standalone asset is not a Windows executable.'}}finally{$stream.Dispose()}
    if((Get-Item -LiteralPath $single).Length -gt 120MB){throw 'Standalone asset unexpectedly exceeds 120 MiB.'}
    Write-Host 'PASS Standalone: executable, checksum and size; runtime behavior is checked by Test-StandaloneRelease.ps1.'
}
Write-Host 'ALL RELEASE PACKAGE CHECKS PASSED'
