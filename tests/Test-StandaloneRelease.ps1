param([string]$Executable)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $Executable){$Executable=Join-Path $root 'releases/packages/ScreenLossless_v1.0.0_Win64_Standalone.exe'}
if(Get-Process -ErrorAction SilentlyContinue | Where-Object {$_.ProcessName -like 'ScreenLossless*'}){throw 'Close ScreenLossless before standalone validation.'}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class StandaloneWindow {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumWindows(Callback f,IntPtr p);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr h,Callback f,IntPtr p);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")]public static extern bool IsWindowEnabled(IntPtr h);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint t,out IntPtr r);
 public static string Text(IntPtr h){var b=new StringBuilder(2048);GetWindowText(h,b,2048);return b.ToString();}
 public static IntPtr Window(int id,string title){IntPtr r=IntPtr.Zero;EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==id&&Text(h).Contains(title)){r=h;return false;}return true;},IntPtr.Zero);return r;}
 public static IntPtr Child(IntPtr parent,string text){IntPtr r=IntPtr.Zero;EnumChildWindows(parent,(h,p)=>{if(Text(h)==text){r=h;return false;}return true;},IntPtr.Zero);return r;}
 public static string[] Texts(IntPtr parent){var values=new System.Collections.Generic.List<string>();EnumChildWindows(parent,(h,p)=>{string value=Text(h);if(value.Length>0)values.Add(value);return true;},IntPtr.Zero);return values.ToArray();}
 public static bool Contains(IntPtr parent,string text){bool found=false;EnumChildWindows(parent,(h,p)=>{if(Text(h).Contains(text)){found=true;return false;}return true;},IntPtr.Zero);return found;}
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
 public static IntPtr Language(IntPtr main){if(main==IntPtr.Zero)return IntPtr.Zero;Rect w;GetWindowRect(main,out w);double scale=GetDpiForWindow(main)/96.0;IntPtr found=IntPtr.Zero;EnumChildWindows(main,(h,p)=>{Rect r;GetWindowRect(h,out r);var c=new StringBuilder(256);GetClassName(h,c,256);if(c.ToString().Contains(".Window.")&&r.Top-w.Top<24*scale&&r.Top>=w.Top&&r.Right-r.Left>130*scale&&r.Right-r.Left<200*scale&&r.Bottom-r.Top>=20*scale&&r.Bottom-r.Top<70*scale){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static void Key(IntPtr h,int key){IntPtr result;if(h==IntPtr.Zero||!IsWindowEnabled(h))throw new Exception("Language selector unavailable");if(SendMessageTimeout(h,0x100,new IntPtr(key),IntPtr.Zero,2,5000,out result)==IntPtr.Zero)throw new Exception("Language change timed out");}
 public static void Click(IntPtr h){IntPtr result;if(h==IntPtr.Zero||!IsWindowEnabled(h))throw new Exception("Control is unavailable");if(SendMessageTimeout(h,0xF5,IntPtr.Zero,IntPtr.Zero,2,5000,out result)==IntPtr.Zero)throw new Exception("Control timed out");}
 public static void Close(IntPtr h){if(h!=IntPtr.Zero)PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);}
}
"@
function Wait-Condition([scriptblock]$Condition,[string]$Message,[int]$Seconds=60){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while($watch.Elapsed.TotalSeconds -lt $Seconds){[Windows.Forms.Application]::DoEvents();if($script:app -and $script:app.HasExited){throw "Standalone exited: $($script:app.ExitCode)"};if(& $Condition){Write-Host "PASS $Message";return};Start-Sleep -Milliseconds 80}
 throw "Timeout: $Message"
}
$folder=Join-Path $root ('tests/artifacts/standalone-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$download=Join-Path $folder 'download';$recordings=Join-Path $folder 'recordings'
New-Item -ItemType Directory -Force -Path $download,$recordings|Out-Null
$copy=Join-Path $download ([IO.Path]::GetFileName($Executable));Copy-Item -LiteralPath $Executable -Destination $copy
$settings=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'ScreenLossless/settings.json'
$existed=Test-Path -LiteralPath $settings;$original=if($existed){[IO.File]::ReadAllBytes($settings)}else{$null}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settings)|Out-Null
$work=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$crop=[Drawing.Rectangle]::new($work.Left+180,$work.Top+240,320,180)
$target=[Windows.Forms.Form]::new();$target.FormBorderStyle='None';$target.TopMost=$true;$target.ShowInTaskbar=$false;$target.StartPosition='Manual'
$target.Bounds=[Drawing.Rectangle]::Inflate($crop,10,10);$target.BackColor=[Drawing.Color]::FromArgb(30,140,85)
$script:app=$null
try{
 $preferences=@{Language=2;Theme=1;FfmpegPath='';OutputFolder=$recordings;Scope=1;LastRegion=@{X=$crop.X;Y=$crop.Y;Width=$crop.Width;Height=$crop.Height};Encoder=2;Fps=15;RecordSystemAudio=$false;RecordMicrophone=$false;RecordCursor=$false;Countdown=$false;UseDxgi=$false;MinimizeWhenRecording=$true;ExcludeHudFromCapture=$true}
 [IO.File]::WriteAllText($settings,($preferences|ConvertTo-Json -Depth 4))
 function Start-Standalone {
  $info=[Diagnostics.ProcessStartInfo]::new($copy);$info.UseShellExecute=$false;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
  $info.EnvironmentVariables['PATH']=$env:SystemRoot+'\System32;'+$env:SystemRoot
  $info.EnvironmentVariables['DOTNET_ROOT']=Join-Path $folder 'no-installed-runtime';$info.EnvironmentVariables['DOTNET_ROOT_X64']=$info.EnvironmentVariables['DOTNET_ROOT'];$info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP']='0'
  $info.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path $folder 'native-extract'
  $script:app=[Diagnostics.Process]::Start($info)
 }
 Start-Standalone
 Wait-Condition { $script:main=[StandaloneWindow]::Window($script:app.Id,'Screen recorder');$button=[StandaloneWindow]::Child($script:main,'Start recording');$language=[StandaloneWindow]::Language($script:main);$script:main -ne [IntPtr]::Zero -and $button -ne [IntPtr]::Zero -and [StandaloneWindow]::IsWindowEnabled($button) -and $language -ne [IntPtr]::Zero -and [StandaloneWindow]::IsWindowEnabled($language) } 'Cold standalone launch loads the UI without an installed runtime or external tools'
 Wait-Condition {if([StandaloneWindow]::Contains($script:main,'Detection failed')){throw ([string]::Join(' | ',[StandaloneWindow]::Texts($script:main)))};[StandaloneWindow]::Contains($script:main,'✓ Ready') -and [StandaloneWindow]::IsWindowEnabled([StandaloneWindow]::Child($script:main,'Start recording'))} 'Standalone completes encoder detection'
 $payload=[IO.Compression.ZipFile]::OpenRead((Join-Path $root '.tools/standalone-payload/tools.zip'))
 try{$index=$payload.GetEntry('bundle-manifest.json').Open();$memory=[IO.MemoryStream]::new();try{$index.CopyTo($memory);$sha=[Security.Cryptography.SHA256]::Create();try{$id=([BitConverter]::ToString($sha.ComputeHash($memory.ToArray()))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}}finally{$index.Dispose();$memory.Dispose()}}finally{$payload.Dispose()}
 $tools=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('ScreenLossless/components/'+$id)
 $cachedFfmpeg=Join-Path $tools 'ffmpeg.exe'
 foreach($name in @('ffmpeg.exe','ffprobe.exe')){
  if((Get-FileHash -LiteralPath (Join-Path $tools $name) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root ('.tools/ffmpeg/'+$name)) -Algorithm SHA256).Hash){throw "Embedded binary mismatch: $name"}
 }
 foreach($name in @('LICENSE','FFmpeg-BUILD-RECORD.json','FFmpeg-SOURCE.md','ffmpeg-licenses/ffmpeg-COPYING.GPLv3.txt','licenses/NAudio-MIT.txt','docs/UserGuide.md')){if(-not(Test-Path -LiteralPath (Join-Path $tools $name))){throw "Missing embedded material: $name"}}
 Write-Host 'PASS Embedded encoder, probe, build record, licenses and guides match the release'
 $stamp=(Get-Item -LiteralPath $cachedFfmpeg).LastWriteTimeUtc
 $target.Show();$target.Refresh();[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 150
 [StandaloneWindow]::Click([StandaloneWindow]::Child($script:main,'Start recording'))
 Wait-Condition {$script:hud=[StandaloneWindow]::Window($script:app.Id,'Recording controls');$script:hud -ne [IntPtr]::Zero -and [StandaloneWindow]::Contains($script:hud,'Recording') -and [StandaloneWindow]::IsWindowEnabled([StandaloneWindow]::Child($script:hud,'Pause'))} 'Single EXE starts a real recording with its floating controls'
 if(-not @(Get-Process -Name ffmpeg -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq $cachedFfmpeg})){throw 'The recording did not use the embedded encoder.'}
 Write-Host 'PASS The active recording uses the embedded encoder from its component cache'
 Start-Sleep -Milliseconds 1200;[Windows.Forms.Application]::DoEvents()
 [StandaloneWindow]::Click([StandaloneWindow]::Child($script:hud,'Pause'))
 Wait-Condition {[StandaloneWindow]::Child($script:hud,'Resume') -ne [IntPtr]::Zero} 'Floating pause works in the standalone release'
 [StandaloneWindow]::Click([StandaloneWindow]::Child($script:hud,'Resume'))
 Wait-Condition {[StandaloneWindow]::Child($script:hud,'Pause') -ne [IntPtr]::Zero -and [StandaloneWindow]::IsWindowEnabled([StandaloneWindow]::Child($script:hud,'Pause'))} 'Floating resume works in the standalone release'
 Start-Sleep -Milliseconds 1000;[Windows.Forms.Application]::DoEvents();[StandaloneWindow]::Click([StandaloneWindow]::Child($script:hud,'Stop'))
 Wait-Condition {$files=@(Get-ChildItem -LiteralPath $recordings -Filter '*.mkv');$files.Count -eq 1 -and [StandaloneWindow]::Contains($script:main,'Saved')} 'Standalone saves the finished recording and restores the main window'
 $video=(Get-ChildItem -LiteralPath $recordings -Filter '*.mkv'|Select-Object -First 1).FullName
 $probeText=& (Join-Path $tools 'ffprobe.exe') -v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames,width,height -of json $video
 if($LASTEXITCODE -ne 0){throw 'Embedded probe could not read the saved file.'}
 $stream=($probeText -join "`n"|ConvertFrom-Json).streams[0]
 if([int]$stream.nb_read_frames -le 0 -or $stream.width -ne 320 -or $stream.height -ne 180){throw 'Standalone recording has invalid dimensions or frames.'}
 $md5=& (Join-Path $tools 'ffmpeg.exe') -v error -i $video -map 0:v:0 -pix_fmt rgb24 -fps_mode passthrough -f framemd5 -
 if($LASTEXITCODE -ne 0){throw 'Standalone recording does not decode.'}
 $frames=@($md5|Where-Object {$_ -match '^\d+,'});$hashes=@($frames|ForEach-Object {($_ -split ',')[-1].Trim()}|Select-Object -Unique)
 if($frames.Count -ne [int]$stream.nb_read_frames -or $hashes.Count -ne 1){throw 'The standalone recording lost frames or included its outside overlays.'}
 Write-Host "PASS Standalone recording decodes all $($frames.Count) RGB frames across pause/resume"
 [StandaloneWindow]::Close($script:main);if(-not $script:app.WaitForExit(10000)){throw 'Standalone did not close.'};if($script:app.ExitCode -ne 0){throw 'Standalone exit failed.'};$script:app.Dispose();$script:app=$null
 Start-Standalone
 Wait-Condition {$script:main=[StandaloneWindow]::Window($script:app.Id,'Screen recorder');$button=[StandaloneWindow]::Child($script:main,'Start recording');$language=[StandaloneWindow]::Language($script:main);$button -ne [IntPtr]::Zero -and [StandaloneWindow]::IsWindowEnabled($button) -and $language -ne [IntPtr]::Zero -and [StandaloneWindow]::IsWindowEnabled($language)} 'Warm standalone launch reuses its prepared components'
 if((Get-Item -LiteralPath $cachedFfmpeg).LastWriteTimeUtc -ne $stamp){throw 'Warm launch rewrote a valid embedded encoder.'}
 [StandaloneWindow]::Key([StandaloneWindow]::Language($script:main),0x26)
 Wait-Condition {$script:main=[StandaloneWindow]::Window($script:app.Id,'屏幕录制');$language=[StandaloneWindow]::Language($script:main);[StandaloneWindow]::Child($script:main,'开始录制') -ne [IntPtr]::Zero -and $language -ne [IntPtr]::Zero -and [StandaloneWindow]::IsWindowEnabled($language)} 'Standalone switches to Chinese within the same process'
 [StandaloneWindow]::Key([StandaloneWindow]::Language($script:main),0x23)
 Wait-Condition {$script:main=[StandaloneWindow]::Window($script:app.Id,'Screen recorder');$language=[StandaloneWindow]::Language($script:main);[StandaloneWindow]::Child($script:main,'Start recording') -ne [IntPtr]::Zero -and $language -ne [IntPtr]::Zero -and [StandaloneWindow]::IsWindowEnabled($language)} 'Standalone switches back to English with its embedded resources'

 if(@(Get-ChildItem -LiteralPath $download -Force).Count -ne 1){throw 'The standalone left dependency files beside the downloaded EXE.'}
 [StandaloneWindow]::Close($script:main);if(-not $script:app.WaitForExit(10000)){throw 'Warm standalone did not close.'};$script:app.Dispose();$script:app=$null
 Write-Host 'ALL STANDALONE RELEASE CHECKS PASSED'
}finally{
 if($script:app -and -not $script:app.HasExited){[StandaloneWindow]::Close([StandaloneWindow]::Window($script:app.Id,'ScreenLossless'));if(-not $script:app.WaitForExit(5000)){& taskkill.exe /PID $script:app.Id /T /F | Out-Null;$script:app.WaitForExit()}}
 $target.Close();$target.Dispose()
 if($existed){[IO.File]::WriteAllBytes($settings,$original)}elseif(Test-Path -LiteralPath $settings){Remove-Item -LiteralPath $settings -Force}
}
