namespace ScreenLossless;

internal static class RecordingProfiles
{
    // Keep the original FPS indices stable for existing UI users and saved values.
    internal static readonly int[] FpsValues = { 0, 30, 60, 120, 15, 24 };
    internal static string[] Names => new[] {
        L10n.T("自定义 · 保留当前设置"), L10n.T("高配 · 60 FPS"), L10n.T("中配 · 30 FPS"), L10n.T("低配 · 15 FPS"), L10n.T("纯 CPU · 30 FPS")
    };
    internal static bool RequiresCpu(RecordingProfile profile) => profile is RecordingProfile.Low or RecordingProfile.Cpu;
    internal static string SoftwarePreset(RecordingProfile profile) => profile == RecordingProfile.High ? "superfast" : "ultrafast";
    internal static int Threads(RecordingProfile profile) => profile == RecordingProfile.Low ? Math.Clamp(Environment.ProcessorCount - 1, 1, 4) : 0;
    internal static void Apply(AppSettings settings, RecordingProfile profile)
    {
        settings.Profile = Enum.IsDefined(profile) ? profile : RecordingProfile.Custom;
        if (settings.Profile == RecordingProfile.Custom) return;
        settings.Fps = settings.Profile == RecordingProfile.High ? 60 : settings.Profile == RecordingProfile.Low ? 15 : 30;
        settings.Resolution = settings.Profile is RecordingProfile.Low or RecordingProfile.Cpu ? OutputResolution.Hd720
            : settings.Profile == RecordingProfile.Balanced ? OutputResolution.FullHd1080 : OutputResolution.Original;
        settings.Encoder = EncoderChoice.Auto;
        settings.Quality = settings.Profile == RecordingProfile.Low ? CompressionQuality.Small : CompressionQuality.Balanced;
        settings.UseDxgi = !RequiresCpu(settings.Profile);
        settings.UseModernWindowCapture = !RequiresCpu(settings.Profile);
    }
    internal static string Hint(RecordingProfile profile) => profile switch
    {
        RecordingProfile.High => L10n.T("高配 60 FPS：无损编码，适合较强处理器与高速磁盘。"),
        RecordingProfile.Balanced => L10n.T("中配 1080p / 30 FPS：H.264 压缩，硬件不可用时使用 CPU。"),
        RecordingProfile.Low => L10n.T("低配 720p / 15 FPS：MPEG-4 轻量压缩，文件可能较大。"),
        RecordingProfile.Cpu => L10n.T("纯 CPU 720p / 30 FPS：H.264 极速压缩，无需独立显卡。"),
        _ => L10n.T("低配可选 H.264 CPU / MPEG-4；有损压缩会降低画质。")
    };
}
