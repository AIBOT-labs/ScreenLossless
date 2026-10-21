using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process,int flags);
    private static async Task TestResourceUsageAsync(bool validate=true)
    {
        string resourceFolder=Path.Combine(_root,"resources");Directory.CreateDirectory(resourceFolder);
        new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=resourceFolder,Scope=RecordingScope.FullScreen,
            RecordSystemAudio=false,RecordMicrophone=false,Countdown=false}.Save();
        using var main=new MainForm{ShowInTaskbar=false};main.Show();await Until(()=>main.CanChangeLanguage);
        await Task.Delay(2000);
        var audioTimer=Field<System.Windows.Forms.Timer>(main,"_audioTicker");
        var systemBar=Field<AudioLevelBar>(main,"_systemLevelBar");int audioInvalidations=0;
        systemBar.Invalidated+=(_,_)=>audioInvalidations++;
        async Task Measure(string mode)
        {
            using var process=Process.GetCurrentProcess();process.Refresh();
            var cpu=process.TotalProcessorTime;long allocated=GC.GetTotalAllocatedBytes(true);
            int invalidations=audioInvalidations;var watch=Stopwatch.StartNew();await Task.Delay(4000);process.Refresh();
            Console.WriteLine($"RESOURCE {mode}: cpu_ms={(process.TotalProcessorTime-cpu).TotalMilliseconds:0.0}; allocated_KiB={(GC.GetTotalAllocatedBytes(true)-allocated)/1024d:0.0}; private_MiB={process.PrivateMemorySize64/1048576d:0.0}; GDI={GetGuiResources(process.Handle,0)}; audio_invalidations={audioInvalidations-invalidations}; audio_timer={audioTimer.Enabled}; wall_ms={watch.Elapsed.TotalMilliseconds:0}");
        }
        await Measure("idle-fullscreen");
        var settings=Field<AppSettings>(main,"_settings");var work=Screen.PrimaryScreen!.WorkingArea;
        settings.Scope=RecordingScope.Region;
        typeof(MainForm).GetField("_region",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.SetValue(main,new Rectangle(work.Left+200,work.Top+240,320,180));
        Invoke(main,"PlanChanged");await Task.Delay(500);await Measure("idle-region");
        if(validate)
        {
            Check(!audioTimer.Enabled,"Idle video page does not run the 100 ms audio timer");
            Field<List<SoftButton>>(main,"_settingsTabs")[1].PerformClick();
            Field<CheckBox>(main,"_systemSound").Checked=true;await Task.Delay(80);
            Check(audioTimer.Enabled,"Visible enabled sound meters start the audio timer");
            main.Hide();await Task.Delay(40);
            Check(!audioTimer.Enabled&&Field<AudioLevelPreview?>(main,"_audioPreview") is null,"Hiding main stops metering and releases idle preview capture");
            main.Show();await Task.Delay(40);Field<List<SoftButton>>(main,"_settingsTabs")[0].PerformClick();
            Check(!audioTimer.Enabled&&Field<AudioLevelPreview?>(main,"_audioPreview") is null,"Leaving the sound page immediately releases preview capture and polling");
            Field<CheckBox>(main,"_systemSound").Checked=false;
            using(var surface=new Form{ShowInTaskbar=false})
            using(var bar=new AudioLevelBar())
            using(var scroll=new ThemedScrollBar(true))
            {
                surface.Controls.Add(bar);surface.Controls.Add(scroll);surface.Show();bar.UpdateLevel(.25f,"steady");scroll.SetMetrics(0,99,10,20);
                int redraws=0;bar.Invalidated+=(_,_)=>redraws++;scroll.Invalidated+=(_,_)=>redraws++;
                for(int i=0;i<100;i++){bar.UpdateLevel(.25f,"steady");scroll.SetMetrics(0,99,10,20);}
                Check(redraws==0,"Unchanged meter values and scroll metrics cause no redundant invalidations");surface.Close();
            }
            using(var hud=new RecordingHud())
            {
                var started=DateTime.Now;var elapsed=TimeSpan.FromSeconds(12);var speed=new PerformanceSnapshot(30,1,0,true);var limit=new RecordingLimit(5);
                hud.UpdateState(RecorderState.Recording,false,0,elapsed,started,limit:limit,performance:speed);
                var stop=Field<SoftButton>(hud,"_stop");var font=stop.Font;int textChanges=0;
                Field<Label>(hud,"_status").TextChanged+=(_,_)=>textChanges++;
                Field<Label>(hud,"_started").TextChanged+=(_,_)=>textChanges++;
                for(int i=0;i<500;i++)hud.UpdateState(RecorderState.Recording,false,0,elapsed,started,limit:limit,performance:speed);
                Check(ReferenceEquals(font,stop.Font)&&textChanges==0,"Steady HUD updates reuse fonts and do not reset speed or remaining-time labels");
                hud.UpdateState(RecorderState.Finalizing,true,0,elapsed,started,canCancelSaving:true,saveProgress:.5);
                Check(stop.Font.Size==8.5f&&stop.Enabled,"Saving still applies compact cancel controls");
                hud.UpdateState(RecorderState.Paused,false,0,elapsed,started,limit:limit);
                Check(stop.Font.Size==9.5f&&Field<Label>(hud,"_started").Text.Contains("剩余"),"Paused HUD retains the limit and restores normal control typography");
            }
            foreach(var size in new[]{new Size(1280,720),new Size(1920,1080),new Size(2560,1440),new Size(3840,2160),new Size(7680,4320)})
            {
                int frames=FfmpegArguments.GdiQueueFrames(size);long bytes=(long)size.Width*size.Height*4;
                Check(frames is >=1 and <=8&&(frames==1||frames*bytes<=64L*1024*1024),"Raw capture queue obeys a 64 MiB target or one large frame: "+size);
            }
            string folder=Path.Combine(resourceFolder,"size-counter");Directory.CreateDirectory(folder);
            string first=Path.Combine(folder,"video-001.mkv");var counter=new ContinuousVideoSize(first);
            Check(counter.ReadBytes()==0,"Size tracking tolerates capture before the first packet");
            long expected=0;
            for(int i=0;i<600;i++){int count=i%7+1;File.WriteAllBytes(Path.Combine(folder,$"video-001-part-{i:000000}.mkv"),new byte[count]);expected+=count;}
            Check(counter.ReadBytes()==expected,"Incremental size tracking catches up with hundreds of closed parts");
            Check(Enumerable.Range(0,20).All(_=>counter.ReadBytes()==expected),"Repeated size checks preserve the current total");
            File.WriteAllBytes(Path.Combine(folder,"video-001-part-000600.mkv"),new byte[13]);expected+=13;
            Check(counter.ReadBytes()==expected,"New chunks advance the cached closed-file total");
            using(var stream=new FileStream(Path.Combine(folder,"video-001-part-000600.mkv"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite))stream.Write(new byte[19]);
            Check(counter.ReadBytes()==expected+19,"Growing current chunks are measured without rescanning closed files");
            File.WriteAllBytes(Path.Combine(folder,"staged-result.mkv"),new byte[91]);
            Check(StorageGuard.WorkspaceBytes(folder)==expected+19,"Workspace sizing batches source file metadata and excludes staged outputs");
            var progress=new RecordingPerformance();progress.Observe("fps=29.97");progress.Observe("speed=1.05x");
            long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)progress.Observe("out_time_us=100000");
            Check(GC.GetAllocatedBytesForCurrentThread()-before<1024&&progress.Snapshot.Fps==29.97,"Unrelated progress fields do not allocate parser substrings or corrupt statistics");
            string timestampFixture=Path.Combine(resourceFolder,"duplicate-timestamps.mkv");
            await Run("-v","error","-y","-f","lavfi","-i","color=c=green:s=128x72:r=30","-vf","setpts=PTS/2","-frames:v","30","-fps_mode","passthrough","-c:v","libx264rgb","-crf","0","-bf","0",timestampFixture);
            Check((await Hashes(timestampFixture)).Length==30,"Frame verification preserves actual packets even when adjacent frames have equal timestamps");
            var crop=new Rectangle(work.Left+600,work.Top+300,320,180);
            using var target=new Form{FormBorderStyle=FormBorderStyle.None,TopMost=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=Rectangle.Inflate(crop,8,8),BackColor=Color.FromArgb(31,121,77)};
            target.Show();using var engine=new RecorderEngine{ChunkSeconds=1};
            var plan=new RecordPlan(crop,null,RecordingScope.Region,RecordingPurpose.Desktop,EncoderChoice.X264Rgb,CaptureBackend.Gdi,15,false,false,false,1,1);
            var args=FfmpegArguments.Capture(plan,"unused.mkv");
            Check(args[args.IndexOf("-thread_queue_size")+1]=="8","GDI launch arguments use the bounded queue");
            await engine.StartAsync(plan,_caps,resourceFolder,false);using(var process=Process.GetProcessById(engine.CaptureProcessId!.Value))
            {
                var cpu=process.TotalProcessorTime;await Task.Delay(1200);process.Refresh();
                Console.WriteLine($"RESOURCE recording-320x180-15fps: ffmpeg_cpu_ms={(process.TotalProcessorTime-cpu).TotalMilliseconds:0}; private_MiB={process.PrivateMemorySize64/1048576d:0.0}");
            }
            await engine.PauseAsync();
            Check(Field<ContinuousVideoSize>(engine,"_videoSize").ReadBytes()==ContinuousMedia.VideoFiles(engine.WorkingDirectory,Field<RecordingManifest>(engine,"_manifest").Segments[^1]).Sum(f=>new FileInfo(f).Length),"Incremental bytes match fully flushed real rolling capture files");
            await engine.ResumeAsync();await Task.Delay(1200);await engine.StopAsync();
            var hashes=await Hashes(engine.OutputFile);
            Check(hashes.Length==engine.FrameCount&&hashes.Distinct().Count()==1,"Bounded capture still rolls, pauses, resumes and preserves every recorded RGB frame");target.Close();
        }
        var brand=Field<Bitmap?>(main,"_brandImage");main.Close();
        if(validate)Check(brand is not null&&Field<Bitmap?>(main,"_brandImage") is null&&Field<Icon?>(main,"_ownedIcon") is null,"Closing the main form releases its owned logo and icon references");
    }
}
