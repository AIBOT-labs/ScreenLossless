using System.Drawing;
using System.Globalization;

namespace ScreenLossless;
internal static class OutputOptions
{
    internal static string[] ResolutionNames => new[] { L10n.T("原始尺寸（100%）"), L10n.T("720p · 不超过 1280×720"), L10n.T("1080p · 不超过 1920×1080"), L10n.T("缩小至 75%"), L10n.T("缩小至 50%") };
    internal static string[] ContainerNames => new[] { L10n.T("MKV · 无损音频"), L10n.T("MP4 · AAC 音频") };
    internal static readonly int[] SegmentMinutes = { 0, 5, 10, 20, 30, 60 };
    internal static readonly int[] SegmentGigabytes = { 0, 1, 2, 4 };
    internal static Size Size(Size source, OutputResolution resolution, bool even)
    {
        double ratio = resolution switch
        {
            OutputResolution.Hd720 => Math.Min(1, Math.Min(1280d / source.Width, 720d / source.Height)),
            OutputResolution.FullHd1080 => Math.Min(1, Math.Min(1920d / source.Width, 1080d / source.Height)),
            OutputResolution.Percent75 => .75, OutputResolution.Percent50 => .5, _ => 1
        };
        int w = Math.Max(2, (int)Math.Floor(source.Width * ratio)), h = Math.Max(2, (int)Math.Floor(source.Height * ratio));
        if (even) { w += w % 2; h += h % 2; }
        return new Size(w, h);
    }
    internal static void Validate(RecordPlan plan)
    {
        if (!Enum.IsDefined(plan.Resolution) || !Enum.IsDefined(plan.Container)) throw new ArgumentException(L10n.T("输出设置无效。"));
        if (!SegmentMinutes.Contains(plan.SegmentMinutes) || !SegmentGigabytes.Contains(plan.SegmentGigabytes)) throw new ArgumentException(L10n.T("自动分段设置无效。"));
        if (plan.Container == OutputContainer.Mp4 && plan.Encoder == EncoderChoice.Ffv1)
            throw new InvalidOperationException(L10n.T("FFV1 不支持 MP4，请选择 MKV 或更换 H.264 编码。"));
    }
    internal static bool SegmentDue(TimeSpan elapsed, int minutes, long videoBytes, int gigabytes, long audioBytes) =>
        (minutes > 0 && elapsed >= TimeSpan.FromMinutes(minutes)) ||
        (gigabytes > 0 && videoBytes >= gigabytes * 1024L * 1024 * 1024) || audioBytes >= 512L * 1024 * 1024;
}

internal sealed record PerformanceSnapshot(double Fps, double Speed, long Dropped, bool HasSpeed)
{
    internal string Text => L10n.F($"编码 {Fps:0.0} FPS · ") + (HasSpeed ? L10n.F($"速度 {Speed:0.00}× · ") : "") + L10n.F($"本段报告丢帧 {Dropped}");
    internal bool Slow => HasSpeed && Speed < .9;
}
internal sealed class RecordingPerformance
{
    private readonly object _gate = new();
    private double _fps, _speed;
    private long _dropped;
    private bool _hasSpeed;
    internal PerformanceSnapshot Snapshot { get { lock (_gate) return new(_fps, _speed, _dropped, _hasSpeed); } }
    internal void Reset() { lock (_gate) { _fps = _speed = 0; _dropped = 0; _hasSpeed = false; } }
    internal void Observe(string line)
    {
        int separator = line.IndexOf('='); if (separator < 0) return;
        var key=line.AsSpan(0,separator);
        if(!key.SequenceEqual("fps")&&!key.SequenceEqual("speed")&&!key.SequenceEqual("drop_frames"))return;
        var value=line.AsSpan(separator+1).Trim().TrimEnd('x');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number < 0) return;
        lock (_gate)
        {
            if (key.SequenceEqual("fps")) _fps = number;
            if (key.SequenceEqual("speed")) { _speed = number; _hasSpeed = true; }
            if (key.SequenceEqual("drop_frames")) _dropped = (long)Math.Min(number, long.MaxValue);
        }
    }
}
