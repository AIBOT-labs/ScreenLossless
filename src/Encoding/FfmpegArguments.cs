using System.Drawing;
using System.Globalization;

namespace ScreenLossless;

/// <summary>
/// No shell strings: every argument is passed through ProcessStartInfo.ArgumentList.
/// Original lossless choices stay lossless; compressed choices are explicitly selected and labelled.
/// </summary>
internal static class FfmpegArguments
{
    // Raw GDI packets contain whole frames. 512 queued 4K frames could retain
    // roughly 16 GiB; bound the queue to a 64 MiB target (at least one frame).
    internal static int GdiQueueFrames(Size size)
    {
        long bytes=checked((long)size.Width*size.Height*4);
        if(bytes<=0)throw new ArgumentOutOfRangeException(nameof(size));
        return (int)Math.Clamp(64L*1024*1024/bytes,1,8);
    }
    public static List<string> Capture(RecordPlan plan, string output, bool forceGdi = false)
    {
        if (plan.Bounds.Width < 64 || plan.Bounds.Height < 64)
            throw new ArgumentException(L10n.T("录制区域至少需要 64×64 像素。"), nameof(plan));
        if (plan.Fps is < 1 or > 240) throw new ArgumentException(L10n.T("无效帧率。"), nameof(plan));
        OutputOptions.Validate(plan);
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-nostats", "-stats_period", "0.5", "-progress", "pipe:2", "-y" };
        bool useDxgi = plan.WindowHandle==0 && plan.Backend == CaptureBackend.Dxgi && !forceGdi && plan.Encoder is (EncoderChoice.Nvenc or EncoderChoice.NvencHevcLossless);
        if(plan.Backend==CaptureBackend.WindowsGraphics&&plan.WindowHandle!=0&&!forceGdi)
            args.AddRange(new[]{"-f","lavfi","-i",GraphicsWindowSource(plan)});
        else if (useDxgi)
        {
            if (plan.Monitor is null) throw new InvalidOperationException(L10n.T("DXGI 模式必须指定显示器。"));
            var display = plan.Monitor.Bounds;
            int offsetX = plan.Bounds.X - display.X, offsetY = plan.Bounds.Y - display.Y;
            if (offsetX < 0 || offsetY < 0 || offsetX + plan.Bounds.Width > display.Width || offsetY + plan.Bounds.Height > display.Height)
                throw new InvalidOperationException(L10n.T("DXGI 捕获区域必须处在同一个显示器内。"));
            // DXGI output index does not necessarily correspond to Screen.AllScreens on every machine.
            // Auto mode only chooses the primary display, normally DXGI output_idx=0.
            var filter = $"ddagrab=output_idx=0:framerate={plan.Fps}:draw_mouse={(plan.Cursor ? 1 : 0)}";
            if (offsetX != 0) filter += $":offset_x={offsetX}";
            if (offsetY != 0) filter += $":offset_y={offsetY}";
            if (plan.Bounds.Width != display.Width || plan.Bounds.Height != display.Height)
                filter += $":video_size={plan.Bounds.Width}x{plan.Bounds.Height}";
            args.AddRange(new[] { "-f", "lavfi", "-i", filter });
        }
        else
        {
            args.AddRange(new[] {
                "-thread_queue_size", GdiQueueFrames(plan.Bounds.Size).ToString(CultureInfo.InvariantCulture), "-f", "gdigrab", "-framerate", plan.Fps.ToString(CultureInfo.InvariantCulture),
                "-draw_mouse", plan.Cursor ? "1" : "0", "-offset_x", (plan.WindowHandle!=0?0:plan.Bounds.X).ToString(CultureInfo.InvariantCulture),
                "-offset_y", (plan.WindowHandle!=0?0:plan.Bounds.Y).ToString(CultureInfo.InvariantCulture),
                "-video_size", $"{plan.Bounds.Width}x{plan.Bounds.Height}", "-i", plan.WindowHandle!=0?"hwnd="+plan.WindowHandle.ToString(CultureInfo.InvariantCulture):"desktop"
            });
        }
        args.AddRange(new[] { "-map", "0:v:0", "-an" });
        // YUV420 codecs require even dimensions. Preserve all selected pixels, pad only the right/bottom by at most 1.
        var videoFilters = new List<string>();
        bool scaled = OutputOptions.Size(plan.Bounds.Size, plan.Resolution, false) != plan.Bounds.Size;
        if (useDxgi && (scaled || plan.Encoder == EncoderChoice.NvencHevcLossless))
            videoFilters.AddRange(new[] { "hwdownload", "format=bgra" });
        if (scaled)
        {
            var size = OutputOptions.Size(plan.Bounds.Size, plan.Resolution, false);
            videoFilters.Add($"scale={size.Width}:{size.Height}:flags=fast_bilinear");
        }
        if (plan.Encoder == EncoderChoice.NvencHevcLossless) videoFilters.Add(HevcLossless.ColorFilter);
        if (!plan.IsLossless) videoFilters.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");
        if (videoFilters.Count > 0) args.AddRange(new[] { "-vf", string.Join(",", videoFilters) });
        switch (plan.Encoder)
        {
            case EncoderChoice.Nvenc:
                // GPU capture keeps its D3D11 texture format; profile high444p + QP0 must be confirmed
                // on the user's GPU. GDI fallback explicitly converts to YUV444P.
                args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", "p1", "-tune", "lossless",
                    "-profile:v", "high444p", "-rc", "constqp", "-qp", "0", "-bf", "0" });
                if (!useDxgi || scaled) args.AddRange(new[] { "-pix_fmt", "yuv444p" });
                break;
            case EncoderChoice.NvencHevcLossless:
                args.AddRange(HevcLossless.EncoderOptions());
                break;
            case EncoderChoice.X264Rgb:
                args.AddRange(new[] { "-c:v", "libx264rgb", "-preset", RecordingProfiles.SoftwarePreset(plan.Profile), "-crf", "0", "-pix_fmt", "bgr0", "-bf", "0" });
                break;
            case EncoderChoice.Ffv1:
                args.AddRange(new[] { "-c:v", "ffv1", "-level", "3", "-coder", "1", "-context", "1",
                    "-slicecrc", "1", "-pix_fmt", "bgr0" });
                break;
            case EncoderChoice.H264Cpu:
                args.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency",
                    "-crf", VideoEncoding.Quantizer(plan.Quality).ToString(CultureInfo.InvariantCulture),
                    "-profile:v", "high", "-pix_fmt", "yuv420p", "-bf", "0" });
                break;
            case EncoderChoice.Mpeg4Cpu:
                args.AddRange(new[] { "-c:v", "mpeg4", "-q:v", VideoEncoding.MpegQuantizer(plan.Quality).ToString(CultureInfo.InvariantCulture),
                    "-pix_fmt", "yuv420p", "-bf", "0" });
                break;
            case EncoderChoice.NvencH264:
            case EncoderChoice.NvencHevc:
                args.AddRange(NvencCompressedOptions(plan.Encoder, plan.Quality));
                break;
            case EncoderChoice.IntelH264:
            case EncoderChoice.AmdH264:
                args.AddRange(OtherHardwareOptions(plan.Encoder, plan.Quality));
                break;
            default: throw new InvalidOperationException(L10n.T("编码器未解析为具体模式。"));
        }
        if (RecordingProfiles.Threads(plan.Profile) is > 0 and var threads)
            args.AddRange(new[] { "-threads", threads.ToString(CultureInfo.InvariantCulture) });
        args.AddRange(new[] { "-f", "matroska", output });
        return args;
    }

    internal static List<string> NvencCompressedOptions(EncoderChoice encoder, CompressionQuality quality) => new()
    {
        "-c:v", encoder == EncoderChoice.NvencHevc ? "hevc_nvenc" : "h264_nvenc", "-preset", "p1", "-tune", "ull",
        "-profile:v", encoder == EncoderChoice.NvencHevc ? "main" : "high", "-rc", "constqp",
        "-qp", VideoEncoding.Quantizer(quality).ToString(CultureInfo.InvariantCulture), "-bf", "0", "-pix_fmt", "yuv420p"
    };
    internal static List<string> OtherHardwareOptions(EncoderChoice encoder, CompressionQuality quality)
    {
        string q = VideoEncoding.Quantizer(quality).ToString(CultureInfo.InvariantCulture);
        return encoder == EncoderChoice.IntelH264
            ? new() { "-c:v", "h264_qsv", "-preset", "veryfast", "-global_quality", q, "-bf", "0", "-pix_fmt", "nv12" }
            : new() { "-c:v", "h264_amf", "-quality", "speed", "-rc", "cqp", "-qp_i", q, "-qp_p", q, "-bf", "0", "-pix_fmt", "nv12" };
    }
    internal static List<string> ExportMp4(string input, string output) => new()
    {
        "-hide_banner", "-loglevel", "error", "-y", "-i", input, "-map", "0", "-c:v", "copy",
        "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4", output
    };
    internal static string GraphicsWindowSource(RecordPlan plan)
    {
        if(plan.WindowHandle==0)throw new InvalidOperationException(L10n.T("现代窗口捕获需要有效窗口句柄。"));
        return $"gfxcapture=hwnd={unchecked((ulong)plan.WindowHandle)}:capture_cursor={(plan.Cursor?1:0)}:capture_border=0:display_border=0:max_framerate={plan.Fps}:width={plan.Bounds.Width}:height={plan.Bounds.Height}:resize_mode=crop:output_fmt=bgra,hwdownload,format=bgra,fps={plan.Fps}";
    }
    public static List<string> MuxAudio(string video, string output, string? systemAudio, string? micAudio,
        float systemVolume, float micVolume, bool systemEnabled, bool micEnabled, bool mixAudio, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentException(L10n.T("视频时长无效。"));
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", video };
        var sources = new List<(int Index, float Volume, string Title)>();
        void AddSource(bool enabled, string? file, float volume, string title)
        {
            if (!enabled) return;
            if (file is not null && File.Exists(file)) args.AddRange(new[] { "-i", file });
            else args.AddRange(new[] { "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo" });
            sources.Add((sources.Count + 1, float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 1, title));
        }
        AddSource(systemEnabled, systemAudio, systemVolume, L10n.T("系统声音"));
        AddSource(micEnabled, micAudio, micVolume, L10n.T("麦克风"));
        args.AddRange(new[] { "-map", "0:v:0" });
        if (sources.Count > 0)
        {
            // Fixed format/track count makes pause segments compatible even after device loss.
            var filters = sources.Select((source, index) =>
                $"[{source.Index}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,volume={source.Volume.ToString("0.000", CultureInfo.InvariantCulture)},apad,atrim=duration={duration.ToString("0.000000", CultureInfo.InvariantCulture)}[a{index}]").ToList();
            if (mixAudio && sources.Count > 1)
            {
                filters.Add(string.Concat(sources.Select((_, index) => $"[a{index}]")) +
                    $"amix=inputs={sources.Count}:duration=longest:dropout_transition=0:normalize=1[mixed]");
                args.AddRange(new[] { "-map", "[mixed]", "-metadata:s:a:0", L10n.T("title=系统声音 + 麦克风") });
            }
            else
            {
                for (int i = 0; i < sources.Count; i++)
                    args.AddRange(new[] { "-map", $"[a{i}]", $"-metadata:s:a:{i}", "title=" + sources[i].Title });
            }
            args.AddRange(new[] { "-filter_complex", string.Join(";", filters), "-c:a", "flac", "-sample_fmt", "s32",
                "-ar", "48000", "-ac", "2", "-disposition:a:0", "default" });
        }
        // Container duration can be rounded near the last video timestamp. Bound only audio;
        // a global -t would also discard already captured video packets at that boundary.
        args.AddRange(new[] { "-c:v", "copy", "-f", "matroska", output });
        return args;
    }

    public static List<string> Concat(IReadOnlyList<string> files, string output, string manifest)
    {
        if (files.Count < 2) throw new ArgumentException(L10n.T("至少两个片段。"));
        File.WriteAllLines(manifest, files.Select(path => "file '" + path.Replace('\\', '/').Replace("'", "'\\''") + "'"), new System.Text.UTF8Encoding(false));
        return new() { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", manifest,
            "-map", "0", "-c", "copy", "-f", "matroska", output };
    }
}
