using System.Reflection;

namespace ScreenLossless;

internal static partial class RegressionTests
{
    private static int CodecIndex(ChoiceBox box, EncoderChoice encoder) => box.Items.IndexOf(
        box.Items.OfType<EncoderOption>().Single(item => item.Encoder == encoder));
    private static void SelectCodec(ChoiceBox box, EncoderChoice encoder) => box.SelectedIndex = CodecIndex(box, encoder);

    private static void CheckEncoderOrder(ChoiceBox box)
    {
        var items = box.Items.OfType<EncoderOption>().ToArray();
        Check(items.Length == Enum.GetValues<EncoderChoice>().Length && items.Select(i => i.Encoder).Distinct().Count() == items.Length,
            "Encoder dropdown contains each stable codec identity exactly once");
        Check(items[0].Encoder == EncoderChoice.Auto && items[0].Available,
            "Automatic encoder selection stays at the top");
        int unavailable = Array.FindIndex(items, item => !item.Available);
        Check(unavailable < 0 || items.Skip(unavailable).All(item => !item.Available && item.ToString().Contains("不可用")),
            "Unavailable encoders form one labelled group at the bottom");
        var available = items.Where(item => item.Available && item.Encoder != EncoderChoice.Auto).ToArray();
        int lossless = Array.FindIndex(available, item => VideoEncoding.IsLossless(item.Encoder));
        Check(lossless < 0 || available.Skip(lossless).All(item => VideoEncoding.IsLossless(item.Encoder)),
            "Available compressed codecs precede lossless preservation modes");
        Check(!available.Any(item => item.Encoder == EncoderChoice.Mpeg4Cpu) || available[0].Encoder == EncoderChoice.Mpeg4Cpu,
            "Lightweight MPEG-4 comes first when CPU encoders are available");
    }

    private static async Task TestEncoderChoicesAsync()
    {
        new AppSettings { FfmpegPath = _caps.Ffmpeg, OutputFolder = _root, Encoder = EncoderChoice.NvencHevcLossless,
            Fps = 24, RecordSystemAudio = false, RecordMicrophone = false, Countdown = false }.Save();
        using var form = new MainForm { Opacity = 0, ShowInTaskbar = false };
        form.Show(); await Until(() => Field<Capabilities>(form, "_caps").Available && !Field<bool>(form, "_detecting"));
        var box = Field<ChoiceBox>(form, "_codecBox"); var settings = Field<AppSettings>(form, "_settings");
        CheckEncoderOrder(box);
        Check((box.SelectedItem as EncoderOption)?.Encoder == EncoderChoice.NvencHevcLossless,
            "Startup restores the saved HEVC codec by identity instead of its old list position");
        var cpu = new Capabilities { Available = true, Ffmpeg = _caps.Ffmpeg, Ffprobe = _caps.Ffprobe,
            H264Cpu = true, Mpeg4Cpu = true, X264Rgb = true, Ffv1 = true };
        var all = new Capabilities { Available = true, Ffmpeg = _caps.Ffmpeg, Ffprobe = _caps.Ffprobe,
            H264Cpu = true, Mpeg4Cpu = true, X264Rgb = true, Ffv1 = true, Nvenc = true,
            NvencH264 = true, NvencHevc = true, NvencHevcLossless = true, IntelH264 = true, AmdH264 = true };
        foreach (var caps in new[] { cpu, all })
        {
            var selected = (box.SelectedItem as EncoderOption)!.Encoder;
            typeof(MainForm).GetField("_caps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, caps);
            Invoke(form, "UpdateEncoderChoices"); CheckEncoderOrder(box);
            Check((box.SelectedItem as EncoderOption)?.Encoder == selected && settings.Encoder == selected,
                "Changing hardware availability reorders the list without changing the selected codec");
            foreach (var encoder in Enum.GetValues<EncoderChoice>())
            {
                SelectCodec(box, encoder);
                var plan = (RecordPlan)form.GetType().GetMethod("GetPlan", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
                Check(settings.Encoder == encoder && AppSettings.Load().Encoder == encoder && settings.Fps == 24
                    && (encoder == EncoderChoice.Auto || plan.Encoder == encoder),
                    "Sorted selection persists and records the intended codec: " + encoder);
            }
        }
        typeof(MainForm).GetField("_caps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, _caps);
        Invoke(form, "UpdateEncoderChoices"); SelectCodec(box, EncoderChoice.Auto);
        box.DroppedDown = true; await Task.Delay(80);
        Snapshot(Field<ToolStripDropDown>(box, "_popup"), "sorted-encoder-dropdown");
        box.DroppedDown = false; form.Close();
    }
}
