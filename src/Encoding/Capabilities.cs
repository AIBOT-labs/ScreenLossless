using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace ScreenLossless;

internal sealed record EncoderProbeResult(EncoderChoice Encoder, bool Listed, bool Available, string Detail);

internal sealed class Capabilities
{
    public string Ffmpeg { get; init; } = "";
    public string Ffprobe { get; init; } = "";
    public bool Available { get; init; }
    public bool Nvenc { get; init; }
    public bool NvencListed { get; init; }
    public string NvencError { get; init; } = "";
    public bool H264Cpu { get; init; }
    public bool Mpeg4Cpu { get; init; }
    public bool NvencH264 { get; init; }
    public bool NvencHevc { get; init; }
    public bool NvencHevcLossless { get; init; }
    public bool IntelH264 { get; init; }
    public bool AmdH264 { get; init; }
    public bool Dxgi { get; init; }
    public bool GfxCapture {get;init;}
    public bool X264Rgb { get; init; }
    public bool Ffv1 { get; init; }
    public IReadOnlyList<EncoderProbeResult> HardwareTests { get; init; } = Array.Empty<EncoderProbeResult>();
    public string Error { get; init; } = "";
    public bool AnyEncoder => Nvenc || X264Rgb || Ffv1 || H264Cpu || Mpeg4Cpu || NvencH264 || NvencHevc || NvencHevcLossless || IntelH264 || AmdH264;
}

