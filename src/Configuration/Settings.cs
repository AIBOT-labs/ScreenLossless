using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace ScreenLossless;

internal enum RecordingScope { FullScreen, Region, Window }
internal enum RecordingPurpose { Game, Desktop }
internal enum EncoderChoice { Auto, Nvenc, X264Rgb, Ffv1, H264Cpu, Mpeg4Cpu, NvencH264, NvencHevc, IntelH264, AmdH264, NvencHevcLossless }
internal enum OutputResolution { Original, Hd720, FullHd1080, Percent75, Percent50 }
internal enum OutputContainer { Mkv, Mp4 }
internal enum CompressionQuality { High, Balanced, Small }
internal enum RecordingProfile { Custom, High, Balanced, Low, Cpu }
internal enum CaptureBackend { Gdi, Dxgi, WindowsGraphics }

internal sealed class AppSettings
{
    public AppLanguage Language { get; set; } = AppLanguage.Automatic;
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public string OutputFolder { get; set; } = DefaultOutputFolder;
    public string FfmpegPath { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public RecordingScope Scope { get; set; } = RecordingScope.FullScreen;
    public RegionSelection? LastRegion { get; set; }
    public WindowSelection? LastWindow {get;set;}
    public FloatingPosition? HudPosition {get;set;}
    public RecordingPurpose Purpose { get; set; } = RecordingPurpose.Game;
    public EncoderChoice Encoder { get; set; } = EncoderChoice.Auto;
    public int Fps { get; set; }
    public RecordingProfile Profile { get; set; } = RecordingProfile.Custom;
    public CompressionQuality Quality { get; set; } = CompressionQuality.Balanced;
    public OutputResolution Resolution { get; set; }
    public OutputContainer Container { get; set; }
    public bool AutoPerformance { get; set; }
    public int SegmentMinutes { get; set; } = 20;
    public int SegmentGigabytes { get; set; } = 2;
    public bool RecordCursor { get; set; } = true;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; }
    public bool MixAudio { get; set; } = true;
    public string SystemDeviceId { get; set; } = "";
    public string MicrophoneDeviceId { get; set; } = "";
    public int StopAfterMinutes { get; set; }
    public int[] Hotkeys { get; set; } = HotkeyBindings.Defaults.ToArray();
    public bool Countdown { get; set; } = true;
    public bool MinimizeWhenRecording { get; set; } = true;
    public bool UseDxgi { get; set; } = true;
    public bool UseModernWindowCapture {get;set;}=true;
    public bool ExcludeHudFromCapture { get; set; } // Compatibility-first: visible in remote desktops and screen sharing.
    public float SystemVolume { get; set; } = 1.0f;
    public float MicrophoneVolume { get; set; } = 1.0f;
    public static string DefaultOutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenLossless");
    internal static string? SettingsFileOverride { get; set; }
    private static string SettingsFile => SettingsFileOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenLossless", "settings.json");

    public void Normalize()
    {
        if (!Enum.IsDefined(Language)) Language = AppLanguage.Automatic;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Light;
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = DefaultOutputFolder;
        FfmpegPath ??= "";
        MonitorName ??= String.Empty;
        SystemDeviceId ??= String.Empty;
        MicrophoneDeviceId ??= String.Empty;
        if (!Enum.IsDefined(Scope)) Scope = RecordingScope.FullScreen;
        if (LastRegion is { } region && (region.Width<64 || region.Height<64)) LastRegion=null;
        if (!Enum.IsDefined(Purpose)) Purpose = RecordingPurpose.Game;
        if (!Enum.IsDefined(Encoder)) Encoder = EncoderChoice.Auto;
        if (!Enum.IsDefined(Profile)) Profile = RecordingProfile.Custom;
        if (!Enum.IsDefined(Quality)) Quality = CompressionQuality.Balanced;
        if (!Enum.IsDefined(Resolution)) Resolution = OutputResolution.Original;
        if (!Enum.IsDefined(Container)) Container = OutputContainer.Mkv;
        if (!OutputOptions.SegmentMinutes.Contains(SegmentMinutes)) SegmentMinutes = 20;
        if (!OutputOptions.SegmentGigabytes.Contains(SegmentGigabytes)) SegmentGigabytes = 2;
        if (Fps is not (0 or 15 or 24 or 30 or 60 or 120)) Fps = 0;
        if (StopAfterMinutes is < 0 or > 1440) StopAfterMinutes = 0;
        if (HotkeyBindings.Validate(Hotkeys) is not null) Hotkeys = HotkeyBindings.Defaults.ToArray();
        SystemVolume = float.IsFinite(SystemVolume) ? Math.Clamp(SystemVolume, 0, 1) : 1;
        MicrophoneVolume = float.IsFinite(MicrophoneVolume) ? Math.Clamp(MicrophoneVolume, 0, 1) : 1;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch (Exception e) { Trace.WriteLine(L10n.T("读取设置失败：") + e.Message); }
        return new AppSettings();
    }
    public void Save()
    {
        string destination = SettingsFile;
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, destination, true);
        }
        catch (Exception e) { Trace.WriteLine(L10n.T("保存设置失败：") + e.Message); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
}

