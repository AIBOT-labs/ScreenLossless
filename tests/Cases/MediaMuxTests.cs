using NAudio.Wave;

namespace ScreenLossless;

internal static partial class RegressionTests
{
    private static async Task TestMuxFramePreservationAsync()
    {
        string folder = Path.Combine(_root, "mux-frame-boundary"); Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "source.mkv");
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=30",
            "-frames:v", "3", "-c:v", "libx264rgb", "-crf", "0", source);
        var expected = await Hashes(source);
        foreach (bool audio in new[] { false, true })
        {
            string file = Path.Combine(folder, audio ? "with-audio.mkv" : "video-only.mkv");
            // Last video timestamp is 0.067 s; rounded/understated audio bounds must not trim it.
            await Run(FfmpegArguments.MuxAudio(source, file, null, null, 1, 1, audio, false, true, .066).ToArray());
            Check(expected.Length == 3 && expected.SequenceEqual(await Hashes(file)),
                "Rounded audio duration preserves all video frames and pixels, audio=" + audio);
            if (audio)
            {
                string wav = Path.Combine(folder, "bounded-audio.wav");
                await Run("-v", "error", "-y", "-i", file, "-map", "0:a:0", "-c:a", "pcm_f32le", wav);
                using var reader = new WaveFileReader(wav);
                Check(Math.Abs(reader.TotalTime.TotalSeconds - .066) < .00003,
                    "Padded audio stops at its own duration without extending indefinitely");
            }
        }
    }
}
