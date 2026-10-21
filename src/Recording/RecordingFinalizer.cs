using System.Globalization;
using System.Text.Json;
using NAudio.Wave;

namespace ScreenLossless;

internal sealed record RecordingSegment(string Video, string? SystemAudio, string? MicrophoneAudio, bool Continuous = false);

internal sealed class RecordingManifest
{
    public string TargetFile { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool SystemAudio { get; set; }
    public bool Microphone { get; set; }
    public bool MixAudio { get; set; } = true;
    public float SystemVolume { get; set; } = 1;
    public float MicrophoneVolume { get; set; } = 1;
    public string? CompletedFile { get; set; }
    public List<RecordingSegment> Segments { get; set; } = new();

    public void Save(string directory)
    {
        string file = Path.Combine(directory, "session.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
    public static string ResolveFile(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name is "." or ".." || name.Contains(':'))
            throw new InvalidDataException(L10n.T("恢复清单包含无效文件名。"));
        return Path.Combine(directory, name);
    }
    public static RecordingManifest Load(string directory)
    {
        string file = Path.Combine(directory, "session.json");
        RecordingManifest manifest;
        if (File.Exists(file))
            manifest = JsonSerializer.Deserialize<RecordingManifest>(File.ReadAllText(file)) ?? throw new InvalidDataException(L10n.T("恢复清单为空。"));
        else
        {
            // Also recognize legacy work folders without a manifest.
            manifest = new RecordingManifest { CreatedAt = Directory.GetCreationTime(directory), TargetFile = "Recovered_" + Path.GetFileName(directory) + ".mkv", MixAudio = false };
            foreach (string video in Directory.EnumerateFiles(directory, "video-*.mkv").Where(p => !Path.GetFileName(p).Contains("-part-")).OrderBy(p => p, StringComparer.Ordinal))
            {
                string suffix = Path.GetFileNameWithoutExtension(video)[6..];
                string system = $"audio-system-{suffix}.wav", mic = $"audio-mic-{suffix}.wav";
                bool hasSystem = File.Exists(Path.Combine(directory, system)), hasMic = File.Exists(Path.Combine(directory, mic));
                manifest.SystemAudio |= hasSystem;
                manifest.Microphone |= hasMic;
                manifest.Segments.Add(new(Path.GetFileName(video), hasSystem ? system : null, hasMic ? mic : null));
            }
        }
        ResolveFile(directory, manifest.TargetFile);
        if (manifest.Segments is null || manifest.Segments.Count == 0) throw new InvalidDataException(L10n.T("没有可恢复的视频片段。"));
        foreach (var segment in manifest.Segments)
        {
            ResolveFile(directory, segment.Video);
            if (segment.SystemAudio is not null) ResolveFile(directory, segment.SystemAudio);
            if (segment.MicrophoneAudio is not null) ResolveFile(directory, segment.MicrophoneAudio);
        }
        return manifest;
    }
}

internal sealed record RecoverableRecording(string Directory, RecordingManifest Manifest, string? Error = null,long Bytes=0)
{
    public override string ToString() => L10n.F($"{Manifest.CreatedAt:yyyy-MM-dd HH:mm:ss}  ·  {Manifest.Segments.Count} 个片段  ·  {Manifest.TargetFile}");
}

internal static class RecordingFinalizer
{
    public static List<RecoverableRecording> Discover(string outputFolder,bool includeCompleted=false)
    {
        var result = new List<RecoverableRecording>();
        string root = Path.Combine(outputFolder, ".screenlossless-work");
        if (!Directory.Exists(root)) return result;
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            // Do not follow directory junctions outside the recording folder.
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            try
            {
                var manifest = RecordingManifest.Load(directory);
                if (includeCompleted||manifest.CompletedFile is null) result.Add(new(directory,manifest,null,includeCompleted?RecoveryStorage.Size(directory):0));
            }
            catch (Exception e)
            {
                result.Add(new(directory, new RecordingManifest { TargetFile = Path.GetFileName(directory), CreatedAt = Directory.GetCreationTime(directory) }, e.Message));
            }
        }
        return result.OrderByDescending(r => r.Manifest.CreatedAt).ToList();
    }

    private static string? ReadableAudio(string directory, string? name, Action<string> log)
    {
        if (name is null) return null;
        string file = RecordingManifest.ResolveFile(directory, name);
        try
        {
            using var reader = new WaveFileReader(file);
            if (reader.Length > 0) return file;
        }
        catch (Exception e) { log(L10n.F($"音频 {name} 不可读取，补静音：{e.Message}")); }
        return null;
    }

    private static async Task<string> PrepareVideoAsync(Capabilities caps, string directory, RecordingSegment segment, int run, CancellationToken token)
    {
        var files = ContinuousMedia.VideoFiles(directory, segment);
        if (files.Length == 0 || files.Any(f => !File.Exists(f) || new FileInfo(f).Length == 0))
            throw new InvalidDataException(L10n.T("连续视频素材缺失或为空：") + segment.Video);
        if (files.Length == 1) return files[0];
        string joined = Path.Combine(directory, $"joined-video-{run:000}.mkv");
        await MediaProcess.RunAsync(caps.Ffmpeg, FfmpegArguments.Concat(files, joined, Path.Combine(directory, $"video-parts-{run:000}.txt")), 0, null, token);
        return joined;
    }
    private static async Task<string?> PrepareAudioAsync(Capabilities caps, string directory, string? name, bool continuous, Action<string> log, CancellationToken token)
    {
        if (!continuous || name is null) return ReadableAudio(directory, name, log);
        var files = ContinuousMedia.Parts(directory, name, ".wav", true);
        if (files.Length == 0) return null;
        var readable = new List<string>();
        foreach (var file in files)
        {
            // A damaged middle chunk must not silently shift all subsequent audio.
            using var reader = new WaveFileReader(file);
            if (reader.Length > 0) readable.Add(file);
        }
        if (readable.Count == 0) return null;
        if (readable.Count == 1) return readable[0];
        string stem = Path.GetFileNameWithoutExtension(name);
        string list = Path.Combine(directory, stem + "-parts.txt");
        File.WriteAllLines(list, readable.Select(f => "file '" + f.Replace('\\', '/').Replace("'", "'\\''") + "'"), new System.Text.UTF8Encoding(false));
        string joined = Path.Combine(directory, "joined-" + stem + ".mka");
        await MediaProcess.RunAsync(caps.Ffmpeg, new[] { "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", list,
            "-c:a", "flac", "-sample_fmt", "s32", "-f", "matroska", joined }, 0, null, token);
        return joined;
    }

    private static async Task<(double Duration, string Signature)> InspectVideoAsync(Capabilities caps, string video, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(caps.Ffprobe))
        {
            string json = await MediaProcess.ReadTextAsync(caps.Ffprobe, new[] { "-v", "error", "-show_entries",
                "stream=codec_name,codec_type,width,height,pix_fmt,profile,sample_aspect_ratio:format=duration", "-of", "json", video }, token);
            using var document = JsonDocument.Parse(json);
            var stream = document.RootElement.GetProperty("streams").EnumerateArray().First(s => s.GetProperty("codec_type").GetString() == "video");
            double duration = double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            // gdigrab can report 30000/1001 on one segment and 30/1 on another.
            // Timestamped Matroska segments can be concatenated without resampling frames.
            string signature=string.Join("|",new[]{"codec_name","width","height","pix_fmt","profile","sample_aspect_ratio"}
                .Select(name=>stream.TryGetProperty(name,out var value)?value.ToString():""));
            return (duration, signature);
        }
        double maximum=0;
        await MediaProcess.RunAsync(caps.Ffmpeg,new[]{"-v","error","-i",video,"-map","0:v:0","-c:v","copy","-f","null","-"},0,null,token,
            mediaTime:seconds=>maximum=Math.Max(maximum,seconds));
        return (maximum,"");
    }

