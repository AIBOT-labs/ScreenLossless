namespace ScreenLossless;

internal sealed record RecordingLimit(int Minutes)
{
    internal static readonly int[] Presets = { 0, 1, 5, 10, 15, 30, 60, 120, 240 };
    public override string ToString() => Minutes == 0 ? L10n.T("不限制 · 手动停止") : L10n.F($"{Minutes} 分钟后自动保存");
    internal TimeSpan Duration => TimeSpan.FromMinutes(Minutes);
    internal bool Reached(TimeSpan recorded) => Minutes > 0 && recorded >= Duration;
    internal TimeSpan Remaining(TimeSpan recorded) => recorded >= Duration ? TimeSpan.Zero : Duration - recorded;
    internal static string Format(TimeSpan duration) => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
}