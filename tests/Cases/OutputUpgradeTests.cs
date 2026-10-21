using System.Text.Json;
using System.Reflection;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static async Task TestLongSplitAsync()
    {
        var work=Screen.PrimaryScreen!.WorkingArea;
        using var target=new Form{Text="Five minute auto split owned fixture",StartPosition=FormStartPosition.Manual,
            Location=new(work.Left+30,work.Top+80),ClientSize=new(320,180),BackColor=Color.DarkGreen,ShowInTaskbar=false,TopMost=true};target.Show();
        var identity=WindowsInterop.DescribeWindow(new(target.Handle,target.Text,target.Bounds));
        string folder=Path.Combine(_root,"long-split");Directory.CreateDirectory(folder);
        new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,Scope=RecordingScope.Window,LastWindow=identity,
            Encoder=EncoderChoice.H264Cpu,Fps=15,Resolution=OutputResolution.Percent50,Container=OutputContainer.Mp4,
            SegmentMinutes=5,SegmentGigabytes=0,RecordSystemAudio=false,RecordMicrophone=false,Countdown=false,
            MinimizeWhenRecording=true,UseModernWindowCapture=false,UseDxgi=false}.Save();
        using var main=new MainForm{Opacity=0,ShowInTaskbar=false};main.Show();
        await Until(()=>!Field<bool>(main,"_busy")&&Field<Capabilities>(main,"_caps").Available);
        // The user picker correctly excludes our own process. Bind only this owned fixture directly.
        typeof(MainForm).GetField("_window",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,
            new WindowInfo(target.Handle,target.Text,WindowsInterop.GetWindowClientBounds(target.Handle)));
        Invoke(main,"PlanChanged");
        var engine=Field<RecorderEngine>(main,"_engine");var hud=Field<RecordingHud>(main,"_recordingHud");hud.Opacity=0;
        Check(!hud.Visible,"Long split fixture has no HUD before recording");
        try
        {
            await InvokeAsync(main,"StartRecordingAsync");
            Check(engine.State==RecorderState.Recording&&engine.CurrentPlan!.SegmentMinutes==5,"Real five-minute split starts through main UI");
            int? initialProcess=engine.CaptureProcessId;
            var clock=System.Diagnostics.Stopwatch.StartNew();int reports=0;
            while((engine.SegmentCount<2||Field<bool>(main,"_busy"))&&clock.Elapsed<TimeSpan.FromSeconds(330))
            {
                await Task.Delay(500);
                if(engine.State==RecorderState.Idle || engine.State==RecorderState.Paused)
                    throw new Exception("Long split interrupted before threshold. Own application log:\n"+Field<Label>(main,"_operationHint").Text);
                if(clock.Elapsed.TotalSeconds>=30*(reports+1)){reports++;Console.WriteLine($"RUNNING auto split: wall={clock.Elapsed.TotalSeconds:0}s recorded={engine.Duration.TotalSeconds:0}s frames={engine.FrameCount} segments={engine.SegmentCount}");}
            }
            Check(engine.SegmentCount==2&&engine.State==RecorderState.Recording&&!Field<bool>(main,"_busy"),"Actual 500ms main timer automatically rotates at the five-minute threshold");
            Check(engine.CaptureProcessId==initialProcess,"Five-minute automatic split keeps exactly the same capture process");
            Check(ContinuousMedia.VideoFiles(engine.WorkingDirectory,RecordingManifest.Load(engine.WorkingDirectory).Segments[0]).Length>=30,"Five-minute recording continuously writes closed protection chunks");
            Check(engine.Duration.TotalSeconds>=300&&engine.FrameCount>=4300,"Actual split preserves five minutes of time and frame totals");
            await Task.Delay(1000);await InvokeAsync(main,"StopRecordingAsync");
            Check(File.Exists(engine.OutputFile)&&engine.OutputFile.EndsWith(".mp4"),"Five-minute automatic segments save a single MP4");
            string json=await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,new[]{"-v","error","-show_entries","stream=width,height:format=duration","-of","json",engine.OutputFile});
            using var doc=JsonDocument.Parse(json);var video=doc.RootElement.GetProperty("streams")[0];
            double seconds=double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
            Check(video.GetProperty("width").GetInt32()==160&&video.GetProperty("height").GetInt32()==90&&seconds>=295,"Five-minute MP4 retains scaled dimensions and complete duration");
            await Run("-v","error","-i",engine.OutputFile,"-f","null","-");Check(true,"Entire five-minute result decodes successfully");
        }
        finally{if(engine.State is RecorderState.Recording or RecorderState.Paused)await InvokeAsync(main,"StopRecordingAsync");main.Close();}
    }
    private static async Task TestOutputUpgradeAsync()
    {
        string folder=Path.Combine(_root,"output-upgrade");Directory.CreateDirectory(folder);
        Check(OutputOptions.Size(new(2560,1440),OutputResolution.Hd720,true)==new Size(1280,720),"720p actually scales a 1440p source");
        Check(OutputOptions.Size(new(3840,2160),OutputResolution.FullHd1080,true)==new Size(1920,1080),"1080p caps a 4K source");
        Check(OutputOptions.Size(new(320,180),OutputResolution.Hd720,true)==new Size(320,180),"Output cap never enlarges small regions");
        Check(OutputOptions.Size(new(1080,1920),OutputResolution.Hd720,true)==new Size(406,720),"Portrait recording keeps aspect ratio within the output box");
        Check(OutputOptions.Size(new(321,181),OutputResolution.Percent50,true)==new Size(160,90),"Percentage scaling and even dimensions agree");
        Check(OutputOptions.Size(new(321,181),OutputResolution.Original,false)==new Size(321,181),"Original RGB dimensions remain unchanged");
        var normalized=new AppSettings{Resolution=(OutputResolution)99,Container=(OutputContainer)99,SegmentMinutes=-1,SegmentGigabytes=99};normalized.Normalize();
        Check(normalized.Resolution==OutputResolution.Original&&normalized.Container==OutputContainer.Mkv&&normalized.SegmentMinutes==20&&normalized.SegmentGigabytes==2,"Invalid output settings normalize safely");
        Check(!OutputOptions.SegmentDue(TimeSpan.FromMinutes(4.99),5,0,0,0)&&OutputOptions.SegmentDue(TimeSpan.FromMinutes(5),5,0,0,0),"Time split triggers only at its threshold");
        Check(OutputOptions.SegmentDue(TimeSpan.Zero,0,1073741824,1,0)&&!OutputOptions.SegmentDue(TimeSpan.FromDays(1),0,1073741823,1,0),"Video byte split threshold and disabled timer");
        Check(OutputOptions.SegmentDue(TimeSpan.Zero,0,0,0,512L*1024*1024),"Audio size safeguard remains active when optional splitting is off");
        Check(!OutputOptions.SegmentDue(TimeSpan.FromDays(1),0,0,0,0),"Disabled optional thresholds do not split by themselves");
        Check(StorageGuard.Evaluate(10000000000,1000000,3).Reserve==3000000+StorageGuard.Margin,"MP4 reserves room for the additional staged conversion");
        var performance=new RecordingPerformance();performance.Observe("fps=14.90");performance.Observe("speed=0.75x");performance.Observe("drop_frames=3");
        Check(performance.Snapshot.Fps==14.9&&performance.Snapshot.Slow&&performance.Snapshot.Dropped==3,"Performance uses actual FFmpeg FPS, speed and drop counters");
        performance.Observe("speed=N/A");performance.Observe("fps=NaN");performance.Observe("drop_frames=-1");
        Check(performance.Snapshot.Fps==14.9&&performance.Snapshot.Dropped==3,"Malformed progress never corrupts valid performance statistics");
        using(var hud=new RecordingHud())
        {
            hud.UpdateState(RecorderState.Recording,false,0,TimeSpan.FromSeconds(15),DateTime.Now,performance:performance.Snapshot,warn:true);
            Check(Field<Label>(hud,"_status").Text.Contains("编码偏慢")&&!Field<Label>(hud,"_status").Text.Contains("无损"),"Hidden-main HUD reports slow encoding without mislabelling compressed video as lossless");
        }
        performance.Reset();Check(!performance.Snapshot.HasSpeed&&!performance.Snapshot.Slow&&performance.Snapshot.Dropped==0,"New segment resets stale performance warnings");
        Check(HardwareProbe.ResolveEncoder(EncoderChoice.Auto,RecordingPurpose.Game,new Capabilities{IntelH264=true,H264Cpu=true},RecordingProfile.Balanced)==EncoderChoice.IntelH264,"Balanced preset uses verified Intel hardware");
        Check(HardwareProbe.ResolveEncoder(EncoderChoice.Auto,RecordingPurpose.Game,new Capabilities{AmdH264=true,H264Cpu=true},RecordingProfile.Balanced)==EncoderChoice.AmdH264,"Balanced preset uses verified AMD hardware");
        Check(VideoEncoding.IsHardware(EncoderChoice.IntelH264)&&VideoEncoding.IsHardware(EncoderChoice.AmdH264),"Intel and AMD use first-frame confirmation and hardware fallback");
        var basePlan=new RecordPlan(new(0,0,2560,1440),null,RecordingScope.Region,RecordingPurpose.Game,EncoderChoice.H264Cpu,CaptureBackend.Gdi,15,false,false,false,1,1);
        foreach(var resolution in Enum.GetValues<OutputResolution>())
        {
            var plan=basePlan with{Resolution=resolution};var capture=FfmpegArguments.Capture(plan,"unused.mkv");
            var args=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc=size=2560x1440:rate=15","-frames:v","3"};
            args.AddRange(capture.Skip(capture.IndexOf("-map")));string file=Path.Combine(folder,"scale-"+resolution+".mkv");args[^1]=file;await Run(args.ToArray());
            string info=await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,new[]{"-v","error","-show_entries","stream=width,height","-of","csv=p=0",file});
            Check(info.Contains($"{plan.OutputSize.Width},{plan.OutputSize.Height}"),resolution+": real encoded output has planned dimensions");
            Check((await Hashes(file)).Length==3,resolution+": all scaled frames decode");
        }
        foreach(var encoder in new[]{EncoderChoice.IntelH264,EncoderChoice.AmdH264})
        {
            var options=FfmpegArguments.OtherHardwareOptions(encoder,CompressionQuality.High);
            Check(options.Contains(encoder==EncoderChoice.IntelH264?"h264_qsv":"h264_amf")&&options.Contains("18"),encoder+": exact speed and quality encoding parameters");
            if(!VideoEncoding.Supported(encoder,_caps)){Console.WriteLine("SKIP real hardware: "+encoder+" unavailable");continue;}
            var args=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc=size=320x180:rate=15","-frames:v","3"};args.AddRange(options);
            string file=Path.Combine(folder,encoder+".mkv");args.Add(file);await Run(args.ToArray());Check((await Hashes(file)).Length==3,encoder+": available hardware produces decodable frames");
        }
        bool incompatible=false;
        try{FfmpegArguments.Capture(basePlan with{Encoder=EncoderChoice.Ffv1,Container=OutputContainer.Mp4},"unused.mkv");}catch(InvalidOperationException){incompatible=true;}
        Check(incompatible,"FFV1 MP4 is rejected before opening capture or creating materials");
        string directory=Path.Combine(folder,".screenlossless-work","mp4-fixture");Directory.CreateDirectory(directory);
        string source=Path.Combine(directory,"video-001.mkv"),audio=Path.Combine(directory,"audio-system-001.wav");
        await Run("-v","error","-y","-f","lavfi","-i","testsrc=size=128x72:rate=15","-t","1","-c:v","libx264","-preset","ultrafast","-crf","23",source);
        await Run("-v","error","-y","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","1",audio);
        File.Copy(source,Path.Combine(directory,"video-002.mkv"));File.Copy(audio,Path.Combine(directory,"audio-system-002.wav"));
        var manifest=new RecordingManifest{TargetFile="audio-export.mp4",SystemAudio=true,Microphone=true,MixAudio=false,
            Segments=new(){new("video-001.mkv","audio-system-001.wav",null),new("video-002.mkv","audio-system-002.wav",null)}};manifest.Save(directory);
        File.WriteAllText(Path.Combine(folder,"audio-export.mp4"),"existing user file");
        double lastProgress=0;
        string exported=await RecordingFinalizer.ExportAsync(_caps,directory,manifest,Console.WriteLine,p=>{Check(p>=lastProgress,"MP4 saving progress never goes backwards");lastProgress=p;});
        Check(Path.GetFileName(exported)=="audio-export_2.mp4"&&File.ReadAllText(Path.Combine(folder,"audio-export.mp4"))=="existing user file","MP4 collision preserves the original and its extension");
        Check(lastProgress==1&&manifest.CompletedFile=="audio-export_2.mp4","MP4 completion is committed only after export");
        string json=await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,new[]{"-v","error","-show_entries","stream=codec_name,codec_type:format=duration","-of","json",exported});
        using(var doc=JsonDocument.Parse(json))
        {
            var streams=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Check(streams.Count(s=>s.GetProperty("codec_name").GetString()=="aac")==2,"MP4 exports separate AAC tracks including a silent missing microphone");
            Check(streams.Any(s=>s.GetProperty("codec_name").GetString()=="h264"),"MP4 retains the video codec");
        }
        var sourceHashes=await Hashes(source);var outputHashes=await Hashes(exported);
        Check(outputHashes.SequenceEqual(sourceHashes.Concat(sourceHashes)),"MP4 segment merge copies video without changing decoded pixels");
        var cancelled=new RecordingManifest{TargetFile="cancel-export.mp4",Segments=new(){new("video-001.mkv",null,null)}};
        using(var cancel=new CancellationTokenSource())
        {
            bool caught=false;try{await RecordingFinalizer.ExportAsync(_caps,directory,cancelled,_=>{},p=>{if(p>=.9)cancel.Cancel();},cancel.Token);}catch(OperationCanceledException){caught=true;}
            Check(caught&&!File.Exists(Path.Combine(folder,"cancel-export.mp4"))&&File.Exists(source)&&cancelled.CompletedFile is null,"Cancelled MP4 export retains source materials and publishes no partial file");
        }
        var work=Screen.PrimaryScreen!.WorkingArea;
        using var target=new Form{Text="Output upgrade owned capture",StartPosition=FormStartPosition.Manual,Location=new(work.Left+30,work.Top+80),ClientSize=new(320,180),BackColor=Color.DarkGreen,ShowInTaskbar=false,TopMost=true};target.Show();
        var bounds=WindowsInterop.GetWindowClientBounds(target.Handle);
        var recording=basePlan with{Bounds=bounds,Scope=RecordingScope.Window,WindowHandle=target.Handle.ToInt64(),Resolution=OutputResolution.Percent50,Container=OutputContainer.Mp4};
        using(var engine=new RecorderEngine())
        {
            await engine.StartAsync(recording,_caps,folder,true);await Task.Delay(700);
            long firstFrames=engine.FrameCount;await engine.RotateSegmentAsync();
            Check(engine.State==RecorderState.Recording&&engine.SegmentCount==2&&engine.FrameCount>=firstFrames,"Real automatic rotation retains frame totals and keeps recording");
            await Task.Delay(500);await engine.PauseAsync();TimeSpan paused=engine.Duration;await Task.Delay(250);
            Check(engine.Duration==paused&&!engine.SegmentDue,"Paused sessions do not accumulate time or automatically split");
            await engine.ResumeAsync();await Task.Delay(500);Check(engine.SegmentCount==3,"Pause and resume coexist with automatic segments");
            string file=await engine.StopAsync();
            string info=await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,new[]{"-v","error","-show_entries","stream=width,height","-of","csv=p=0",file});
            Check(file.EndsWith(".mp4")&&info.Contains("160,90")&&(await Hashes(file)).Length>firstFrames,"Real scaled multi-segment recording saves a decodable MP4");
        }
        foreach(var encoder in new[]{EncoderChoice.IntelH264,EncoderChoice.AmdH264}.Where(e=>!VideoEncoding.Supported(e,_caps)))
        {
            using var engine=new RecorderEngine();await engine.StartAsync(recording with{Encoder=encoder},_caps,folder,true);
            Check(engine.CurrentPlan!.Encoder==EncoderChoice.H264Cpu&&engine.CurrentPlan.Resolution==OutputResolution.Percent50&&engine.CurrentPlan.Container==OutputContainer.Mp4,encoder+": failed hardware falls back to CPU and retains output settings");
            await engine.StopAsync();
        }
        new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,Scope=RecordingScope.Region,LastRegion=RegionSelection.FromRectangle(bounds),Encoder=EncoderChoice.H264Cpu,RecordSystemAudio=false,RecordMicrophone=false,Countdown=false,MinimizeWhenRecording=false}.Save();
        using(var main=new MainForm())
        {
            main.Show();await Until(()=>!Field<bool>(main,"_busy")&&Field<Capabilities>(main,"_caps").Available);
            var settings=Field<AppSettings>(main,"_settings");Field<ChoiceBox>(main,"_profileBox").SelectedIndex=(int)RecordingProfile.Low;
            Check(settings.Resolution==OutputResolution.Hd720&&Field<ChoiceBox>(main,"_resolutionBox").SelectedIndex==1,"Low preset applies real 720p output");
            Field<ChoiceBox>(main,"_resolutionBox").SelectedIndex=(int)OutputResolution.Percent50;
            Check(settings.Profile==RecordingProfile.Custom&&settings.Encoder==EncoderChoice.Mpeg4Cpu,"Manual resolution keeps the CPU codec and switches to custom");
            Field<ChoiceBox>(main,"_containerBox").SelectedIndex=1;Field<ChoiceBox>(main,"_segmentMinutesBox").SelectedIndex=1;Field<ChoiceBox>(main,"_segmentSizeBox").SelectedIndex=1;
            var saved=AppSettings.Load();Check(saved.Resolution==OutputResolution.Percent50&&saved.Container==OutputContainer.Mp4&&saved.SegmentMinutes==5&&saved.SegmentGigabytes==1,"All output controls persist across reopen");
            var tabs=Field<List<SoftButton>>(main,"_settingsTabs");tabs[3].PerformClick();await Task.Delay(70);
            CheckButtonsFit(main);using(var bitmap=new Bitmap(main.Width,main.Height)){main.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(_root,"output-page.png"));}
            await InvokeAsync(main,"StartRecordingAsync");var engine=Field<RecorderEngine>(main,"_engine");
            Check(engine.State==RecorderState.Recording&&!Field<ChoiceBox>(main,"_containerBox").Enabled&&!Field<ChoiceBox>(main,"_resolutionBox").Enabled,"Recording locks output controls");
            await InvokeAsync(main,"RotateSegmentAsync");
            Check(engine.State==RecorderState.Recording&&engine.SegmentCount==2&&!Field<bool>(main,"_busy"),"Main timer rotation route clears busy state and keeps the HUD session");
            await InvokeAsync(main,"StopRecordingAsync");
            settings.Scope=RecordingScope.Window;
            typeof(MainForm).GetField("_window",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,
                new WindowInfo(target.Handle,target.Text,WindowsInterop.GetWindowClientBounds(target.Handle)));
            Invoke(main,"PlanChanged");
            await InvokeAsync(main,"StartRecordingAsync");
            target.ClientSize=new Size(340,180);
            await Until(()=>engine.State==RecorderState.Idle&&!Field<bool>(main,"_busy"));
            Check(File.Exists(engine.OutputFile)&&engine.LastStopReason.Contains("窗口尺寸发生变化"),
                "Safety stop publishes existing video and retains the current stop reason");
            main.Close();
        }
    }
}
