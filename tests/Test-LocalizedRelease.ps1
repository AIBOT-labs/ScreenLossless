param([ValidateSet('Portable','Compact','Both')][string]$Edition='Both')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(Get-Process ScreenLossless -ErrorAction SilentlyContinue){throw 'Close ScreenLossless before validating the release.'}
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;
public static class LocalizedReleaseWindow {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]static extern bool EnumWindows(Callback callback,IntPtr param);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr h,Callback callback,IntPtr param);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder text,int count);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder text,int count);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")]public static extern bool IsWindowEnabled(IntPtr h);
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr h,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
 public static string Text(IntPtr h){var b=new StringBuilder(1024);GetWindowText(h,b,1024);return b.ToString();}
 public static IntPtr FindMainWindow(int id,string fragment){IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==id&&Text(h).Contains(fragment)){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static IntPtr Child(IntPtr main,string text){IntPtr found=IntPtr.Zero;EnumChildWindows(main,(h,p)=>{if(Text(h)==text){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static IntPtr Language(IntPtr main){Rect window;GetWindowRect(main,out window);IntPtr found=IntPtr.Zero;EnumChildWindows(main,(h,p)=>{Rect rect;GetWindowRect(h,out rect);var c=new StringBuilder(256);GetClassName(h,c,256);if(c.ToString().Contains(".Window.")&&rect.Top-window.Top<24&&rect.Top>=window.Top&&rect.Right-rect.Left>70&&rect.Right-rect.Left<280&&rect.Bottom-rect.Top>=20&&rect.Bottom-rect.Top<70){found=h;return false;}return true;},IntPtr.Zero);return found;}
 public static void Key(IntPtr h,int key){IntPtr result;if(h==IntPtr.Zero||!IsWindowEnabled(h))throw new Exception("Language selector is not ready");if(SendMessageTimeout(h,0x100,new IntPtr(key),IntPtr.Zero,2,5000,out result)==IntPtr.Zero)throw new Exception("Language change timed out");}
 public static void Close(IntPtr h){if(h!=IntPtr.Zero)PostMessage(h,0x10,IntPtr.Zero,IntPtr.Zero);}
}
"@
function Wait-Release([string]$Fragment,[string]$Button){
 $watch=[Diagnostics.Stopwatch]::StartNew()
 while($watch.Elapsed.TotalSeconds -lt 45){
  if($script:app.HasExited){throw "Release exited unexpectedly: $($script:app.ExitCode)"}
  $window=[LocalizedReleaseWindow]::FindMainWindow($script:app.Id,$Fragment)
  if($window -ne [IntPtr]::Zero){$start=[LocalizedReleaseWindow]::Child($window,$Button);$language=[LocalizedReleaseWindow]::Language($window);if($start -ne [IntPtr]::Zero -and $language -ne [IntPtr]::Zero -and [LocalizedReleaseWindow]::IsWindowEnabled($start) -and [LocalizedReleaseWindow]::IsWindowEnabled($language)){return $window}}
  Start-Sleep -Milliseconds 100
 }
 throw "Release language did not become ready: $Fragment"
}
function Open-Release([string]$Kind){
 $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $root "releases/v1.0.0/$Kind/ScreenLossless.exe"))
 $info.UseShellExecute=$false;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
 $runtime=if($Kind -eq 'Compact'){Join-Path $root '.tools/dotnet'}else{Join-Path $root '.tools/no-installed-runtime'}
 $info.EnvironmentVariables['DOTNET_ROOT']=$runtime;$info.EnvironmentVariables['DOTNET_ROOT_X64']=$runtime;$info.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP']='0'
 $script:app=[Diagnostics.Process]::Start($info)
 return (Wait-Release 'Screen recorder' 'Start recording')
}
function Close-Release([IntPtr]$Window){
 [LocalizedReleaseWindow]::Close($Window)
 if(-not $script:app.WaitForExit(10000)){throw 'Release did not close normally.'}
 if($script:app.ExitCode -ne 0){throw "Release exit failed: $($script:app.ExitCode)"}
 $script:app.Dispose();$script:app=$null
}
$settings=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'ScreenLossless/settings.json'
$existed=Test-Path -LiteralPath $settings;$original=if($existed){[IO.File]::ReadAllBytes($settings)}else{$null}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settings) | Out-Null
$kinds=if($Edition -eq 'Both'){@('Portable','Compact')}else{@($Edition)}
try{
 foreach($kind in $kinds){
  $preferences=@{Language=2;FfmpegPath=(Join-Path $root '.tools/ffmpeg/ffmpeg.exe');OutputFolder=(Join-Path $PSScriptRoot 'artifacts/release-language');Scope=0;RecordSystemAudio=$false;RecordMicrophone=$false;Countdown=$false}
  [IO.File]::WriteAllText($settings,($preferences|ConvertTo-Json))
  $window=Open-Release $kind
  Write-Host "PASS $kind : published executable loads the English UI and all language resources"
  [LocalizedReleaseWindow]::Key([LocalizedReleaseWindow]::Language($window),0x26)
  $window=Wait-Release '屏幕录制' '开始录制'
  Write-Host "PASS $kind : actual selector changes to Chinese without restarting the process"
  [LocalizedReleaseWindow]::Key([LocalizedReleaseWindow]::Language($window),0x23)
  $window=Wait-Release 'Screen recorder' 'Start recording'
  if((Get-Content -LiteralPath $settings -Raw|ConvertFrom-Json).Language -ne 2){throw 'The release did not persist English selection.'}
  Write-Host "PASS $kind : switching back to English persists the manual choice"
  Close-Release $window
  $window=Open-Release $kind
  Write-Host "PASS $kind : reopening honors the remembered English selection"
  Close-Release $window
 }
}finally{
 if($script:app -and -not $script:app.HasExited){$window=[LocalizedReleaseWindow]::FindMainWindow($script:app.Id,'ScreenLossless');[LocalizedReleaseWindow]::Close($window);if(-not $script:app.WaitForExit(5000)){& taskkill.exe /PID $script:app.Id /T /F | Out-Null}}
 if($existed){[IO.File]::WriteAllBytes($settings,$original)}elseif(Test-Path -LiteralPath $settings){Remove-Item -LiteralPath $settings -Force}
}
