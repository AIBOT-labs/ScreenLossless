namespace ScreenLossless;

internal readonly record struct AudioCorrection(long SilenceFrames, int OutputFrames);
internal static class AudioClockCorrection
{
    internal const int SampleRate = 48000;
    // Keep WASAPI's sample clock close to the same monotonic clock as video.
    // A 20 ms deadband tolerates callback jitter; <=0.5% resampling corrects drift
    // gradually. A genuine missing interval (>100 ms) is preserved as silence.
    internal static AudioCorrection Plan(int frames, long writtenFrames, TimeSpan elapsed)
    {
        if (frames < 1 || frames > 48000) throw new ArgumentOutOfRangeException(nameof(frames));
        long expected = Math.Max(0, (long)(elapsed.TotalSeconds * SampleRate));
        long gap = expected - frames - writtenFrames;
        long silence = gap > SampleRate / 10 ? gap : 0;
        long error = expected - (writtenFrames + silence + frames);
        int adjustment = Math.Abs(error) > SampleRate / 50
            ? (int)Math.Clamp(error, -(long)Math.Ceiling(frames * .005), (long)Math.Ceiling(frames * .005)) : 0;
        return new(silence, Math.Max(1, frames + adjustment));
    }
    internal static void ResampleStereo(float[] input, int frames, float[] output, int targetFrames)
    {
        if (frames == targetFrames) { Array.Copy(input, output, frames * 2); return; }
        for (int f = 0; f < targetFrames; f++)
        {
            double position = targetFrames == 1 ? 0 : f * (frames - 1d) / (targetFrames - 1);
            int left = (int)position, right = Math.Min(frames - 1, left + 1);
            float fraction = (float)(position - left);
            for (int channel = 0; channel < 2; channel++)
                output[f * 2 + channel] = input[left * 2 + channel] + (input[right * 2 + channel] - input[left * 2 + channel]) * fraction;
        }
    }
}
