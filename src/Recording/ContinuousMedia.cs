using System.Globalization;

namespace ScreenLossless;

// One manifest entry per uninterrupted run; capture keeps running across file boundaries.
internal static class ContinuousMedia
{
    internal static int NextSegmentIndex(string directory, int minimum)
    {
        var names = Directory.EnumerateFiles(directory).Select(Path.GetFileNameWithoutExtension).ToArray();
        for (int index = Math.Max(1, minimum); ; index = checked(index + 1))
        {
            string suffix = index.ToString("000", CultureInfo.InvariantCulture);
            string[] stems = { "video-" + suffix, "audio-system-" + suffix, "audio-mic-" + suffix };
            if (!stems.Any(stem => names.Any(name => string.Equals(name, stem, StringComparison.OrdinalIgnoreCase) ||
                name!.StartsWith(stem + "-part-", StringComparison.OrdinalIgnoreCase)))) return index;
        }
    }
    internal static string Pattern(string firstFile) => Path.Combine(Path.GetDirectoryName(firstFile)!,
        Path.GetFileNameWithoutExtension(firstFile) + "-part-%06d.mkv");
    internal static string[] VideoFiles(string directory, RecordingSegment segment) => segment.Continuous
        ? Parts(directory, segment.Video, ".mkv", false)
        : new[] { RecordingManifest.ResolveFile(directory, segment.Video) };
    internal static string[] Parts(string directory, string name, string extension, bool includeFirst)
    {
        RecordingManifest.ResolveFile(directory, name);
        string stem = Path.GetFileNameWithoutExtension(name) + "-part-";
        var files = Directory.EnumerateFiles(directory, stem + "*" + extension)
            .Where(f => Path.GetFileNameWithoutExtension(f) is var n && n.Length == stem.Length + 6
                && n[stem.Length..].All(char.IsAsciiDigit))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        for (int i=0; i<files.Count; i++)
        {
            int number=int.Parse(Path.GetFileNameWithoutExtension(files[i])[stem.Length..],CultureInfo.InvariantCulture);
            if(number!=i+(includeFirst?1:0))throw new InvalidDataException(L10n.T("连续素材中间缺失，保留原始文件：")+name);
        }
        if(includeFirst && files.Count>0 && !File.Exists(Path.Combine(directory,name)))
            throw new InvalidDataException(L10n.T("连续音频首段缺失：")+name);
        if (includeFirst && File.Exists(Path.Combine(directory, name))) files.Insert(0, Path.Combine(directory, name));
        return files.ToArray();
    }
    internal static List<string> Capture(RecordPlan plan, string firstFile, double seconds = 10)
    {
        if (!double.IsFinite(seconds) || seconds < 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        var args = FfmpegArguments.Capture(plan, firstFile);
        args.RemoveRange(args.Count - 3, 3);
        // I frames alone can still reference an older GOP; request independently decodable IDRs.
        if (plan.Encoder is EncoderChoice.IntelH264 or EncoderChoice.AmdH264)
            args.AddRange(new[] { "-forced_idr", "1" });
        else if (plan.Encoder is EncoderChoice.Nvenc or EncoderChoice.NvencH264 or EncoderChoice.NvencHevc or EncoderChoice.NvencHevcLossless or EncoderChoice.H264Cpu or EncoderChoice.X264Rgb)
            args.AddRange(new[] { "-forced-idr", "1" });
        string interval = seconds.ToString("0.######", CultureInfo.InvariantCulture);
        args.AddRange(new[] { "-force_key_frames", "expr:gte(t,n_forced*" + interval + ")",
            "-f", "segment", "-segment_format", "matroska", "-segment_time", interval,
            "-segment_time_delta", (0.5 / plan.Fps).ToString("0.########", CultureInfo.InvariantCulture),
            "-reset_timestamps", "1", Pattern(firstFile) });
        return args;
    }
}
// FFmpeg closes each numbered video part before opening the next. Account for
// closed files once; steady-state polling reads only the current and next paths.
internal sealed class ContinuousVideoSize
{
    private readonly string _prefix;
    private int _index;
    private long _closed;
    private string _current,_next;
    internal ContinuousVideoSize(string firstFile)
    {
        _prefix=Path.Combine(Path.GetDirectoryName(firstFile)!,Path.GetFileNameWithoutExtension(firstFile)+"-part-");
        _current=Part(0);_next=Part(1);
    }
    private string Part(int index)=>_prefix+index.ToString("000000",CultureInfo.InvariantCulture)+".mkv";
    internal long ReadBytes()
    {
        while(File.Exists(_next))
        {
            _closed=checked(_closed+new FileInfo(_current).Length);
            _current=_next;_next=Part(++_index+1);
        }
        var current=new FileInfo(_current);
        return checked(_closed+(current.Exists?current.Length:0));
    }
}
internal sealed class AdaptivePerformance
{
    private TimeSpan? _slowSince;
    private TimeSpan _nextChange;
    internal int? NextFps(RecordPlan plan, PerformanceSnapshot performance, TimeSpan duration, TimeSpan runDuration)
    {
        if (!plan.AutoPerformance || plan.IsLossless || runDuration < TimeSpan.FromSeconds(15)
            || !performance.HasSpeed || performance.Speed >= .85)
        { _slowSince = null; return null; }
        _slowSince ??= duration;
        if (duration < _nextChange || duration - _slowSince < TimeSpan.FromSeconds(15)) return null;
        int next = plan.Fps > 30 ? 30 : plan.Fps > 24 ? 24 : plan.Fps > 15 ? 15 : plan.Fps > 10 ? 10 : plan.Fps;
        if (next == plan.Fps) return null;
        _slowSince = null; _nextChange = duration + TimeSpan.FromSeconds(60);
        return next;
    }
    internal void Reset() { _slowSince = null; _nextChange = TimeSpan.Zero; }
}
