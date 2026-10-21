using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
    private static async Task TestPlatformAsync()
    {
        string folder = Path.Combine(_root, "platform"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "devices.txt"), HardwareDiagnostics.Report(_caps));
        Check(_caps.HardwareTests.Count == 6 && _caps.HardwareTests.All(t => t.Detail.Length > 0), "All six hardware modes retain an actual trial result or diagnostic reason");
        var missing = await HardwareProbe.ProbeHardwareAsync(_caps.Ffmpeg, "", EncoderChoice.AmdH264);
        Check(!missing.Listed && !missing.Available, "A build without an encoder is distinct from a hardware/driver failure");
        using(var watcher = new AudioEndpointWatcher(_ => { })) Check(true, "Real Windows audio endpoint notification registration and disposal succeeds");
        Check(AudioEndpointWatcher.Affects(new("B", DataFlow.Render, Role.Multimedia, true), "", "A", DataFlow.Render), "Default output change invalidates a following loopback stream");
        Check(!AudioEndpointWatcher.Affects(new("B", DataFlow.Render, Role.Multimedia, true), "A", "A", DataFlow.Render), "Pinned audio selection never switches to an unrelated default device");
        Check(!AudioEndpointWatcher.Affects(new("B", DataFlow.Capture, Role.Multimedia, true), "", "A", DataFlow.Render), "Default microphone change never reopens the loopback stream");
        Check(!AudioEndpointWatcher.Affects(new("B", DataFlow.Render, Role.Communications, true), "", "A", DataFlow.Render), "Communications-role changes leave the recording role unchanged");
        Check(AudioEndpointWatcher.Affects(new("A"), "A", "A", DataFlow.Render) && !AudioEndpointWatcher.Affects(new("B"), "A", "A", DataFlow.Render), "Only the current endpoint's removal/state/format event invalidates the stream");
        Check(!AudioEndpointWatcher.Affects(new("A", DataFlow.Render, Role.Multimedia, true), "", "A", DataFlow.Render), "Duplicate default notification does not reopen a healthy stream");
        foreach (double ppm in new[] { -500d, -200d, 0d, 200d, 500d })
        {
            long written = 0, corrected = 0, silence = 0; double sourceFrames = 0, lastFrames = 0, maximum = 0;
            for(int i = 1; i <= 1080000; i++) // three simulated hours in 10 ms packets
            {
                sourceFrames += 480 * (1 + ppm / 1000000);
                int frames = (int)Math.Floor(sourceFrames - lastFrames); lastFrames += frames;
                double jitter = .004 * Math.Sin(i * .03);
                var plan = AudioClockCorrection.Plan(frames, written, TimeSpan.FromSeconds(i * .01 + jitter));
                written += plan.SilenceFrames + plan.OutputFrames;
                corrected += plan.OutputFrames - frames; silence += plan.SilenceFrames;
                maximum = Math.Max(maximum, Math.Abs(written / 48000d - i * .01));
            }
            Check(maximum < .03 && Math.Abs(written / 48000d - 10800) < .03, $"Three simulated hours at {ppm:+0;-0;0} ppm stay within 30 ms of the master clock");
            Check(silence == 0 && (ppm == 0 || corrected != 0), $"Clock drift {ppm:+0;-0;0} ppm is corrected by resampling without false unplug gaps");
        }
        var gap = AudioClockCorrection.Plan(480, 48000, TimeSpan.FromSeconds(11.01));
        Check(gap.SilenceFrames == 480000, "A ten-second unplug gap retains its exact place on the master timeline");
        float[] ramp = { 0, 1, .5f, .5f, 1, 0 }, expanded = new float[10];
        AudioClockCorrection.ResampleStereo(ramp, 3, expanded, 5);
        Check(expanded.SequenceEqual(new float[] {0,1,.25f,.75f,.5f,.5f,.75f,.25f,1,0}), "Gradual correction interpolates both channels without crossing them");
        // Exercise the writer, not just clock arithmetic: +0.2% device sample-clock drift.
        TimeSpan now = TimeSpan.Zero; FakeAudioSource? fake = null; string driftFile = Path.Combine(folder, "drift.wav");
        using(var track = new RecoveringAudioTrack(driftFile, () => fake = new FakeAudioSource(RecoveringAudioTrack.TimelineFormat), _ => {}, elapsed: () => now))
        {
            await track.RetryAsync();
            byte[] packet = new byte[4810 * 8];
            for(int i=0;i<600;i++){now += TimeSpan.FromMilliseconds(100);fake!.Push(packet);}
            await track.StopAsync();
        }
        using(var audio = new WaveFileReader(driftFile)) Check(Math.Abs(audio.TotalTime.TotalSeconds - 60) < .03, "Real normalized WAV writer corrects a fast clock over 60 accelerated seconds");
        // Record and independently decode every hardware segment at a useful resolution.
        foreach(var hardware in _caps.HardwareTests)
        {
            if(!hardware.Available){Console.WriteLine("SKIP hardware not available: " + hardware.Encoder + " · " + hardware.Detail);continue;}
            string directory=Path.Combine(folder,hardware.Encoder.ToString());Directory.CreateDirectory(directory);
            var plan=new RecordPlan(new(0,0,2560,1440),null,RecordingScope.Region,RecordingPurpose.Game,hardware.Encoder,CaptureBackend.Gdi,30,false,false,false,1,1);
            var capture=ContinuousMedia.Capture(plan,Path.Combine(directory,"video-001.mkv"),1);
            var args=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc2=size=2560x1440:rate=30","-frames:v","90"};
            args.AddRange(capture.Skip(capture.IndexOf("-map")));
            await HardwareProbe.ExecuteTextAsync(_caps.Ffmpeg,args,60000);
            var files=ContinuousMedia.VideoFiles(directory,new("video-001.mkv",null,null,true));
            Check(files.Length==3,hardware.Encoder+": actual 1440p/30 hardware encoding creates three protection files");
            foreach(string file in files)
            {
                await HardwareProbe.ExecuteTextAsync(_caps.Ffmpeg,new[]{"-v","error","-xerror","-err_detect","explode","-i",file,"-f","null","-"},60000);
                if (hardware.Encoder == EncoderChoice.NvencHevcLossless)
                    await HevcLossless.VerifyFormatAsync(_caps.Ffprobe, file, 2560, 1440);
            }
            Check(true,hardware.Encoder+": every real 1440p hardware file independently decodes without errors");
        }
        foreach(var monitor in WindowsInterop.GetMonitors())
        {
            Check(monitor.DpiX>=96 && monitor.DpiY>=96, "Connected monitor reports effective DPI: " + monitor.Name);
            Console.WriteLine($"PHYSICAL MONITOR: {monitor.Name} {monitor.Bounds} DPI={monitor.DpiX}x{monitor.DpiY}");
        }
        var desktop=SystemInformation.VirtualScreen;
        using(var indicator=new RegionIndicator())
        {
            var bounds=new Rectangle(desktop.Left+80,desktop.Top+80,320,180);indicator.SetEditable(true);indicator.SetSelection(bounds,RecorderState.Idle);indicator.Show();
            foreach(int dpi in new[]{144,192,120,96})
            {
                var suggested=new Rectangle(100,100,900,600);IntPtr native=Marshal.AllocHGlobal(16);
                try
                {
                    Marshal.WriteInt32(native,0,suggested.Left);Marshal.WriteInt32(native,4,suggested.Top);Marshal.WriteInt32(native,8,suggested.Right);Marshal.WriteInt32(native,12,suggested.Bottom);
                    SendMessage(indicator.Handle,0x2E0,new IntPtr(dpi | dpi << 16),native);
                    Check(indicator.Selection==bounds && indicator.Bounds==Rectangle.Inflate(bounds,indicator.Inset,indicator.Inset), $"Simulated {dpi} DPI message preserves physical region bounds and hollow mask");
                }
                finally{Marshal.FreeHGlobal(native);}
            }
            indicator.Hide();
        }
        using(var indicator=new RegionIndicator())
        {
            indicator.SetEditable(true);
            foreach(var monitor in WindowsInterop.GetMonitors())
            {
                var bounds = new Rectangle(monitor.Bounds.Left + 100, monitor.Bounds.Top + 100, 320, 180);
                indicator.SetSelection(bounds, RecorderState.Idle); indicator.Show(); await Task.Delay(100);
                Check(indicator.Selection == bounds && indicator.PointToScreen(new Point(indicator.Inset, indicator.Inset)) == bounds.Location,
                    "Actual native dashed border retains physical coordinates on " + monitor.Name);
                Check(!indicator.Region!.IsVisible(indicator.Inset+160, indicator.Inset+90), "Actual cross-DPI selection interior stays hollow and click-through");
            }
            indicator.Hide();
        }
        using(var diagnostic=new HardwareDiagnosticsForm(_caps))
        {
            diagnostic.Show();await Task.Delay(100);CheckButtonsFit(diagnostic);Snapshot(diagnostic,"device-diagnostics");diagnostic.Close();
        }
        // Native transitions across the connected displays exercise real per-monitor DPI.
        new AppSettings { FfmpegPath = _caps.Ffmpeg, OutputFolder = folder, RecordSystemAudio = false, RecordMicrophone = false }.Save();
        using(var main = new MainForm())
        using(var hud = new RecordingHud())
        {
            main.Show(); await Until(() => Field<Capabilities>(main, "_caps").Available && !Field<bool>(main, "_busy"));
            hud.Show();
            foreach(var monitor in WindowsInterop.GetMonitors().Concat(WindowsInterop.GetMonitors().AsEnumerable().Reverse()))
            {
                var work = Screen.AllScreens.First(s => s.DeviceName == monitor.Name).WorkingArea;
                main.Location = work.Location + new Size(30, 30); await Task.Delay(250);
                Check(main.DeviceDpi == monitor.DpiX, $"Actual main window adopts {monitor.DpiX} DPI on {monitor.Name}");
                Check(Descendants(main).OfType<TableLayoutPanel>().SelectMany(table => table.ColumnStyles.Cast<ColumnStyle>())
                    .Any(style => style.SizeType == SizeType.Absolute && Math.Abs(style.Width - 490 * main.DeviceDpi / 96f) < 1),
                    "Actual DPI transitions retain the header's design width instead of repeatedly shrinking it");
                foreach(var tab in Field<List<SoftButton>>(main, "_settingsTabs")) { tab.PerformClick(); main.PerformLayout(); CheckButtonsFit(main); }
                Check(work.Contains(main.Bounds), "Actual cross-display main window remains inside the destination work area");
                hud.Location = work.Location + new Size(80, 80); await Task.Delay(100);
                Check(hud.DeviceDpi == monitor.DpiX && work.Contains(hud.Bounds), "Actual floating controls adopt destination DPI and remain on screen");
                Snapshot(hud, "native-dpi-hud-beforecheck-" + monitor.DpiX);
                CheckButtonsFit(hud);
                Snapshot(main, "native-dpi-main-" + monitor.DpiX);
                Snapshot(hud, "native-dpi-hud-" + monitor.DpiX);
            }
            hud.Close(); main.Close();
        }
        // Short real high-resolution capture + actual WASAPI endpoint, explicitly distinct
        // from the three-hour arithmetic simulation and manual physical unplug testing.
        int seconds=60;
        if(int.TryParse(Environment.GetEnvironmentVariable("SCREENLOSSLESS_TEST_SECONDS"),out int configured)) seconds=Math.Clamp(configured,5,21600);
        var screen=Screen.PrimaryScreen!;
        using var fixture=new Form{Text="ScreenLossless owned endurance fixture",FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=screen.Bounds,BackColor=Color.DarkGreen,ShowInTaskbar=false};
        fixture.Show();
        using var engine=new RecorderEngine();
        var encoder=_caps.NvencH264?EncoderChoice.NvencH264:EncoderChoice.H264Cpu;
        var livePlan=new RecordPlan(WindowsInterop.GetWindowClientBounds(fixture.Handle),null,RecordingScope.Window,RecordingPurpose.Game,encoder,CaptureBackend.Gdi,30,false,true,false,1,1,WindowHandle:fixture.Handle.ToInt64(),SegmentMinutes:0,SegmentGigabytes:0);
        var logs=new List<string>();engine.Message+=m=>{lock(logs)logs.Add(m);};
        await engine.StartAsync(livePlan,_caps,folder,true);
        bool realAudio=false;
        for(int i=0;i<seconds;i++)
        {
            await Task.Delay(1000);fixture.BackColor=i%2==0?Color.DarkGreen:Color.DarkBlue;
            if(engine.UnexpectedExit) throw new Exception("High-resolution capture unexpectedly exited: " + engine.ExitDetails);
            realAudio|=engine.SystemAudioAvailable;
            if(i%30==29 && StorageGuard.AvailableBytes(folder) is long available && StorageGuard.Evaluate(available, StorageGuard.WorkspaceBytes(engine.WorkingDirectory), 3).Stop)
                throw new IOException("长测空间不足，已中断并保留素材。");
            if(i%30==29)Console.WriteLine($"ENDURANCE: {engine.Duration} · {engine.FrameCount} frames · {engine.Performance.Text}");
        }
        Check(engine.State==RecorderState.Recording,"Actual high-resolution capture remains alive through the requested duration");
        string output=await engine.StopAsync();fixture.Hide();
        var json=await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,new[]{"-v","error","-show_entries","stream=codec_type,width,height,duration:format=duration","-of","json",output});
        using(var document=JsonDocument.Parse(json))
        {
            var streams=document.RootElement.GetProperty("streams").EnumerateArray().ToArray();var video=streams.First(s=>s.GetProperty("codec_type").GetString()=="video");
            Check(video.GetProperty("width").GetInt32()==livePlan.OutputSize.Width && video.GetProperty("height").GetInt32()==livePlan.OutputSize.Height,"Actual capture preserves selected physical-pixel dimensions");
            Check(streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio"),"Actual WASAPI session exports a fixed audio track even when idle/silent");
            double duration=double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture);
            Check(duration>=seconds-1,"Actual high-resolution session retains complete duration");
        }
        await HardwareProbe.ExecuteTextAsync(_caps.Ffmpeg,new[]{"-v","error","-xerror","-err_detect","explode","-i",output,"-f","null","-"},Math.Max(60000,seconds*2000));
        Check(true,"Entire actual high-resolution audio/video output decodes without errors");
        string summary; lock(logs) summary = logs.Last(m => m.Contains("音频时间线结束"));
        var match = System.Text.RegularExpressions.Regex.Match(summary, @"结束：(\d+(?:[.,]\d+)?) 秒");
        double sourceAudioSeconds = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        Check(Math.Abs(sourceAudioSeconds-engine.Duration.TotalSeconds)<.03, "Source audio duration matches the stopped master clock before muxing (30 ms tolerance)");
        Console.WriteLine("ACTUAL WASAPI ENDPOINT CONNECTED: "+realAudio+"; real capture seconds="+seconds+"; this is not a physical unplug or multi-hour AV-sync certification.");
        Check(!File.Exists(Path.ChangeExtension(output,".log")),"Recording does not create a diagnostic sidecar");
    }
}
