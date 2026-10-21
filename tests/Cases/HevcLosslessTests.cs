namespace ScreenLossless;

internal static partial class RegressionTests
{
    private static async Task<string[]> HevcHashesAsync(string file) => HevcLossless.FrameHashes(await Run(
        "-v", "error", "-i", file, "-pix_fmt", "yuv444p10le", "-f", "framemd5", "-"));

    private static async Task TestHevcLosslessAsync(bool capture = false)
    {
        string folder = Path.Combine(_root, "hevc-lossless"); Directory.CreateDirectory(folder);
        // Existing numeric settings must retain their meanings when adding a new codec.
        Check((int)EncoderChoice.AmdH264 == 9 && (int)EncoderChoice.Ffv1 == 3,
            "HEVC lossless addition preserves existing saved encoder identifiers");
        new AppSettings { Encoder = EncoderChoice.NvencHevcLossless, Profile = RecordingProfile.Custom }.Save();
        Check(AppSettings.Load().Encoder == EncoderChoice.NvencHevcLossless,
            "HEVC lossless selection survives settings save and load");
        Check(!VideoEncoding.Supported(EncoderChoice.NvencHevcLossless, new Capabilities { NvencHevc = true }),
            "Regular HEVC availability does not imply HEVC 10-bit 4:4:4 lossless availability");
        var missing = await HardwareProbe.ProbeHardwareAsync(_caps.Ffmpeg, "", EncoderChoice.NvencHevcLossless);
        Check(!missing.Listed && !missing.Available, "FFmpeg without HEVC cannot advertise HEVC lossless");
        var noProbe = await HevcLossless.ProbeAsync(Path.Combine(folder, "missing-ffprobe", "ffmpeg.exe"));
        Check(!noProbe.Available && noProbe.Detail.Contains("ffprobe"),
            "HEVC cannot claim verified 10-bit output when ffprobe is missing");
        var trial = _caps.HardwareTests.Single(t => t.Encoder == EncoderChoice.NvencHevcLossless);
        Check(trial.Available == _caps.NvencHevcLossless && trial.Detail.Length > 0,
            "HEVC lossless availability is backed by a separate verification result");
        if (!_caps.NvencHevcLossless)
        { Console.WriteLine("SKIP HEVC lossless hardware: " + trial.Detail); return; }

        foreach (var item in new[] { (new Size(321, 181), OutputResolution.Original),
            (new Size(641, 361), OutputResolution.Percent50) })
        {
            var plan = new RecordPlan(new Rectangle(Point.Empty, item.Item1), null, RecordingScope.Region,
                RecordingPurpose.Desktop, EncoderChoice.NvencHevcLossless, CaptureBackend.Gdi, 15,
                false, false, false, 1, 1, Resolution: item.Item2, Quality: CompressionQuality.Small);
            string file = Path.Combine(folder, item.Item2 + ".mkv");
            var options = FfmpegArguments.Capture(plan, file);
            var input = new[] { "-v", "error", "-y", "-f", "lavfi", "-i",
                $"testsrc=size={item.Item1.Width}x{item.Item1.Height}:rate=15", "-frames:v", "3" };
            await Run(input.Concat(options.Skip(options.IndexOf("-map"))).ToArray());
            await HevcLossless.VerifyFormatAsync(_caps.Ffprobe, file, plan.OutputSize.Width, plan.OutputSize.Height);
            Check(true, "HEVC " + item.Item2 + ": real output keeps 10-bit 4:4:4 and intended dimensions");
            var reference = input.Concat(new[] { "-vf", options[options.IndexOf("-vf") + 1],
                "-pix_fmt", "yuv444p10le", "-f", "framemd5", "-" });
            var expected = HevcLossless.FrameHashes(await Run(reference.ToArray()));
            var actual = await HevcHashesAsync(file);
            Check(expected.Length == 3 && expected.SequenceEqual(actual),
                "HEVC " + item.Item2 + ": every decoded YUV sample matches the pre-encoding reference");
            string mp4 = Path.ChangeExtension(file, ".mp4");
            await Run(FfmpegArguments.ExportMp4(file, mp4).ToArray());
            await HevcLossless.VerifyFormatAsync(_caps.Ffprobe, mp4, plan.OutputSize.Width, plan.OutputSize.Height);
            Check(actual.SequenceEqual(await HevcHashesAsync(mp4)), "HEVC " + item.Item2 + ": MP4 export preserves every video sample");
        }
        string wrong = Path.Combine(folder, "wrong-format.mkv");
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=15",
            "-frames:v", "1", "-c:v", "libx264rgb", "-crf", "0", wrong);
        bool rejected = false;
        try { await HevcLossless.VerifyFormatAsync(_caps.Ffprobe, wrong, 320, 180); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "HEVC verification rejects an output with the wrong codec or bit depth");

