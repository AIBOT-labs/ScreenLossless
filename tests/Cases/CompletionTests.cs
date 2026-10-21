using NAudio.Wave;
using System.Diagnostics;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static async Task TestCompletionAsync()
    {
        var format=WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
        var samples=new[]{0f,-.5f,.25f,float.NaN,float.PositiveInfinity};
        var buffer=samples.SelectMany(BitConverter.GetBytes).ToArray();
        Check(AudioPeak.Measure(buffer,format)==.5f,"Audio meter measures floating-point peaks and ignores invalid samples");
        Check(AudioPeak.Measure(new byte[]{0,128,255,127},new WaveFormat(48000,16,1))==1,"Audio meter reads signed PCM16 including negative full scale");
        Check(AudioPeak.Measure(new byte[]{128,128},new WaveFormat(48000,8,1))==0,"Unsigned PCM8 silence has zero level");
        Check(AudioPeak.Measure(new byte[]{0,0,128},new WaveFormat(48000,24,1))==1,"PCM24 metering sign extends negative full scale");
        var peak=new AudioPeak();peak.Observe(buffer,buffer.Length,format);
        Check(peak.Level==.5f,"Meter publishes the current captured peak");await Task.Delay(500);
        Check(peak.Level==0,"Silent loopback stops displaying stale audio peaks");
        Check(StorageGuard.Evaluate(StorageGuard.Margin,0).Stop&&!StorageGuard.Evaluate(8L<<30,0).Warn,"Storage guard distinguishes critical and sufficient free space");
        Check(StorageGuard.Evaluate(3L<<30,2L<<30).Stop&&StorageGuard.Evaluate(9L<<30,2L<<30).Reserve==(4L<<30)+StorageGuard.Margin,"Storage guard reserves both mux and concat output space");
        Check(StorageGuard.Evaluate(long.MaxValue,long.MaxValue).Reserve==long.MaxValue,"Storage estimates saturate instead of overflowing");
        var identity=new WindowSelection{ProcessPath="C:/Apps/Editor.exe",ClassName="Editor",Title="Document"};
        var window=new WindowInfo(new IntPtr(123),"Document",new Rectangle(0,0,320,180));
        Check(identity.Resolve(new[]{(window,(WindowSelection?)identity)})==window,"Saved window matches a unique process, class and title");
        Check(identity.Resolve(new[]{(window,(WindowSelection?)identity),(window with{Handle=new IntPtr(124)},identity)})is null,"Ambiguous identical windows require user selection");
        Check(identity.Resolve(new[]{(window,(WindowSelection?)new WindowSelection{ProcessPath="other.exe",ClassName="Editor",Title="Document"})})is null,"Same-titled windows from another program are not restored");
        string ready=Path.Combine(_root,"window-ready-"+Guid.NewGuid().ToString("N"));
        string stop=ready+".stop";
        var childStart=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))childStart.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(RegressionTests).Assembly.GetName().Name + ".dll"));
        foreach(var argument in new[]{"--window-memory-fixture",ready,stop})childStart.ArgumentList.Add(argument);
        using(var child=Process.Start(childStart)!)
        {
            try
            {
                await Until(()=>File.Exists(ready));
                IntPtr handle=new(long.Parse(await File.ReadAllTextAsync(ready)));
                var candidate=WindowsInterop.GetVisibleWindows().Single(x=>x.Handle==handle);
                var memory=WindowsInterop.DescribeWindow(candidate)!;
                new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=_root,Scope=RecordingScope.Window,LastWindow=memory,RecordSystemAudio=false,Countdown=false}.Save();
                using var reopened=new MainForm{Opacity=0,ShowInTaskbar=false};reopened.Show();
                await Until(()=>!Field<bool>(reopened,"_busy")&&Field<Capabilities>(reopened,"_caps").Available);
                Check(Field<WindowInfo>(reopened,"_window").Handle==handle&&Field<AppSettings>(reopened,"_settings").Scope==RecordingScope.Window,
                    "Reopening the recorder restores the exact running target from saved stable identity");
                reopened.Close();
            }
            finally{File.WriteAllText(stop,"stop");using var timeout=new CancellationTokenSource(5000);
                try{await child.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){child.Kill(true);}}
        }
        var work=Screen.PrimaryScreen!.WorkingArea;
        using var target=new Form{Text="Completion owned target",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(work.Left+80,work.Top+100,320,180),BackColor=Color.FromArgb(25,125,80),ShowInTaskbar=false,TopMost=true};
        target.Show();
        var nativeIdentity=WindowsInterop.DescribeWindow(new(target.Handle,target.Text,target.Bounds));
        Check(nativeIdentity?.Valid==true&&nativeIdentity.Title==target.Text,"Native window identity uses actual program path and window class");
        string folder=Path.Combine(_root,"completion");Directory.CreateDirectory(folder);
        new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,Encoder=EncoderChoice.X264Rgb,Countdown=false,
            RecordSystemAudio=false,RecordMicrophone=false,UseDxgi=false,MinimizeWhenRecording=true,
            LastWindow=identity,HudPosition=new(){X=work.Left+20,Y=work.Top+20}}.Save();
        Check(AppSettings.Load().LastWindow?.Title=="Document"&&AppSettings.Load().HudPosition?.X==work.Left+20,"Window identity and floating coordinates roundtrip settings");
        using(var main=new MainForm{Opacity=0,ShowInTaskbar=false})
        {
            main.Show();await Until(()=>!Field<bool>(main,"_busy")&&Field<Capabilities>(main,"_caps").Available);
            var engine=Field<RecorderEngine>(main,"_engine");var hud=Field<RecordingHud>(main,"_recordingHud");hud.Opacity=0;
            var settings=Field<AppSettings>(main,"_settings");settings.Scope=RecordingScope.Region;
            Set(main,"_region",new Rectangle(target.Left+10,target.Top+10,128,72));Invoke(main,"PlanChanged");
            Field<List<SoftButton>>(main,"_settingsTabs")[1].PerformClick();Invoke(main,"UpdateAudioLevels");
            Check(Field<AudioLevelPreview>(main,"_audioPreview")is not null&&!Directory.Exists(Path.Combine(folder,".screenlossless-work")),"Idle sound-page preview creates no recording or audio files");
            var systemBox=Field<ChoiceBox>(main,"_systemDeviceBox");systemBox.Items.Add(new AudioDeviceChoice("not-an-active-test-endpoint","测试不可用设备"));
            systemBox.SelectedIndex=systemBox.Items.Count-1;Field<CheckBox>(main,"_systemSound").Checked=true;
            Invoke(main,"UpdateAudioLevels");
            Check(!Field<AudioLevelPreview>(main,"_audioPreview").SystemAvailable&&Field<AudioLevelBar>(main,"_systemLevelBar").AccessibleName!.Contains("不可用"),
                "Disconnected preview endpoint reports unavailable instead of a misleading silent meter");
            var unavailablePreview = Field<AudioLevelPreview>(main, "_audioPreview");
            Check(unavailablePreview.NeedsReconnect, "Unavailable idle audio preview requests recovery");
            Set(main, "_nextPreviewRetry", DateTime.MinValue); Invoke(main, "UpdateAudioLevels");
            Check(!ReferenceEquals(unavailablePreview, Field<AudioLevelPreview>(main, "_audioPreview")), "Idle preview recreates a disconnected source without changing the selected endpoint");
            Check(settings.SystemDeviceId == "not-an-active-test-endpoint", "Idle preview recovery preserves a pinned device instead of changing to default");
            Field<CheckBox>(main,"_systemSound").Checked=false;
            Field<AudioLevelBar>(main,"_systemLevelBar").UpdateLevel(.5f,"-6.0 dB · 音量 100%");
            Field<AudioLevelBar>(main,"_micLevelBar").UpdateLevel(.2f,"-14.0 dB · 音量 100%");
            Snapshot(main,"completion-audio-levels");CheckButtonsFit(main);
            Field<List<SoftButton>>(main,"_settingsTabs")[0].PerformClick();Invoke(main,"UpdateAudioLevels");
            Check(Field<AudioLevelPreview?>(main,"_audioPreview")is null,"Leaving the sound page releases preview capture devices");
            await InvokeAsync(main,"StartRecordingAsync");
            Check(hud.Location==settings.HudPosition!.Point,"Recording HUD restores saved coordinates when starting");
            var position=new Point(work.Right-hud.Width-20,work.Top+20);hud.Location=position;
            SendOwnMessage(hud.Handle,0x232,IntPtr.Zero,IntPtr.Zero);
            Check(AppSettings.Load().HudPosition!.Point==position,"Completing a user HUD drag persists the floating position");
            await InvokeAsync(main,"TogglePauseAsync");
            Set(main,"_spaceReader",new Func<string,long?>(_=>StorageGuard.Margin/2));
            Set(main,"_workspaceSizer",new Func<string,long>(_=>0));Invoke(main,"RefreshStorage");
            await Until(()=>engine.State==RecorderState.Idle&&!Field<bool>(main,"_busy"));
            Check(File.Exists(engine.OutputFile)&&engine.LastStopReason.Contains("磁盘空间保护"),"Low disk space automatically stops and saves a paused recording");
            Check((await Hashes(engine.OutputFile)).Length>0,"Storage-protected recording remains decodable");
            Set(main,"_spaceReader",new Func<string,long?>(_=>100L<<30));
            await InvokeAsync(main,"StartRecordingAsync");
            Check(hud.Location==position,"Next recording reuses the last committed floating position");
            await InvokeAsync(main,"StopRecordingAsync");
            settings.HudPosition=new(){X=int.MinValue,Y=int.MaxValue};
            Call(main,"PlaceRecordingHud",target.Bounds,true);
            Check(Screen.AllScreens.Any(x=>x.WorkingArea.Contains(hud.Bounds)),"Invalid or disconnected-monitor HUD position is clamped before showing");
            main.Close();
        }
        string directory=Path.Combine(folder,".screenlossless-work","clean-test");Directory.CreateDirectory(directory);
        string video=Path.Combine(directory,"video-001.mkv");
        await Run("-v","error","-y","-f","lavfi","-i","testsrc=size=128x72:rate=10:duration=1","-c:v","libx264rgb","-crf","0",video);
        var manifest=new RecordingManifest{TargetFile="cleanup-saved.mkv",Segments=new(){new("video-001.mkv",null,null)}};manifest.Save(directory);
        bool refused=false;try{RecoveryStorage.CleanupCompleted(folder,directory);}catch(InvalidOperationException){refused=true;}
        Check(refused&&File.Exists(video),"Cleanup refuses unfinished recovery material");
        string saved=await RecordingFinalizer.ExportAsync(_caps,directory,manifest,_=>{});
        long expected=Directory.GetFiles(directory).Sum(x=>new FileInfo(x).Length);
        Check(RecordingFinalizer.Discover(folder).All(x=>x.Directory!=directory)&&RecordingFinalizer.Discover(folder,true).Single(x=>x.Directory==directory).Bytes==expected,"Completed recovery material is separately discoverable with accurate disk usage");
        string moved=saved+".moved";File.Move(saved,moved);refused=false;
        try{RecoveryStorage.CleanupCompleted(folder,directory);}catch(InvalidOperationException){refused=true;}finally{File.Move(moved,saved);}
        Check(refused&&Directory.Exists(directory),"Cleanup keeps source material when its completed video has been moved");
        refused=false;try{RecoveryStorage.CleanupCompleted(folder,folder);}catch(InvalidOperationException){refused=true;}
        Check(refused&&Directory.Exists(folder),"Cleanup rejects paths outside an immediate work-session directory");
        var savedBytes=await File.ReadAllBytesAsync(saved);
        using(var recovery=new RecoveryForm(folder,_caps){Opacity=0})
        {
            recovery.Show();Field<ChoiceBox>(recovery,"_filter").SelectedIndex=1;
            Check(Field<DetailListBox>(recovery,"_list").Items.Count==1&&Field<SoftButton>(recovery,"_clean").Enabled&&!Field<SoftButton>(recovery,"_recover").Enabled,"Completed-material filter enables cleanup instead of duplicate recovery");
            Snapshot(recovery,"completion-recovery-storage");CheckButtonsFit(recovery);
            await (Task)Call(recovery,"CleanupSelectedAsync",true)!;
            Check(!Directory.Exists(directory)&&File.Exists(saved)&&savedBytes.SequenceEqual(await File.ReadAllBytesAsync(saved)),"Confirmed cleanup removes only recovered source material and preserves the exact saved video");
            Check(!Field<SoftButton>(recovery,"_clean").Enabled&&Field<DetailListBox>(recovery,"_list").Items.Count==0,"Cleanup refreshes the list and disables stale actions");recovery.Close();
        }
        if(_caps.GfxCapture)
        {
            var client=WindowsInterop.GetWindowClientBounds(target.Handle);
            var modern=new RecordPlan(client,null,RecordingScope.Window,RecordingPurpose.Desktop,EncoderChoice.X264Rgb,CaptureBackend.WindowsGraphics,30,false,false,false,1,1,WindowHandle:target.Handle.ToInt64());
            string graph=FfmpegArguments.GraphicsWindowSource(modern);
            Check(graph.Contains("hwnd="+target.Handle.ToInt64())&&graph.Contains("hwdownload")&&graph.Contains("fps=30"),"Modern capture pins an explicit HWND and fixed output frame rate");
            var gdiCaps=new Capabilities{Available=true,X264Rgb=true};
            Check(HardwareProbe.ResolveBackend(true,RecordingScope.Window,EncoderChoice.X264Rgb,null,client,gdiCaps)==CaptureBackend.Gdi,"FFmpeg without modern capture retains GDI compatibility");
            using var repaint=new System.Windows.Forms.Timer{Interval=50};int frame=0;
            target.Paint+=(_,e)=>e.Graphics.FillRectangle(Brushes.Blue,5+(frame++%20),5,3,3);
            repaint.Tick+=(_,_)=>target.Refresh();repaint.Start();
            using var cover=new Form{StartPosition=FormStartPosition.Manual,Bounds=target.Bounds,BackColor=Color.Red,TopMost=true,ShowInTaskbar=false};
            cover.Show();cover.Activate();await Task.Delay(120);
            using(var captured=await MediaProcess.ReadImageAsync(_caps.Ffmpeg,new[]{"-v","error","-f","lavfi","-i",graph,"-frames:v","1","-f","image2pipe","-vcodec","png","pipe:1"},CancellationToken.None))
                Check(captured.Size==client.Size&&captured.GetPixel(100,80).ToArgb()==target.BackColor.ToArgb(),"Modern window preview captures the selected client's pixels beneath an occluding window");
            using var engine=new RecorderEngine();
            await engine.StartAsync(modern,_caps,folder,false);await Task.Delay(300);
            Check(engine.CurrentPlan!.Backend==CaptureBackend.WindowsGraphics&&engine.FrameCount>0,"Modern recording receives actual frames without falling back");
            await engine.PauseAsync();await engine.ResumeAsync();await Task.Delay(300);string videoFile=await engine.StopAsync();
            Check((await Hashes(videoFile)).LongLength==engine.FrameCount,"Modern pause and resume save every captured frame with compatible segments");
            cover.Close();repaint.Stop();
        }
        else Console.WriteLine("SKIP modern native capture: FFmpeg lacks gfxcapture");
        target.Close();
    }
}
