using System.Diagnostics;
using NAudio.Wave;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static int MediaProgressFixture(string[] args, int index)
    {
        string mode = args[index + 1];
        File.WriteAllText(args[index + 2], Environment.ProcessId.ToString());
        for (int i = 0; i < 14; i++)
        {
            Console.WriteLine("frame=" + (mode == "frame" ? i + 1 : 1));
            Console.WriteLine("total_size=" + (mode == "size" ? (i + 1) * 1024 : 1024));
            Console.WriteLine("out_time_us=" + (mode == "time" ? (i + 1) * 180000 : 0));
            Console.WriteLine("out_time_us=NaN");
            Console.WriteLine("total_size=N/A");
            Console.WriteLine("frame=-1");
            Console.WriteLine("progress=continue");
            Console.Error.WriteLine("repeated controlled warning");
            Console.Out.Flush(); Console.Error.Flush(); Thread.Sleep(180);
        }
        return 0;
    }
    private static async Task TestCodeAuditAsync(bool capture)
    {
        string folder = Path.Combine(_root, "code-audit"); Directory.CreateDirectory(folder);
        foreach (string mode in new[] { "stuck", "time", "frame", "size" })
        {
            string pidFile = Path.Combine(folder, mode + ".pid");
            var progress = new List<double>(); var mediaTimes = new List<double>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            bool stalled = false;
            try
            {
                await MediaProcess.RunAsync(Environment.ProcessPath!, new[] { "--media-progress-fixture", mode, pidFile },
                    3, progress.Add, deadline.Token, TimeSpan.FromMilliseconds(750), mediaTimes.Add);
            }
            catch (TimeoutException) { stalled = true; }
            Check(stalled == (mode == "stuck"), mode + ": watchdog distinguishes advancing media from repeated heartbeats and errors");
            Check(progress.Concat(mediaTimes).All(double.IsFinite), mode + ": malformed progress cannot reach callbacks as NaN");
            if (!stalled) Check(progress[^1] == 1, mode + ": healthy process completes even after exceeding the stall interval");
            int pid = int.Parse(File.ReadAllText(pidFile));
            bool Alive() { try { using var child = Process.GetProcessById(pid); return !child.HasExited; } catch (ArgumentException) { return false; } }
            for (int i = 0; i < 20 && Alive(); i++) await Task.Delay(50);
            Check(!Alive(), mode + ": media child is reaped on completion or timeout");
        }
        var disconnected = new FakeAudioSource(RecoveringAudioTrack.TimelineFormat) { StopOnStart = true };
        using (var preview = new AudioLevelPreview(true, false, "", "", _ => { }, (_, _) => disconnected))
            Check(!preview.SystemAvailable && preview.NeedsReconnect, "Preview disconnect during Start remains visible and retryable");
        Check(disconnected.Disposed, "Disconnected preview source is released");
        var failed = new FakeAudioSource(RecoveringAudioTrack.TimelineFormat) { FailStart = true };
        using (var preview = new AudioLevelPreview(false, true, "", "", _ => { }, (_, _) => failed))
            Check(!preview.MicrophoneAvailable && preview.NeedsReconnect && failed.Disposed, "Failed preview start releases the device and remains retryable");
        var healthy = new FakeAudioSource(RecoveringAudioTrack.TimelineFormat);
        using (var preview = new AudioLevelPreview(true, false, "", "", _ => { }, (_, _) => healthy))
        {
            Check(preview.SystemAvailable && !preview.NeedsReconnect, "Healthy preview is available without reconnecting");
            healthy.Disconnect();
            Check(!preview.SystemAvailable && preview.NeedsReconnect, "Preview responds to a later device disconnect");
        }
        var first = new FakeAudioSource(RecoveringAudioTrack.TimelineFormat) { FailDispose = true };
        var second = new FakeAudioSource(RecoveringAudioTrack.TimelineFormat);
        using (var preview = new AudioLevelPreview(true, true, "", "", _ => { }, (system, _) => system ? first : second)) { }
        Check(first.Disposed && second.Disposed, "Cleanup releases both preview devices when one disposal fails");
        string retries = Path.Combine(folder, "retry-names"); Directory.CreateDirectory(retries);
        Check(ContinuousMedia.NextSegmentIndex(retries, 1) == 1, "Fresh recording starts with segment one");
        File.WriteAllText(Path.Combine(retries, "video-001-part-000005.mkv"), "failed attempt");
        File.WriteAllText(Path.Combine(retries, "audio-system-002-part-000001.wav"), "failed audio");
        File.WriteAllText(Path.Combine(retries, "audio-mic-003.wav"), "failed microphone");
        Check(ContinuousMedia.NextSegmentIndex(retries, 1) == 4, "Retry skips leftover video, system audio and microphone filenames");
        Check(ContinuousMedia.NextSegmentIndex(retries, 5) == 5 && Directory.GetFiles(retries).Length == 3,
            "Retry respects its minimum index and preserves failed attempt files");
        if (!capture) return;
        var work = Screen.PrimaryScreen!.WorkingArea;
        var plan = new RecordPlan(new(work.Left + 80, work.Top + 120, 320, 180), null,
            RecordingScope.Region, RecordingPurpose.Desktop, EncoderChoice.X264Rgb, CaptureBackend.Gdi,
            15, false, false, false, 1, 1);
        using var engine = new RecorderEngine { ChunkSeconds = 1 };
        await engine.StartAsync(plan, _caps, Path.Combine(folder, "live"), false);
        await Task.Delay(250); await engine.PauseAsync();
        string old = Path.Combine(engine.WorkingDirectory, "video-002-part-000005.mkv");
        File.WriteAllText(old, "controlled failed attempt must not enter export");
        await engine.ResumeAsync(); await Task.Delay(250); await engine.PauseAsync();
        var manifest = RecordingManifest.Load(engine.WorkingDirectory);
        Check(manifest.Segments.Select(s => s.Video).SequenceEqual(new[] { "video-001.mkv", "video-003.mkv" }),
            "Actual pause/resume chooses a fresh name after a failed attempt leaves chunks");
        Check(File.ReadAllText(old).Contains("must not enter export"), "Resume preserves leftover material until final cleanup");
        long frames = engine.FrameCount;
        await engine.StopAsync();
        Check((await Hashes(engine.OutputFile)).LongLength == frames && frames > 0,
            "Actual resumed recording exports every accepted frame without mixing failed chunks");
        await Run("-v", "error", "-xerror", "-err_detect", "explode", "-i", engine.OutputFile, "-f", "null", "-");
        Check(true, "Actual retry output fully decodes without errors");
    }
}
