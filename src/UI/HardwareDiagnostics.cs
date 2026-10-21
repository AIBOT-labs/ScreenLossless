using NAudio.CoreAudioApi;
using System.Text;

namespace ScreenLossless;

internal static class HardwareDiagnostics
{
    internal static string Report(Capabilities caps)
    {
        var text = new StringBuilder();
        text.AppendLine(L10n.F($"ScreenLossless {AppVersion.Display} · 设备诊断 · {DateTime.Now:O}"));
        text.AppendLine(L10n.F($"系统：{Environment.OSVersion} · {Environment.ProcessorCount} 个逻辑处理器 · {(Environment.Is64BitProcess ? "64" : "32")} 位进程"));
        text.AppendLine("FFmpeg：" + caps.Ffmpeg);
        text.AppendLine(L10n.T("\r\n编码器（实际试编码；开始录制还会验证所选尺寸）"));
        foreach (var test in caps.HardwareTests)
            text.AppendLine($"{VideoEncoding.Names[(int)test.Encoder]}：{(test.Available ? L10n.T("通过") : L10n.T("不可用"))}\r\n  {test.Detail}");
        text.AppendLine($"CPU：H.264 {(caps.H264Cpu ? L10n.T("有") : L10n.T("无"))} / MPEG-4 {(caps.Mpeg4Cpu ? L10n.T("有") : L10n.T("无"))} / x264rgb {(caps.X264Rgb ? L10n.T("有") : L10n.T("无"))} / FFV1 {(caps.Ffv1 ? L10n.T("有") : L10n.T("无"))}");
        text.AppendLine(L10n.T("\r\n显示器（物理像素坐标；选区不随界面 DPI 缩放）"));
        foreach (var monitor in WindowsInterop.GetMonitors())
            text.AppendLine($"{monitor.Name} · {monitor.Adapter} · {monitor.Bounds} · DPI {monitor.DpiX}×{monitor.DpiY}（{monitor.DpiX / 96d:P0}）{(monitor.Primary ? L10n.T(" · 主屏") : "")}");
        text.AppendLine(L10n.T("\r\n音频端点（系统默认按 Multimedia 角色跟随）"));
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                string active = "";
                try { using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); active = device.ID; }
                catch { }
                foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.All))
                    using (device) text.AppendLine($"{flow} · {device.FriendlyName} · {device.State}{(device.ID == active ? L10n.T(" · 默认") : "")}\r\n  {device.ID}");
            }
        }
        catch (Exception e) { text.AppendLine(L10n.T("音频枚举失败：") + e.Message); }
        text.AppendLine(L10n.T("\r\n试编码和模拟测试不能替代各品牌显卡、实体声卡拔插、数小时音画同步及混合 DPI 多屏的实机验证。"));
        return text.ToString();
    }
}
internal sealed class HardwareDiagnosticsForm : ThemedForm
{
    internal HardwareDiagnosticsForm(Capabilities caps)
    {
        SuspendLayout(); Text = L10n.T("设备诊断"); DialogLayout.Style(this); ClientSize = new Size(820, 600); MinimumSize = new Size(560, 400);
        var root = DialogLayout.Grid(new[] { 100f }, 38, -100, 48); root.Padding = new Padding(18);
        root.Controls.Add(DialogLayout.Text(L10n.T("查看编码试验、声卡状态与屏幕 DPI；可导出完整报告。"), 10, true), 0, 0);
        string report = HardwareDiagnostics.Report(caps);
        var details = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Ui.Surface, ForeColor = Ui.Text, Text = report };
        var card = DialogLayout.Card(); card.Controls.Add(new ThemedScrollHost(details)); root.Controls.Add(card, 0, 1);
        var actions = DialogLayout.Grid(new[] { 100f, -130f, -100f }, -100);
        var save = new SoftButton { Text = L10n.T("导出报告"), Glyph = "folder" };
        var close = new SoftButton { Text = L10n.T("关闭"), Primary = true, DialogResult = DialogResult.OK };
        save.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Title = L10n.T("导出设备诊断"), Filter = L10n.T("文本文件 (*.txt)|*.txt"), FileName = "ScreenLossless-devices.txt" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, report); }
            catch (Exception e) { MessageBox.Show(this, e.Message, L10n.T("导出失败"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        DialogLayout.Button(actions, save, 1); DialogLayout.Button(actions, close, 2);
        root.Controls.Add(actions, 0, 2); AcceptButton = close; CancelButton = close; SetContent(root);
        Shown += (_, _) => Bounds = MainForm.FitBounds(Bounds, Screen.FromHandle(Handle).WorkingArea);
        ResumeLayout(true);
    }
}
