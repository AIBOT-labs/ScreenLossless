namespace ScreenLossless;

internal sealed record EncoderOption(EncoderChoice Encoder, bool Available)
{
    public override string ToString() => VideoEncoding.Names[(int)Encoder] + (Available ? "" : L10n.T("（不可用）"));
}

internal static class VideoEncoding
{
    internal static string[] Names => new[] {
        L10n.T("自动选择（按当前预设）"), L10n.T("H.264 NVIDIA · 4:4:4 无损"), L10n.T("H.264 x264rgb · CPU RGB 无损"), L10n.T("FFV1 · CPU RGB 无损"),
        L10n.T("H.264 CPU · 极速 / 有损"), L10n.T("MPEG-4 CPU · 轻量 / 有损"), L10n.T("H.264 NVIDIA · 低占用 / 有损"), L10n.T("HEVC NVIDIA · 压缩 / 有损"), L10n.T("H.264 Intel QSV · 核显 / 有损"), L10n.T("H.264 AMD AMF · 硬件 / 有损"),
        L10n.T("HEVC NVIDIA · 10-bit 4:4:4 编码无损")
    };
    // Display order is independent of saved enum IDs. Group lightweight compression before
    // hardware compression, YUV lossless encoding and strict RGB preservation.
    private static readonly EncoderChoice[] DisplayOrder =
    {
        EncoderChoice.Auto, EncoderChoice.Mpeg4Cpu, EncoderChoice.H264Cpu,
        EncoderChoice.IntelH264, EncoderChoice.AmdH264, EncoderChoice.NvencH264, EncoderChoice.NvencHevc,
        EncoderChoice.Nvenc, EncoderChoice.NvencHevcLossless, EncoderChoice.X264Rgb, EncoderChoice.Ffv1
    };
    internal static EncoderOption[] Choices(Capabilities caps) => DisplayOrder
        .Select(encoder => new EncoderOption(encoder, encoder == EncoderChoice.Auto || Supported(encoder, caps)))
        .OrderBy(option => !option.Available).ToArray();

    internal static string[] QualityNames => new[] { L10n.T("画质优先"), L10n.T("均衡（推荐）"), L10n.T("体积优先") };
    internal static bool IsLossless(EncoderChoice encoder) => encoder is EncoderChoice.Auto or EncoderChoice.Nvenc or EncoderChoice.X264Rgb or EncoderChoice.Ffv1 or EncoderChoice.NvencHevcLossless;
    internal static bool IsHardware(EncoderChoice encoder) => encoder is EncoderChoice.Nvenc or EncoderChoice.NvencH264 or EncoderChoice.NvencHevc or EncoderChoice.IntelH264 or EncoderChoice.AmdH264 or EncoderChoice.NvencHevcLossless;
    internal static int Quantizer(CompressionQuality quality) => quality == CompressionQuality.High ? 18 : quality == CompressionQuality.Small ? 28 : 23;
    internal static int MpegQuantizer(CompressionQuality quality) => quality == CompressionQuality.High ? 2 : quality == CompressionQuality.Small ? 6 : 4;
    internal static bool Supported(EncoderChoice encoder, Capabilities caps) => encoder switch
    {
        EncoderChoice.Nvenc => caps.Nvenc, EncoderChoice.X264Rgb => caps.X264Rgb, EncoderChoice.Ffv1 => caps.Ffv1,
        EncoderChoice.NvencHevcLossless => caps.NvencHevcLossless,
        EncoderChoice.H264Cpu => caps.H264Cpu, EncoderChoice.Mpeg4Cpu => caps.Mpeg4Cpu,
        EncoderChoice.NvencH264 => caps.NvencH264, EncoderChoice.NvencHevc => caps.NvencHevc, EncoderChoice.IntelH264 => caps.IntelH264, EncoderChoice.AmdH264 => caps.AmdH264, _ => false
    };
    internal static EncoderChoice CpuFallback(Capabilities caps, bool lossless) => lossless
        ? caps.X264Rgb ? EncoderChoice.X264Rgb : caps.Ffv1 ? EncoderChoice.Ffv1 : EncoderChoice.Auto
        : caps.H264Cpu ? EncoderChoice.H264Cpu : caps.Mpeg4Cpu ? EncoderChoice.Mpeg4Cpu : EncoderChoice.Auto;
}