        string work = Path.Combine(folder, ".screenlossless-work", "continuous"); Directory.CreateDirectory(work);
        var continuousPlan = new RecordPlan(new(0, 0, 640, 360), null, RecordingScope.Region,
            RecordingPurpose.Game, EncoderChoice.NvencHevcLossless, CaptureBackend.Gdi, 15, false, false, false, 1, 1);
        var continuous = ContinuousMedia.Capture(continuousPlan, Path.Combine(work, "video-001.mkv"), 1);
        var synthetic = new[] { "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=15", "-frames:v", "45" };
        await Run(synthetic.Concat(continuous.Skip(continuous.IndexOf("-map"))).ToArray());
        var manifest = new RecordingManifest { TargetFile = "continuous.mkv", Segments = new() { new("video-001.mkv", null, null, true) } };
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", Path.Combine(work, "audio-system-001.wav"));
        manifest.SystemAudio = true;
        manifest.Segments[0] = new("video-001.mkv", "audio-system-001.wav", null, true);
        manifest.Save(work);
        var parts = ContinuousMedia.VideoFiles(work, manifest.Segments[0]);
        Check(parts.Length == 3, "HEVC continuous recording creates three independently protected files");
        foreach (string part in parts)
        {
            await HevcLossless.VerifyFormatAsync(_caps.Ffprobe, part, 640, 360);
            await Run("-v", "error", "-xerror", "-err_detect", "explode", "-i", part, "-f", "null", "-");
        }
        string merged = await RecordingFinalizer.ExportAsync(_caps, work, manifest, _ => { });
        var baseline = HevcLossless.FrameHashes(await Run(synthetic.Concat(new[] { "-vf", HevcLossless.ColorFilter,
            "-pix_fmt", "yuv444p10le", "-f", "framemd5", "-" }).ToArray()));
        Check(baseline.Length == 45 && baseline.SequenceEqual(await HevcHashesAsync(merged)),
            "HEVC segment merge preserves all 45 frames and every YUV sample");
        string audioInfo = await HardwareProbe.ExecuteTextAsync(_caps.Ffprobe,
            new[] { "-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", merged });
        Check(audioInfo.Contains("flac"), "HEVC protected video merges with the system audio track");
        if (!capture) return;

        var monitor = WindowsInterop.GetMonitors().First(m => m.Primary);
        var bounds = new Rectangle(monitor.Bounds.Left + 30, monitor.Bounds.Top + 30, 320, 180);
        var backends = new List<CaptureBackend> { CaptureBackend.Gdi };
        if (HardwareProbe.ResolveBackend(true, RecordingScope.Region, EncoderChoice.NvencHevcLossless, monitor, bounds, _caps) == CaptureBackend.Dxgi)
            backends.Add(CaptureBackend.Dxgi);
        else Console.WriteLine("SKIP automatic DXGI routing: primary output does not meet the existing DISPLAY1 mapping rule.");
        foreach (var backend in backends)
        {
            var plan = continuousPlan with { Bounds = bounds, Monitor = monitor, Backend = backend };
            using var engine = new RecorderEngine(); engine.Message += Console.WriteLine;
            await engine.StartAsync(plan, _caps, folder, false); await Task.Delay(350);
            Check(engine.State == RecorderState.Recording && engine.CurrentPlan!.Encoder == EncoderChoice.NvencHevcLossless,
                "HEVC " + backend + ": actual desktop capture starts without changing codecs");
            await engine.PauseAsync(); var elapsed = engine.Duration; await Task.Delay(100);
            Check(engine.Duration == elapsed, "HEVC " + backend + ": pause freezes recording time");
            await engine.ResumeAsync(); await Task.Delay(350);
            string saved = await engine.StopAsync();
            await HevcLossless.VerifyFormatAsync(_caps.Ffprobe, saved, bounds.Width, bounds.Height);
            Check((await HevcHashesAsync(saved)).LongLength == engine.FrameCount && engine.FrameCount > 0,
                "HEVC " + backend + ": resume, merge and stop retain every recorded frame in 10-bit 4:4:4");
        }
        if (_caps.GfxCapture)
        {
            using var target = new Form { Text = "ScreenLossless HEVC test", ClientSize = new Size(320, 180),
                BackColor = Color.CornflowerBlue, StartPosition = FormStartPosition.Manual, Location = bounds.Location, ShowInTaskbar = false };
            using var repaint = new System.Windows.Forms.Timer { Interval = 50 };
            target.Paint += (_, e) => e.Graphics.FillRectangle(Brushes.Blue, DateTime.Now.Millisecond % 200, 5, 10, 10);
            repaint.Tick += (_, _) => target.Invalidate(); target.Show(); repaint.Start();
            var client = WindowsInterop.GetWindowClientBounds(target.Handle);
            var plan = continuousPlan with { Bounds = client, Scope = RecordingScope.Window,
                Backend = CaptureBackend.WindowsGraphics, WindowHandle = target.Handle.ToInt64() };
            using var engine = new RecorderEngine();
            await engine.StartAsync(plan, _caps, folder, false); await Task.Delay(300);
            Check(engine.CurrentPlan!.Backend == CaptureBackend.WindowsGraphics && engine.FrameCount > 0,
                "HEVC modern window capture produces actual hardware-encoded frames");
            string file = await engine.StopAsync();
            await HevcLossless.VerifyFormatAsync(_caps.Ffprobe, file, client.Width, client.Height);
            Check((await HevcHashesAsync(file)).LongLength == engine.FrameCount,
                "HEVC modern window output retains 10-bit 4:4:4 and every recorded frame");
            repaint.Stop(); target.Close();
        }
        new AppSettings { FfmpegPath = _caps.Ffmpeg, OutputFolder = folder, Encoder = EncoderChoice.NvencHevcLossless,
            Countdown = false, RecordSystemAudio = false, MinimizeWhenRecording = false }.Save();
        using var form = new MainForm { Opacity = 0, ShowInTaskbar = false };
        form.Show(); await Until(() => Field<Capabilities>(form, "_caps").Available && !Field<bool>(form, "_busy"));
        Check((Field<ChoiceBox>(form, "_codecBox").SelectedItem as EncoderOption)?.Encoder == EncoderChoice.NvencHevcLossless
            && Field<Label>(form, "_encodingNote").Text.Contains("RGB") && !Field<ChoiceBox>(form, "_qualityBox").Enabled,
            "HEVC UI restores the codec, explains RGB limits and disables lossy quality controls");
        form.Close();
    }
}
