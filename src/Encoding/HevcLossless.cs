using System.Text.Json;

namespace ScreenLossless;

internal static class HevcLossless
{
    // SDR RGB -> full-range BT.709, with rounding at 10 bits before MSB-aligned NVENC input.
    // Encoding preserves these YUV samples; RGB conversion is not promised to be reversible.
    internal const string ColorFilter =
        "scale=in_range=full:out_range=full:out_color_matrix=bt709:flags=accurate_rnd+full_chroma_int+bitexact," +
        "format=yuv444p10le,scale=in_range=full:out_range=full:flags=neighbor+bitexact,format=yuv444p16le";

    internal static string[] EncoderOptions() => new[]
    {
        "-c:v", "hevc_nvenc", "-preset", "p1", "-tune", "lossless", "-profile:v", "rext",
        "-rc", "constqp", "-qp", "0", "-bf", "0", "-pix_fmt", "+yuv444p16le",
        "-color_range", "pc", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "iec61966-2-1"
    };

    internal static async Task VerifyFormatAsync(string ffprobe, string file, int width, int height)
    {
        string json = await HardwareProbe.ExecuteTextAsync(ffprobe, new[]
        {
            "-v", "error", "-select_streams", "v:0", "-show_entries",
            "stream=codec_name,profile,pix_fmt,width,height,color_range,color_space", "-of", "json", file
        }, 7000);
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() != 1) throw new InvalidDataException(L10n.T("HEVC 试编码缺少视频流。"));
        var stream = streams[0];
        string Value(string name) => stream.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        if (Value("codec_name") != "hevc" || Value("profile") != "Rext" || Value("pix_fmt") != "yuv444p10le"
            || Value("color_range") != "pc" || Value("color_space") != "bt709"
            || stream.GetProperty("width").GetInt32() != width || stream.GetProperty("height").GetInt32() != height)
            throw new InvalidDataException(L10n.T("HEVC 输出未满足 10-bit / 4:4:4 / Rext / BT.709 全范围要求，已拒绝降级。"));
    }

    internal static string[] FrameHashes(string text) => text.Split('\n')
        .Where(line => line.TrimStart().StartsWith("0,", StringComparison.Ordinal))
        .Select(line => line.Split(',')[^1].Trim()).ToArray();

    internal static async Task<EncoderProbeResult> ProbeAsync(string ffmpeg)
    {
        const EncoderChoice encoder = EncoderChoice.NvencHevcLossless;
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(ffprobe)) return new(encoder, true, false, L10n.T("缺少同目录 ffprobe.exe，无法确认 10-bit 4:4:4 输出。"));
        string directory = Path.Combine(Path.GetTempPath(), "ScreenLossless-hevc-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "probe.mkv");
            var input = new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=30", "-frames:v", "3", "-vf", ColorFilter };
            var args = new List<string> { "-y" };
            args.AddRange(input); args.AddRange(EncoderOptions()); args.AddRange(new[] { "-f", "matroska", file });
            await HardwareProbe.ExecuteTextAsync(ffmpeg, args, 12000);
            await VerifyFormatAsync(ffprobe, file, 320, 180);
            // Compare decoded samples, rather than accepting QP=0 or a successful initialization alone.
            var referenceArgs = input.Concat(new[] { "-pix_fmt", "yuv444p10le", "-f", "framemd5", "-" });
            var expected = FrameHashes(await HardwareProbe.ExecuteTextAsync(ffmpeg, referenceArgs, 12000));
            var actual = FrameHashes(await HardwareProbe.ExecuteTextAsync(ffmpeg, new[]
                { "-v", "error", "-i", file, "-pix_fmt", "yuv444p10le", "-f", "framemd5", "-" }, 12000));
            if (expected.Length != 3 || !expected.SequenceEqual(actual))
                throw new InvalidDataException(L10n.T("HEVC 解码数据与编码前的 10-bit YUV 不一致，已禁用该模式。"));
            return new(encoder, true, true, L10n.T("320×180 · 3 帧：HEVC Rext / 10-bit / 4:4:4 / BT.709 全范围通过；解码 YUV 逐像素一致。RGB 转换与 HDR 不在此保证范围内。"));
        }
        catch (Exception e) { return new(encoder, true, false, e.Message.Trim()); }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
