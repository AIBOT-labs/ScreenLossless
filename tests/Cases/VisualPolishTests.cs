namespace ScreenLossless;
internal static partial class RegressionTests
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    private static void PolishSnapshot(Control window, string name)
    {
        window.Refresh();
        using var bitmap = new Bitmap(window.Width, window.Height);
        bool rendered;
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr dc = graphics.GetHdc();
            try { rendered = PrintWindow(window.Handle, dc, 2); }
            finally { graphics.ReleaseHdc(dc); }
        }
        if (!rendered) window.DrawToBitmap(bitmap, window.ClientRectangle);
        bitmap.Save(Path.Combine(_root, name + ".png"));
    }
    private static async Task TestVisualPolishAsync()
    {
        var original = L10n.Preference;
        try
        {
            foreach (var language in new[] { AppLanguage.Chinese, AppLanguage.English })
            {
                L10n.Configure(language);
                new AppSettings { Language = language, FfmpegPath = _caps.Ffmpeg, OutputFolder = _root, Scope = RecordingScope.FullScreen }.Save();
                using var main = new MainForm { ShowInTaskbar = false };
                main.Show(); await Until(() => main.CanChangeLanguage);
                Check(!Descendants(main).Any(c => c.Text is "历史记录" or "History"), "Removed history entry stays absent in both languages");
                Check(new[]{"ScreenLossless.HistoryForm","ScreenLossless.HistoryGrid","ScreenLossless.HistoryFile","ScreenLossless.MediaInfo"}.All(name => typeof(MainForm).Assembly.GetType(name) is null),
                    "History scanning, metadata and list types are absent from the application");
                var languageBox = Field<ChoiceBox>(main, "_languageBox");
                // Test native DPI changes as well as the minimum viewport on each display.
                foreach (var screen in Screen.AllScreens)
                {
                    main.Location = screen.WorkingArea.Location + new Size(20, 20);
                    await Task.Delay(100);
                    foreach (var size in new[] { main.Size, main.MinimumSize })
                    {
                        main.Size = size; await Task.Delay(30);
                        Check(languageBox.Items.Cast<object>().All(item => TextRenderer.MeasureText(languageBox.GetItemText(item), languageBox.Font,
                                Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width <= languageBox.DisplayTextBounds.Width),
                            $"All language labels fit without ellipsis: {language}, width {size.Width}, DPI {main.DeviceDpi}");
                        Check(languageBox.Height >= languageBox.Font.Height + Ui.S(languageBox, 8), "Language field has balanced vertical padding");
                        var captions = Descendants(main).OfType<CaptionButton>().ToArray();
                        Check(captions.Length == 3 && captions.All(c => c.Height >= Ui.S(c, 36) && c.Parent!.ClientRectangle.Contains(c.Bounds)),
                            "Caption buttons retain comfortable hit areas within the title bar");
                        foreach (var tab in Field<List<SoftButton>>(main, "_settingsTabs"))
                        {
                            tab.PerformClick(); await Task.Delay(15);
                            var tabs = Field<List<SoftButton>>(main, "_settingsTabs");
                            Check(tabs.Count(t => t.Active) == 1 && tab.Active && tabs.All(t => t.Parent!.ClientRectangle.Contains(t.Bounds)),
                                "Segmented settings tabs remain exclusive and unclipped: " + tab.Text);
                        }
                        CheckButtonsFit(main);
                    }
                }
                languageBox.DroppedDown = true; await Task.Delay(35);
                var popup = Field<ToolStripDropDown>(languageBox, "_popup");
                Check(popup.Region is not null && !popup.Region.IsVisible(0, 0) && popup.Region.IsVisible(popup.Width / 2, popup.Height / 2),
                    "Language menu has real rounded window corners");
                Check(!popup.DropShadowEnabled && Screen.FromControl(main).WorkingArea.Contains(popup.Bounds),
                    "Language menu avoids native square shadow and stays inside the display");
                var list = Field<ListBox>(languageBox, "_popupList"); int preference = languageBox.SelectedIndex;
                list.SelectedIndex = (preference + 1) % list.Items.Count; Key(list, Keys.Escape);
                Check(!languageBox.DroppedDown && languageBox.SelectedIndex == preference, "Escape cancels highlighted language without changing preference");
                var tabsList = Field<List<SoftButton>>(main, "_settingsTabs"); tabsList[3].PerformClick();
                var format = Field<ChoiceBox>(main, "_containerBox"); format.DroppedDown = true; await Task.Delay(35);
                popup = Field<ToolStripDropDown>(format, "_popup");
                Check(popup.Region is not null && !popup.Region.IsVisible(0, 0) && !popup.DropShadowEnabled,
                    "Output format menu uses the same rounded popup treatment");
                PolishSnapshot(popup, "polish-format-" + language);
                Key(Field<ListBox>(format, "_popupList"), Keys.Escape);
                main.Activate(); await Task.Delay(30); PolishSnapshot(main, "polish-main-" + language);
                main.Close();
            }
            using var form = new Form { ShowInTaskbar = false, ClientSize = new Size(240, 90) };
            using var stop = new SoftButton { Dock = DockStyle.Fill, Destructive = true, Text = "Stop" }; form.Controls.Add(stop); form.Show();
            using var buttonShot = new Bitmap(stop.Width, stop.Height); stop.DrawToBitmap(buttonShot, stop.ClientRectangle);
            Check(buttonShot.GetPixel(20, 20).ToArgb() == Ui.DangerLight.ToArgb(), "Enabled stop action has a clear destructive surface");
            stop.Enabled = false; stop.DrawToBitmap(buttonShot, stop.ClientRectangle);
            Check(buttonShot.GetPixel(20, 20).ToArgb() == Ui.Disabled.ToArgb(), "Disabled stop action has a neutral surface"); form.Close();
        }
        finally { L10n.Configure(original); }
    }
}
