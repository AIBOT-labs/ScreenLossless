using NAudio.Wave;
using System.Globalization;
using System.Reflection;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private sealed class FakeAudioSource : IAudioCaptureSource
    {
        public WaveFormat Format { get; }
        public event EventHandler<WaveInEventArgs>? Data;
        public event EventHandler<StoppedEventArgs>? Stopped;
        internal bool Disposed, FailStart, StopOnStart, FailDispose;
        internal FakeAudioSource(WaveFormat format) { Format = format; }
        public void Start() { if(FailStart)throw new IOException("controlled device start failure"); if(StopOnStart)Disconnect(); }
        public void Stop() => Stopped?.Invoke(this,new StoppedEventArgs());
        public void Dispose() { Disposed=true; if(FailDispose)throw new IOException("controlled device disposal failure"); }
        internal void Disconnect()=>Stopped?.Invoke(this,new StoppedEventArgs(new IOException("controlled unplug")));
        internal void Push(byte[] bytes)=>Data?.Invoke(this,new WaveInEventArgs(bytes,bytes.Length));
        internal void Tone(double seconds)
        {
            int frames=(int)(seconds*Format.SampleRate);byte[] bytes=new byte[frames*Format.BlockAlign];
            for(int f=0;f<frames;f++)for(int ch=0;ch<Format.Channels;ch++)
            {
                double value=.4*Math.Sin(2*Math.PI*440*f/Format.SampleRate);
                byte[] sample=Format.Encoding==WaveFormatEncoding.IeeeFloat?BitConverter.GetBytes((float)value):BitConverter.GetBytes((short)(value*32767));
                Buffer.BlockCopy(sample,0,bytes,f*Format.BlockAlign+ch*sample.Length,sample.Length);
            }
            Data?.Invoke(this,new WaveInEventArgs(bytes,bytes.Length));
        }
    }
    private static async Task TestAudioTimelineAsync()
    {
        string dir=Path.Combine(_root,"audio-timeline");Directory.CreateDirectory(dir);
        TimeSpan now=TimeSpan.Zero;int connections=0;FakeAudioSource? source=null;
        string file=Path.Combine(dir,"audio-system-001.wav");
        using(var track=new RecoveringAudioTrack(file,()=>source=new FakeAudioSource(++connections==1?new WaveFormat(44100,16,1):WaveFormat.CreateIeeeFloatWaveFormat(48000,2)),_=>{},8L*1024*1024,()=>now))
        {
            await track.RetryAsync();
            byte[] pcm=new byte[4410*2];for(int i=0;i<4410;i++){short value=(short)(12000*Math.Sin(2*Math.PI*440*i/44100));BitConverter.TryWriteBytes(pcm.AsSpan(i*2,2),value);}
            byte[] floats=new byte[4800*8];for(int i=0;i<4800;i++){float value=(float)(.4*Math.Sin(2*Math.PI*440*i/48000));BitConverter.TryWriteBytes(floats.AsSpan(i*8,4),value);BitConverter.TryWriteBytes(floats.AsSpan(i*8+4,4),value);}
            for(int i=0;i<6000;i++)
            {
                if(i==4000){source!.Disconnect();now+=TimeSpan.FromSeconds(2);await track.RetryAsync();}
                now+=TimeSpan.FromMilliseconds(100);source!.Push(i<4000?pcm:floats);
            }
            await track.StopAsync();
        }
        var files=ContinuousMedia.Parts(dir,Path.GetFileName(file),".wav",true);long bytes=0;
        foreach(string part in files){using var reader=new WaveFileReader(part);bytes+=reader.Length;}
        Check(Math.Abs(bytes/384000d-602)<.005,"Accelerated ten-minute audio stream retains sample count across 6000 packets and a two-second unplug");
        Check(files.Length>20,"Long audio timeline continuously rotates small WAV files without reopening the device");
        Check(connections==2,"Audio file rotation never reconnects a healthy device");
    }
    private static async Task TestStabilityAsync()
    {
        string folder=Path.Combine(_root,"stability");Directory.CreateDirectory(folder);
        TimeSpan elapsed=TimeSpan.FromSeconds(1);bool unplugged=true;var sources=new List<FakeAudioSource>();var log=new List<string>();
        IAudioCaptureSource Factory()
        {
            if(unplugged)throw new IOException("controlled missing selected device");
            var source=new FakeAudioSource(sources.Count==0?new WaveFormat(44100,16,1):WaveFormat.CreateIeeeFloatWaveFormat(48000,2));sources.Add(source);return source;
        }
        string audio=Path.Combine(folder,"audio-system-001.wav");
        using(var track=new RecoveringAudioTrack(audio,Factory,m=>log.Add(m),768000,()=>elapsed))
        {
            await track.RetryAsync();Check(!track.Available,"Missing audio device remains retryable without failing video");
            unplugged=false;await track.RetryAsync();Check(track.Available,"Audio connects after being absent at startup");
            elapsed=TimeSpan.FromSeconds(1.2);sources[0].Tone(.2);Check(track.Level>0,"44.1 kHz mono PCM converts to the stable stereo timeline");
            sources[0].Disconnect();unplugged=true;elapsed=TimeSpan.FromSeconds(3);await track.RetryAsync();
            Check(!track.Available&&sources[0].Disposed,"Disconnected source is closed before reconnecting");
            unplugged=false;await track.RetryAsync();Check(track.Available,"Audio reconnects inside the same recording run");
            elapsed=TimeSpan.FromSeconds(3.2);sources[1].Tone(.2);elapsed=TimeSpan.FromSeconds(3.4);
            await Task.WhenAll(track.StopAsync(),track.RetryAsync());
            Check(!track.Available&&sources.All(s=>s.Disposed),"Stop/retry race closes all sources without reopening devices");
        }
        var parts=ContinuousMedia.Parts(folder,Path.GetFileName(audio),".wav",true);
        var samples=new List<float>();
        foreach(string part in parts){using var reader=new WaveFileReader(part);Check(reader.WaveFormat.SampleRate==48000&&reader.WaveFormat.Channels==2,"Audio chunks retain a fixed format across device changes");var provider=reader.ToSampleProvider();float[] buffer=new float[4096];int count;while((count=provider.Read(buffer,0,buffer.Length))>0)samples.AddRange(buffer.Take(count));}
        Check(parts.Length==2&&Math.Abs(samples.Count/96000d-3.4)<.005,"Audio chunk rotation preserves the complete timeline");
        Check(samples.Take(96000).All(s=>s==0)&&samples.Skip((int)(1.3*96000)).Take((int)(1.6*96000)).All(s=>s==0),"Missing device intervals remain silence without shifting later audio");
        Check(samples.Skip(96000).Take(15000).Any(s=>Math.Abs(s)>.1)&&samples.Skip(3*96000).Take(15000).Any(s=>Math.Abs(s)>.1),"Actual audio remains present before and after reconnect with different formats");
        Check(log.Any(m=>m.Contains("重新连接")),"Reconnect success is reported in diagnostics");
        elapsed=TimeSpan.Zero;int attempts=0;FakeAudioSource? failed=null;
        using(var track=new RecoveringAudioTrack(Path.Combine(folder,"start-failure.wav"),()=>{attempts++;var f=new FakeAudioSource(new WaveFormat(48000,16,2)){FailStart=attempts==1};if(attempts==1)failed=f;return f;},_=>{},elapsed:()=>elapsed))
        {await track.RetryAsync();Check(!track.Available&&failed!.Disposed,"Failed device start disposes the partial capture");await track.RetryAsync();Check(track.Available,"Device start failure retries successfully without restarting video");await track.StopAsync();}
        var plan=new RecordPlan(new(0,0,128,72),null,RecordingScope.Region,RecordingPurpose.Game,EncoderChoice.H264Cpu,CaptureBackend.Gdi,60,false,false,false,1,1,AutoPerformance:true);
        var adaptive=new AdaptivePerformance();var slow=new PerformanceSnapshot(20,.6,0,true);
        Check(adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(14),TimeSpan.FromSeconds(14)) is null,"Adaptive protection ignores startup");
        Check(adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(15),TimeSpan.FromSeconds(15)) is null&&adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(29),TimeSpan.FromSeconds(29)) is null,"A brief slowdown does not change FPS");
        Check(adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30))==30,"Sustained slowdown lowers FPS by one step");
        plan=plan with{Fps=30};Check(adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(20)) is null&&adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(90),TimeSpan.FromSeconds(50))==24,"Adaptive cooldown prevents rapid repeated changes");
        adaptive.Reset();adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30));adaptive.NextFps(plan,new(30,1,0,true),TimeSpan.FromSeconds(44),TimeSpan.FromSeconds(44));
        Check(adaptive.NextFps(plan,slow,TimeSpan.FromSeconds(45),TimeSpan.FromSeconds(45)) is null,"Recovered speed resets sustained-slow timing");
        foreach(var excluded in new[]{plan with{Encoder=EncoderChoice.X264Rgb},plan with{AutoPerformance=false},plan with{Fps=10}})
        {adaptive.Reset();adaptive.NextFps(excluded,slow,TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(30));Check(adaptive.NextFps(excluded,slow,TimeSpan.FromSeconds(60),TimeSpan.FromSeconds(60)) is null,"Lossless, disabled protection and minimum FPS are respected");}
        foreach(var encoder in new[]{EncoderChoice.X264Rgb,EncoderChoice.Ffv1,EncoderChoice.Mpeg4Cpu,EncoderChoice.H264Cpu})
        {
            string dir=Path.Combine(folder,".screenlossless-work","continuous-"+encoder);Directory.CreateDirectory(dir);
            var sourcePlan=plan with{Encoder=encoder,Fps=15,AutoPerformance=false};var args=ContinuousMedia.Capture(sourcePlan,Path.Combine(dir,"video-001.mkv"),1);
            var generated=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc=size=128x72:rate=15","-frames:v","90"};generated.AddRange(args.Skip(args.IndexOf("-map")));await Run(generated.ToArray());
            bool withAudio=encoder==EncoderChoice.X264Rgb;
            if(withAudio)foreach(var part in parts)File.Copy(part,Path.Combine(dir,Path.GetFileName(part)));
            var segment=new RecordingSegment("video-001.mkv",withAudio?Path.GetFileName(audio):null,null,true);var files=ContinuousMedia.VideoFiles(dir,segment);
            Check(files.Length==6,encoder+": one encoder produces six independently readable chunks");
            foreach(string part in files)await Run("-v","error","-xerror","-err_detect","explode","-i",part,"-f","null","-");
            Check(true,encoder+": every protection chunk independently decodes with strict error detection");
            string baseline=Path.Combine(dir,"reference.mkv");var reference=new List<string>{"-v","error","-y","-f","lavfi","-i","testsrc=size=128x72:rate=15","-frames:v","90"};reference.AddRange(FfmpegArguments.Capture(sourcePlan,baseline).Skip(args.IndexOf("-map")));await Run(reference.ToArray());
            var manifest=new RecordingManifest{TargetFile="continuous-"+encoder+".mkv",SystemAudio=withAudio,Segments=new(){segment}};manifest.Save(dir);
            var restored=RecordingManifest.Load(dir);Check(restored.Segments[0].Continuous,"Recovery persists continuous-run identity");
            string result=await RecordingFinalizer.ExportAsync(_caps,dir,restored,_=>{});
            var hashes=await Hashes(result);Check(hashes.Length==90,encoder+": continuous finalization loses and duplicates no frames");
            if(sourcePlan.IsLossless)Check(hashes.SequenceEqual(await Hashes(baseline)),encoder+": every decoded pixel matches the uninterrupted reference");
            await Run("-v","error","-i",result,"-f","null","-");Check(true,encoder+": every joined chunk decodes");
            if(withAudio)
            {
                string decoded=Path.Combine(dir,"decoded-audio.wav");await Run("-v","error","-y","-i",result,"-map","0:a:0","-c:a","pcm_f32le",decoded);
                using var reader=new WaveFileReader(decoded);var provider=reader.ToSampleProvider();float[] buffer=new float[(int)reader.Length/4];int count=provider.Read(buffer,0,buffer.Length);
                Check(Math.Abs(count/96000d-6)<.01,"Audio chunk merge follows the continuous six-second video timeline");
                Check(buffer.Skip((int)(1.3*96000)).Take(96000).All(x=>Math.Abs(x)<.0001)&&buffer.Skip(3*96000).Take(15000).Any(x=>Math.Abs(x)>.1),"Export preserves the silent unplug interval and the reconnected tone position");
            }
            string missing=files[1]+".missing";File.Move(files[1],missing);
            try
            {
                bool rejected=false;try{await RecordingFinalizer.ExportAsync(_caps,dir,new RecordingManifest{TargetFile="missing-"+encoder+".mkv",Segments=new(){segment}},_=>{});}catch(InvalidDataException){rejected=true;}
                Check(rejected&&!File.Exists(Path.Combine(folder,"missing-"+encoder+".mkv")),"Missing middle protection chunk rejects export instead of shortening the recording");
            }
            finally{File.Move(missing,files[1]);}
        }
        var work=Screen.PrimaryScreen!.WorkingArea;using var target=new Form{Text="Stability owned fixture",StartPosition=FormStartPosition.Manual,Location=work.Location+new Size(40,80),ClientSize=new(320,180),BackColor=Color.DarkGreen,ShowInTaskbar=false};target.Show();
        string live=Path.Combine(folder,"live");Directory.CreateDirectory(live);
        var livePlan=plan with{Bounds=WindowsInterop.GetWindowClientBounds(target.Handle),WindowHandle=target.Handle.ToInt64(),Fps=30,Scope=RecordingScope.Window,Container=OutputContainer.Mp4};
        using(var engine=new RecorderEngine{ChunkSeconds=1})
        {
            await engine.StartAsync(livePlan,_caps,live,true);int? process=engine.CaptureProcessId;
            await Task.Delay(2400);await engine.RotateSegmentAsync();
            Check(engine.CaptureProcessId==process&&engine.State==RecorderState.Recording,"Real automatic group rotation never restarts the capture process");
            Check(ContinuousMedia.VideoFiles(engine.WorkingDirectory,RecordingManifest.Load(engine.WorkingDirectory).Segments[0]).Length>=3,"Real capture writes several continuous protection chunks");
            await engine.ChangeFpsAsync(15);Check(engine.CurrentPlan!.Fps==15&&engine.CurrentPlan.Bounds==livePlan.Bounds&&engine.CurrentPlan.Encoder==livePlan.Encoder,"Real adaptive restart keeps output dimensions and codec");
            await Task.Delay(1200);string file=await engine.StopAsync();
            await Run("-v","error","-i",file,"-f","null","-");Check(File.Exists(file),"Mixed-FPS runs export a completely decodable MP4");
        }
        new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=live,Scope=RecordingScope.Region,LastRegion=RegionSelection.FromRectangle(livePlan.Bounds),Encoder=EncoderChoice.H264Cpu,Fps=15,RecordSystemAudio=false,RecordMicrophone=false,Countdown=false,MinimizeWhenRecording=false}.Save();
        using(var main=new MainForm())
        {
            main.Show();await Until(()=>!Field<bool>(main,"_busy")&&Field<Capabilities>(main,"_caps").Available);
            Field<ToggleCheck>(main,"_autoPerformance").Checked=true;
            Check(AppSettings.Load().AutoPerformance,"Performance protection preference persists");
            await InvokeAsync(main,"StartRecordingAsync");var engine=Field<RecorderEngine>(main,"_engine");
            Check(!Field<ToggleCheck>(main,"_autoPerformance").Enabled,"Active session locks performance protection preference");
            typeof(MainForm).GetMethod("QueueDeviceRefresh",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,new object[]{true});
            await Task.Delay(1800);Check(engine.State==RecorderState.Recording,"Redundant display notification does not prematurely stop recording");
            await InvokeAsync(main,"StopRecordingAsync");Check(!File.Exists(Path.ChangeExtension(engine.OutputFile,".log")) && engine.LastStopReason.Length == 0,"Redundant display notification produces neither a stop reason nor a log file");main.Close();
        }
    }
}