internal sealed record MonitorInfo(int Index, string Name, Rectangle Bounds, bool Primary, string Adapter, int DpiX = 96, int DpiY = 96)
{
    public override string ToString() => L10n.F($"显示器 {Index + 1}  ·  {Bounds.Width}×{Bounds.Height}") + (Primary ? L10n.T("  ·  主显示器") : "");
}

internal sealed record WindowInfo(IntPtr Handle, string Title, Rectangle Bounds)
{
    public override string ToString() => $"{Title}  ({Bounds.Width}×{Bounds.Height})";
}

internal sealed record RecordPlan(
    Rectangle Bounds, MonitorInfo? Monitor, RecordingScope Scope, RecordingPurpose Purpose,
    EncoderChoice Encoder, CaptureBackend Backend, int Fps, bool Cursor,
    bool SystemAudio, bool Microphone, float SystemVolume, float MicrophoneVolume,
    bool MixAudio = true, string SystemDeviceId = "", string MicrophoneDeviceId = "", long WindowHandle = 0, RecordingProfile Profile = RecordingProfile.Custom, CompressionQuality Quality = CompressionQuality.Balanced,
    OutputResolution Resolution = OutputResolution.Original, OutputContainer Container = OutputContainer.Mkv,
    int SegmentMinutes = 20, int SegmentGigabytes = 2, bool AutoPerformance = false)
{
    public string EncoderName => Encoder switch
    {
        EncoderChoice.Nvenc => L10n.T("NVIDIA NVENC · High 4:4:4 无损"),
        EncoderChoice.X264Rgb => L10n.T("x264rgb · RGB 逐像素无损"),
        EncoderChoice.Ffv1 => L10n.T("FFV1 · RGB 逐像素无损"),
        EncoderChoice.H264Cpu => L10n.T("H.264 CPU · 极速有损压缩"),
        EncoderChoice.Mpeg4Cpu => L10n.T("MPEG-4 CPU · 轻量有损压缩"),
        EncoderChoice.NvencH264 => L10n.T("NVIDIA H.264 · 硬件有损压缩"),
        EncoderChoice.NvencHevc => L10n.T("NVIDIA HEVC · 硬件有损压缩"),
        EncoderChoice.NvencHevcLossless => L10n.T("NVIDIA HEVC · 10-bit 4:4:4 编码无损"),
        EncoderChoice.IntelH264 => L10n.T("Intel QSV H.264 · 硬件有损压缩"),
        EncoderChoice.AmdH264 => L10n.T("AMD AMF H.264 · 硬件有损压缩"),
        _ => L10n.T("自动")
    };
    public Size OutputSize => OutputOptions.Size(Bounds.Size, Resolution, !IsLossless);
    public bool IsLossless => VideoEncoding.IsLossless(Encoder);
    public string BackendName => Backend==CaptureBackend.WindowsGraphics?L10n.T("WGC 现代窗口捕获"):WindowHandle!=0?L10n.T("GDI 窗口跟随"):Backend == CaptureBackend.Dxgi ? L10n.T("DXGI GPU 捕获") : L10n.T("GDI 兼容捕获");
}