internal static class HardwareProbe
{
    public static string LocateFfmpeg(string storedPath)
    {
        bool managed=BundledTools.IsManagedPath(storedPath);
        if (!managed&&!string.IsNullOrWhiteSpace(storedPath)&&File.Exists(storedPath))return storedPath;
        string dir = AppContext.BaseDirectory;
        string sidecar = Path.Combine(dir, "ffmpeg.exe");
        if (File.Exists(sidecar)) return sidecar;
        if(BundledTools.Locate() is {} embedded)return embedded;
        if(managed&&File.Exists(storedPath))return storedPath;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string segment in path.Split(Path.PathSeparator))
        {
            try { var item = Path.Combine(segment.Trim('"'), "ffmpeg.exe"); if (File.Exists(item)) return item; }
            catch { }
        }
        return "";
    }
    public static async Task<Capabilities> ProbeAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new Capabilities { Error = L10n.T("未找到 FFmpeg。请在设置里选择 ffmpeg.exe。") };
        try
        {
            var encoders = await ExecuteTextAsync(path, new[] { "-hide_banner", "-encoders" }, 7000);
            var filters = await ExecuteTextAsync(path, new[] { "-hide_banner", "-filters" }, 7000);
            string ffprobe = Path.Combine(Path.GetDirectoryName(path)!, "ffprobe.exe");
            // Probe sequentially: older drivers limit simultaneous encoder sessions.
            // Tiny 64x64 pictures can fail hardware minimum dimensions despite a usable GPU.
            var tests = new List<EncoderProbeResult>();
            foreach (var encoder in new[] { EncoderChoice.Nvenc, EncoderChoice.NvencH264, EncoderChoice.NvencHevc, EncoderChoice.IntelH264, EncoderChoice.AmdH264, EncoderChoice.NvencHevcLossless })
                tests.Add(await ProbeHardwareAsync(path, encoders, encoder));
            bool Supported(EncoderChoice encoder) => tests.Any(t => t.Encoder == encoder && t.Available);
            return new Capabilities
            {
                Ffmpeg = path,
                Ffprobe = File.Exists(ffprobe) ? ffprobe : "",
                Available = true,
                Nvenc = Supported(EncoderChoice.Nvenc), NvencListed = tests[0].Listed, NvencError = tests[0].Available ? "" : tests[0].Detail, HardwareTests = tests,
                H264Cpu = HasEncoder(encoders, "libx264"), Mpeg4Cpu = HasEncoder(encoders, "mpeg4"),
                NvencH264 = Supported(EncoderChoice.NvencH264), NvencHevc = Supported(EncoderChoice.NvencHevc), NvencHevcLossless = Supported(EncoderChoice.NvencHevcLossless), IntelH264 = Supported(EncoderChoice.IntelH264), AmdH264 = Supported(EncoderChoice.AmdH264),
                Dxgi = filters.Contains("ddagrab", StringComparison.OrdinalIgnoreCase),
                GfxCapture=filters.Contains("gfxcapture",StringComparison.OrdinalIgnoreCase),
                X264Rgb = encoders.Contains("libx264rgb", StringComparison.OrdinalIgnoreCase),
                Ffv1 = encoders.Contains("ffv1", StringComparison.OrdinalIgnoreCase)
            };
        }
        catch (Exception e) { return new Capabilities { Ffmpeg = path, Error = L10n.T("FFmpeg 检测失败：") + e.Message }; }
    }
    private static bool HasEncoder(string listing, string name) => listing.Split('\n').Any(line =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault() == name);
    internal static async Task<EncoderProbeResult> ProbeHardwareAsync(string path, string listing, EncoderChoice encoder)
    {
        string name = encoder switch { EncoderChoice.Nvenc or EncoderChoice.NvencH264 => "h264_nvenc",
            EncoderChoice.NvencHevc or EncoderChoice.NvencHevcLossless => "hevc_nvenc", EncoderChoice.IntelH264 => "h264_qsv", EncoderChoice.AmdH264 => "h264_amf",
            _ => throw new ArgumentOutOfRangeException(nameof(encoder)) };
        bool listed = HasEncoder(listing, name);
        if (!listed) return new(encoder, false, false, L10n.T("此 FFmpeg 未包含该编码器。"));
        if (encoder == EncoderChoice.NvencHevcLossless) return await HevcLossless.ProbeAsync(path);
        try
        {
            var args = new List<string> { "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=30", "-frames:v", "3" };
            args.AddRange(encoder == EncoderChoice.Nvenc
                ? new[] { "-c:v", "h264_nvenc", "-preset", "p1", "-tune", "lossless", "-profile:v", "high444p", "-rc", "constqp", "-qp", "0", "-bf", "0", "-pix_fmt", "yuv444p" }
                : encoder is EncoderChoice.IntelH264 or EncoderChoice.AmdH264
                    ? FfmpegArguments.OtherHardwareOptions(encoder, CompressionQuality.Balanced)
                    : FfmpegArguments.NvencCompressedOptions(encoder, CompressionQuality.Balanced));
            args.AddRange(new[] { "-f", "null", "-" });
            await ExecuteTextAsync(path, args, 12000);
            return new(encoder, true, true, L10n.T("320×180 · 30 FPS · 3 帧试编码通过；实际尺寸仍会在开始录制时验证。"));
        }
        catch (Exception e) { return new(encoder, true, false, e.Message.Trim()); }
    }
    public static EncoderChoice ResolveEncoder(EncoderChoice requested, RecordingPurpose purpose, Capabilities caps, RecordingProfile profile = RecordingProfile.Custom)
    {
        if (profile == RecordingProfile.Low && caps.Mpeg4Cpu) return EncoderChoice.Mpeg4Cpu;
        if (profile == RecordingProfile.Balanced && caps.NvencH264) return EncoderChoice.NvencH264;
        if (profile == RecordingProfile.Balanced && caps.IntelH264) return EncoderChoice.IntelH264;
        if (profile == RecordingProfile.Balanced && caps.AmdH264) return EncoderChoice.AmdH264;
        if (profile is RecordingProfile.Low or RecordingProfile.Cpu or RecordingProfile.Balanced)
        {
            var cpu = VideoEncoding.CpuFallback(caps, false);
            return cpu != EncoderChoice.Auto ? cpu : VideoEncoding.CpuFallback(caps, true);
        }
        if (requested != EncoderChoice.Auto) return requested;
        if (purpose == RecordingPurpose.Game && caps.Nvenc) return EncoderChoice.Nvenc;
        if (caps.X264Rgb) return EncoderChoice.X264Rgb;
        if (caps.Ffv1) return EncoderChoice.Ffv1;
        if (caps.Nvenc) return EncoderChoice.Nvenc;
        if (caps.NvencHevcLossless) return EncoderChoice.NvencHevcLossless;
        return EncoderChoice.Auto;
    }
    public static int ResolveFps(int requested, MonitorInfo? monitor, RecordingPurpose purpose, string adapter)
    {
        if (requested != 0) return requested;
        if (purpose == RecordingPurpose.Desktop) return 30;
        if (adapter.Contains("1050 Ti", StringComparison.OrdinalIgnoreCase)
            && (monitor?.Bounds.Width ?? 1920) >= 2560) return 30;
        return 60;
    }
    public static CaptureBackend ResolveBackend(bool preferred, RecordingScope scope, EncoderChoice encoder, MonitorInfo? monitor, Rectangle target, Capabilities caps,bool modernWindow=true)
    {
        if(scope==RecordingScope.Window)return modernWindow&&caps.GfxCapture&&OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)?CaptureBackend.WindowsGraphics:CaptureBackend.Gdi;
        if (!preferred || !caps.Dxgi || encoder is not (EncoderChoice.Nvenc or EncoderChoice.NvencHevcLossless) || monitor is null || !monitor.Primary || !monitor.Name.EndsWith("DISPLAY1", StringComparison.OrdinalIgnoreCase))
            return CaptureBackend.Gdi;
        return monitor.Bounds.Contains(target) ? CaptureBackend.Dxgi : CaptureBackend.Gdi;
    }
    public static async Task<string> ExecuteTextAsync(string exe, IEnumerable<string> args, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = new Process { StartInfo = psi };
        if (!proc.Start()) throw new InvalidOperationException(L10n.T("无法启动 FFmpeg。 "));
        var output = proc.StandardOutput.ReadToEndAsync();
        var error = proc.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeoutMs);
        try { await proc.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(L10n.T("等待 FFmpeg 超时。"));
        }
        string combined = (await output) + "\n" + (await error);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(combined.Trim().Length > 2000 ? combined[^2000..] : combined.Trim());
        return combined;
    }
}
