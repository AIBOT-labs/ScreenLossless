using System.Reflection;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static async Task TestEncodingProfilesAsync()
    {
        var cpuCaps = new Capabilities { Available=true, Ffmpeg=_caps.Ffmpeg, Ffprobe=_caps.Ffprobe,
            Nvenc=false, NvencListed=true, H264Cpu=true, Mpeg4Cpu=true, X264Rgb=true, Ffv1=true, Dxgi=true, GfxCapture=true };
        Check(!cpuCaps.Nvenc && cpuCaps.NvencListed, "Compiled NVENC and usable hardware are distinct capabilities");
        Check(!_caps.Nvenc || _caps.NvencListed, "Usable NVENC is confirmed by a real lossless encode");
        Check(!_caps.NvencListed || _caps.Nvenc || _caps.NvencError.Length>0, "Unavailable NVENC keeps a diagnostic reason");
        var broken=new AppSettings{Profile=(RecordingProfile)999,Quality=(CompressionQuality)999,Fps=15}; broken.Normalize();
        Check(broken.Quality==CompressionQuality.Balanced,"Invalid compression quality normalizes");
        Check(broken.Profile==RecordingProfile.Custom && broken.Fps==15, "Invalid profile normalizes and 15 FPS is retained");
        broken.Fps=24;broken.Normalize();Check(broken.Fps==24,"24 FPS custom option is retained");
        var legacy=new AppSettings{Encoder=EncoderChoice.Ffv1,Fps=120,UseDxgi=false};
        RecordingProfiles.Apply(legacy,RecordingProfile.Custom);
        Check(legacy.Encoder==EncoderChoice.Ffv1 && legacy.Fps==120 && !legacy.UseDxgi, "Custom profile preserves existing encoding settings");
        var gpuCaps=new Capabilities{Available=true,Nvenc=true,NvencH264=true,X264Rgb=true,H264Cpu=true,Mpeg4Cpu=true,Ffv1=true};
        foreach(var profile in new[]{RecordingProfile.High,RecordingProfile.Balanced,RecordingProfile.Low,RecordingProfile.Cpu})
        {
            var settings=new AppSettings();RecordingProfiles.Apply(settings,profile);
            Check(settings.Fps==(profile==RecordingProfile.High?60:profile==RecordingProfile.Low?15:30),
                profile+": preset sets the promised frame rate");
            var encoder=HardwareProbe.ResolveEncoder(settings.Encoder,RecordingPurpose.Game,cpuCaps,profile);
            var expected = profile == RecordingProfile.High ? EncoderChoice.X264Rgb : profile == RecordingProfile.Low ? EncoderChoice.Mpeg4Cpu : EncoderChoice.H264Cpu;
            Check(encoder==expected,profile+": without usable NVENC chooses the intended CPU codec");
            var plan=new RecordPlan(new Rectangle(0,0,128,72),null,RecordingScope.Region,RecordingPurpose.Game,
                encoder,CaptureBackend.Gdi,settings.Fps,false,false,false,1,1,Profile:profile,Quality:settings.Quality);
            var capture=FfmpegArguments.Capture(plan,"unused.mkv");
            Check(!capture.Contains("h264_nvenc") && capture.Contains(profile == RecordingProfile.High ? "libx264rgb" : profile == RecordingProfile.Low ? "mpeg4" : "libx264"),
                profile+": uses a real CPU encoder without GPU");
            Check(plan.IsLossless == (profile == RecordingProfile.High),
                profile+": lossless and compressed modes are labelled honestly");
            if(profile==RecordingProfile.Low)
                Check(capture.Contains("-threads") && int.Parse(capture[capture.IndexOf("-threads")+1]) is >=1 and <=4,
                    "Low profile caps encoder threads");
            var encode=new List<string>{"-v","error","-y","-f","lavfi","-i",$"testsrc=size=128x72:rate={settings.Fps}","-frames:v","3"};
            encode.AddRange(capture.Skip(capture.IndexOf("-map")));
            string file=Path.Combine(_root,"profile-"+profile+".mkv");encode[^1]=file;
            await Run(encode.ToArray());
            string raw=await Run("-v","error","-f","lavfi","-i",$"testsrc=size=128x72:rate={settings.Fps}",
                "-frames:v","3","-pix_fmt","rgb24","-f","framemd5","-");
            var reference=raw.Split('\n').Where(line=>System.Text.RegularExpressions.Regex.IsMatch(line,@"^\d+,")).Select(line=>line.Split(',')[^1].Trim());
            var decoded = await Hashes(file);
            Check(decoded.Length==3,profile+": output contains all three decodable frames");
            if(plan.IsLossless)Check(decoded.SequenceEqual(reference),profile+": lossless output preserves every source pixel");
        }
        Check(HardwareProbe.ResolveEncoder(EncoderChoice.Auto,RecordingPurpose.Game,gpuCaps,RecordingProfile.High)==EncoderChoice.Nvenc,
            "High profile uses verified hardware when available");
        Check(HardwareProbe.ResolveEncoder(EncoderChoice.Nvenc,RecordingPurpose.Game,gpuCaps,RecordingProfile.Cpu)==EncoderChoice.H264Cpu,
            "CPU profile cannot accidentally use hardware encoding");
        var ffvCaps=new Capabilities{Available=true,Nvenc=true,Ffv1=true};
        Check(HardwareProbe.ResolveEncoder(EncoderChoice.Auto,RecordingPurpose.Game,ffvCaps,RecordingProfile.Low)==EncoderChoice.Ffv1,
            "CPU preset falls back to FFV1 when x264rgb is missing");
        Check(HardwareProbe.ResolveEncoder(EncoderChoice.Auto,RecordingPurpose.Game,new Capabilities{Nvenc=true},RecordingProfile.Cpu)==EncoderChoice.Auto,
            "CPU preset does not silently use GPU when CPU encoders are missing");
        foreach(var encoder in new[]{EncoderChoice.H264Cpu,EncoderChoice.Mpeg4Cpu,EncoderChoice.NvencH264,EncoderChoice.NvencHevc})
        {
            foreach(var quality in Enum.GetValues<CompressionQuality>())
            {
                var plan=new RecordPlan(new Rectangle(0,0,321,181),null,RecordingScope.Region,RecordingPurpose.Game,
                    encoder,CaptureBackend.Gdi,15,false,false,false,1,1,Quality:quality);
                var capture=FfmpegArguments.Capture(plan,"unused.mkv");
                string qOption=encoder==EncoderChoice.H264Cpu?"-crf":encoder==EncoderChoice.Mpeg4Cpu?"-q:v":"-qp";
                int expected=encoder==EncoderChoice.Mpeg4Cpu?VideoEncoding.MpegQuantizer(quality):VideoEncoding.Quantizer(quality);
                Check(capture[capture.IndexOf(qOption)+1]==expected.ToString(),encoder+": "+quality+" changes actual encoder quantizer");
                if(!VideoEncoding.Supported(encoder,_caps)){Console.WriteLine("SKIP real encode: "+encoder+" hardware unavailable");continue;}
                var encode=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc=size=321x181:rate=15","-frames:v","3"};
                encode.AddRange(capture.Skip(capture.IndexOf("-map")));
                string file=Path.Combine(_root,"codec-"+encoder+"-"+quality+".mkv");encode[^1]=file;await Run(encode.ToArray());
                string codec=encoder==EncoderChoice.Mpeg4Cpu?"mpeg4":encoder==EncoderChoice.NvencHevc?"hevc":"h264";
                string info=await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,new[]{"-v","error","-show_entries","stream=codec_name,width,height,pix_fmt","-of","csv=p=0",file});
                Check(info.Contains(codec+",322,182,yuv420p"),encoder+": "+quality+" emits expected real codec and padded size");
                Check((await Hashes(file)).Length==3,encoder+": "+quality+" output decodes completely");
            }
        }
        var stillLossless=new RecordPlan(new Rectangle(0,0,129,73),null,RecordingScope.Region,RecordingPurpose.Desktop,
            EncoderChoice.X264Rgb,CaptureBackend.Gdi,15,false,false,false,1,1,Quality:CompressionQuality.Small);
        var oldArgs=FfmpegArguments.Capture(stillLossless,"lossless.mkv");
        Check(oldArgs[oldArgs.IndexOf("-crf")+1]=="0" && !oldArgs.Contains("-vf"),
            "Compressed quality settings never alter old RGB lossless encoding or odd source dimensions");
        Check(HardwareProbe.ResolveEncoder(EncoderChoice.Auto,RecordingPurpose.Game,new Capabilities{H264Cpu=true})==EncoderChoice.Auto,
            "Custom automatic lossless mode never silently falls back to lossy compression");
        new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=_root,Scope=RecordingScope.Region,
            LastRegion=RegionSelection.FromRectangle(new Rectangle(Screen.PrimaryScreen!.Bounds.Left+80,Screen.PrimaryScreen.Bounds.Top+240,320,180)),
            RecordSystemAudio=false,RecordMicrophone=false,Countdown=false,Encoder=EncoderChoice.Ffv1,Fps=24}.Save();
        using(var form=new MainForm())
        {
            form.Show();await Until(()=>Field<Capabilities>(form,"_caps").Available&&!Field<bool>(form,"_detecting"));
            typeof(MainForm).GetField("_caps",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(form,cpuCaps);
            Invoke(form,"UpdateEncoderChoices");
            Check(Field<ChoiceBox>(form,"_codecBox").Items.OfType<EncoderOption>().Single(item=>item.Encoder==EncoderChoice.NvencH264).ToString().Contains("不可用"),
                "Unusable GPU encoders are marked directly in the dropdown");
            var profiles=Field<ChoiceBox>(form,"_profileBox");var fps=Field<ChoiceBox>(form,"_fpsBox");
            var codecs=Field<ChoiceBox>(form,"_codecBox");var settings=Field<AppSettings>(form,"_settings");
            Check(profiles.SelectedIndex==0 && settings.Fps==24 && settings.Encoder==EncoderChoice.Ffv1,
                "Existing custom frame rate and codec survive upgrade");
            profiles.SelectedIndex=(int)RecordingProfile.High;
            Check(settings.Fps==60 && settings.Profile==RecordingProfile.High,"Profile dropdown applies high settings");
            fps.SelectedIndex=4;
            Check(settings.Profile==RecordingProfile.Custom && profiles.SelectedIndex==0 && settings.Fps==15,
                "Manual FPS edit switches to custom and preserves the edit");
            profiles.SelectedIndex=(int)RecordingProfile.Low;
            Check(!Field<CheckBox>(form,"_dxgi").Checked && !Field<CheckBox>(form,"_modernWindow").Checked,
                "Low preset disables GPU-dependent capture paths");
            profiles.SelectedIndex=(int)RecordingProfile.Cpu;
            var plan=(RecordPlan)form.GetType().GetMethod("GetPlan",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,null)!;
            Check(plan.Encoder==EncoderChoice.H264Cpu && plan.Backend==CaptureBackend.Gdi && plan.Fps==30,
                "Actual no-GPU UI plan uses CPU encoder and compatible capture");
            Check(AppSettings.Load().Profile==RecordingProfile.Cpu,"Preset selection is persisted");
            Field<ChoiceBox>(form,"_qualityBox").SelectedIndex=(int)CompressionQuality.High;
            Check(settings.Profile==RecordingProfile.Custom && settings.Encoder==EncoderChoice.H264Cpu && settings.Quality==CompressionQuality.High,
                "Manual quality edit preserves resolved CPU codec instead of switching to lossless auto");
            profiles.SelectedIndex=(int)RecordingProfile.Cpu;
            fps.SelectedIndex=5;
            Check(settings.Encoder==EncoderChoice.H264Cpu && settings.Fps==24 && settings.Profile==RecordingProfile.Custom,
                "Manual FPS edit preserves CPU encoder after leaving preset");
            profiles.SelectedIndex=(int)RecordingProfile.Cpu;
            Snapshot(form,"profiles-cpu-main");
            profiles.DroppedDown=true;await Task.Delay(40);
            Snapshot(Field<ToolStripDropDown>(profiles,"_popup"),"profiles-dropdown");
            profiles.DroppedDown=false;
            codecs.DroppedDown=true;await Task.Delay(40);
            Snapshot(Field<ToolStripDropDown>(codecs,"_popup"),"encoders-dropdown");
            codecs.DroppedDown=false;
            await InvokeAsync(form,"StartRecordingAsync");
            var engine=Field<RecorderEngine>(form,"_engine");
            try
            {
                Check(engine.State==RecorderState.Recording && engine.CurrentPlan?.Encoder==EncoderChoice.H264Cpu,
                    "No-GPU preset actually starts a CPU recording");
                Check(!profiles.Enabled && !fps.Enabled && !codecs.Enabled,"Preset and codec stay locked during recording");
                await Task.Delay(400);await InvokeAsync(form,"TogglePauseAsync");
                Check(engine.State==RecorderState.Paused,"CPU preset pauses successfully");
                await InvokeAsync(form,"TogglePauseAsync");await Task.Delay(400);
                await InvokeAsync(form,"StopRecordingAsync");
                Check((await Hashes(engine.OutputFile)).LongLength==engine.FrameCount && engine.FrameCount>0,
                    "CPU preset saves and resumes with exact decoded frame count");
                Check(profiles.Enabled,"Preset unlocks after saving");
            }
            finally { if(engine.State!=RecorderState.Idle) await InvokeAsync(form,"StopRecordingAsync"); }
            SelectCodec(codecs, EncoderChoice.Ffv1);
            Check(settings.Profile==RecordingProfile.Custom && settings.Encoder==EncoderChoice.Ffv1,
                "Manual codec selection leaves preset mode");
            profiles.SelectedIndex=(int)RecordingProfile.Low;
            typeof(MainForm).GetField("_region",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(form,new Rectangle(80,240,321,181));
            Invoke(form,"PlanChanged");
            Snapshot(form,"profiles-low-main");
            await InvokeAsync(form,"StartRecordingAsync");
            try
            {
                Check(engine.CurrentPlan?.Encoder==EncoderChoice.Mpeg4Cpu && engine.State==RecorderState.Recording,
                    "Low preset starts actual MPEG-4 CPU recording at odd source dimensions");
                await Task.Delay(450);await InvokeAsync(form,"TogglePauseAsync");
                await InvokeAsync(form,"TogglePauseAsync");await Task.Delay(450);
                await InvokeAsync(form,"StopRecordingAsync");
                Check((await Hashes(engine.OutputFile)).LongLength==engine.FrameCount && engine.FrameCount>0,
                    "MPEG-4 pause and resume preserves decoded frame count");
                string details=await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,new[]{"-v","error","-show_entries","stream=codec_name,width,height","-of","csv=p=0",engine.OutputFile});
                Check(details.Contains("mpeg4,322,182"),"Odd compressed selection pads at most one pixel on right and bottom");
            }
            finally { if(engine.State!=RecorderState.Idle) await InvokeAsync(form,"StopRecordingAsync"); }
            form.Close();
        }
        if(!_caps.NvencH264)
        {
            var fallbackPlan=new RecordPlan(new Rectangle(80,240,128,72),null,RecordingScope.Region,RecordingPurpose.Game,
                EncoderChoice.NvencH264,CaptureBackend.Gdi,15,false,false,false,1,1,Quality:CompressionQuality.High);
            using var fallbackEngine=new RecorderEngine(); fallbackEngine.Message+=Console.WriteLine;
            await fallbackEngine.StartAsync(fallbackPlan,cpuCaps,Path.Combine(_root,"hardware-fallback"),true);
            try
            {
                Check(fallbackEngine.CurrentPlan?.Encoder==EncoderChoice.H264Cpu && fallbackEngine.CurrentPlan?.Quality==CompressionQuality.High,
                    "Failed compressed GPU startup falls back to CPU H.264 and keeps the selected quality");
                await Task.Delay(300);
                string file=await fallbackEngine.StopAsync();
                Check((await Hashes(file)).LongLength==fallbackEngine.FrameCount,"Compressed GPU-to-CPU fallback creates a complete decodable video");
            }
            finally { if(fallbackEngine.State!=RecorderState.Idle)await fallbackEngine.StopAsync(); }
        }
        using(var reopened=new MainForm())
        {
            Check(Field<ChoiceBox>(reopened,"_profileBox").SelectedIndex==(int)RecordingProfile.Low
                && Field<AppSettings>(reopened,"_settings").Fps==15,"Reopening restores the chosen low preset and FPS");
        }
    }
}