    public static async Task<string> ExportAsync(Capabilities caps, string directory, RecordingManifest manifest, Action<string> log, Action<double>? progress = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!caps.Available) throw new InvalidOperationException(L10n.T("请先配置 FFmpeg。"));
        string outputFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(directory)))!;
        string target = RecordingManifest.ResolveFile(outputFolder, manifest.TargetFile);
        string stem = Path.GetFileNameWithoutExtension(target);
        string extension = Path.GetExtension(target);
        bool mp4 = extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        for (int suffix = 2; File.Exists(target); suffix++) target = Path.Combine(outputFolder, $"{stem}_{suffix}{extension}");
        var completed = new List<string>();
        string? signature = null;
        double totalDuration=0;int units=manifest.Segments.Count+(mp4?2:1);
        var progressClock=System.Diagnostics.Stopwatch.StartNew();
        double reported=0;
        void Report(double value,bool force=false)
        {
            value=Math.Max(reported,Math.Clamp(value,0,1));
            if(!force&&progressClock.ElapsedMilliseconds<100)return;
            reported=value;progressClock.Restart();progress?.Invoke(value);
        }
        Report(0,true);
        foreach (var segment in manifest.Segments)
        {
            token.ThrowIfCancellationRequested();
            string video = await PrepareVideoAsync(caps, directory, segment, completed.Count + 1, token);
            if (!File.Exists(video) || new FileInfo(video).Length == 0) throw new InvalidDataException(L10n.T("视频片段缺失或为空：") + segment.Video);
            var info = await InspectVideoAsync(caps, video,token);
            if (!double.IsFinite(info.Duration) || info.Duration <= 0) throw new InvalidDataException(L10n.T("无法读取片段时长：") + segment.Video);
            if (signature is not null && signature != info.Signature) throw new InvalidDataException(L10n.T("视频片段编码或尺寸不同，无法直接无损拼接。原始文件已保留。"));
            signature = info.Signature;
            string muxed = Path.Combine(directory, $"mux-{completed.Count + 1:000}.mkv");
            log(L10n.F($"整理音视频 {completed.Count + 1}/{manifest.Segments.Count}…"));
            totalDuration+=info.Duration;
            int unit=completed.Count;
            await MediaProcess.RunAsync(caps.Ffmpeg, FfmpegArguments.MuxAudio(video, muxed,
                await PrepareAudioAsync(caps, directory, segment.SystemAudio, segment.Continuous, log, token),
                await PrepareAudioAsync(caps, directory, segment.MicrophoneAudio, segment.Continuous, log, token),
                manifest.SystemVolume, manifest.MicrophoneVolume, manifest.SystemAudio, manifest.Microphone, manifest.MixAudio, info.Duration),info.Duration,p=>Report((unit+p)/units,p>=1),token);
            completed.Add(muxed);
        }
        if (completed.Count == 0) throw new InvalidOperationException(L10n.T("没有可合成的片段。"));
        string staged = Path.Combine(directory, "completed.mkv");
        if (completed.Count == 1)
        {
            await using var source=new FileStream(completed[0],FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,true);
            await using var destination=new FileStream(staged,FileMode.Create,FileAccess.Write,FileShare.None,1024*1024,true);
            byte[] buffer=new byte[1024*1024];long written=0;int count;
            while((count=await source.ReadAsync(buffer,token))>0)
            {
                await destination.WriteAsync(buffer.AsMemory(0,count),token);written+=count;
                Report((completed.Count+(double)written/source.Length)/units,written==source.Length);
            }
            await destination.FlushAsync(token);
        }
        else
        {
            log(L10n.T("正在合并暂停前后的片段…"));
            await MediaProcess.RunAsync(caps.Ffmpeg,FfmpegArguments.Concat(completed, staged, Path.Combine(directory, "concat.txt")),totalDuration,p=>Report((completed.Count+p)/units,p>=1),token);
        }
        if (!File.Exists(staged) || new FileInfo(staged).Length == 0) throw new InvalidDataException(L10n.T("合成输出为空。"));
        if (mp4)
        {
            log(L10n.T("正在导出 MP4：视频直接复制，音频转换为 AAC…"));
            string converted = Path.Combine(directory, "completed.mp4");
            await MediaProcess.RunAsync(caps.Ffmpeg, FfmpegArguments.ExportMp4(staged, converted), totalDuration,
                p => Report((completed.Count + 1 + p) / units, p >= 1), token);
            if (!File.Exists(converted) || new FileInfo(converted).Length == 0) throw new InvalidDataException(L10n.T("MP4 输出为空，素材已保留。"));
            staged = converted;
        }
        token.ThrowIfCancellationRequested();
        File.Move(staged, target); // Publish only the completed file; never overwrite an existing recording.
        manifest.CompletedFile = Path.GetFileName(target);
        try { manifest.Save(directory); }
        catch (Exception e) { log(L10n.T("文件已保存，但恢复清单更新失败：") + e.Message); }
        Report(1,true);
        return target;
    }
}
