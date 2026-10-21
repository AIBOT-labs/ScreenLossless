using System.Reflection;
using System.Runtime.InteropServices;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static void MouseBar(ThemedScrollBar bar, string action, Point point, int delta = 0)
    {
        typeof(ThemedScrollBar).GetMethod(action, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar,
            new object[] { new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, delta) });
    }
    private static async Task TestScrollbarsAsync()
    {
        using var form = new Form { Text = "ScreenLossless scrollbar checks", ClientSize = new Size(480, 300), ShowInTaskbar = false };
        var list = new DetailListBox();
        for (int i = 0; i < 50; i++) list.Items.Add("Window " + i);
        using var host = new ThemedScrollHost(list); form.Controls.Add(host); form.Show();
        await Task.Delay(100); host.RefreshScrollbars(); var bar = host.VerticalBar;
        Check(!host.HorizontalBar.Visible, "Vertical-only list never shows a phantom horizontal scrollbar");
        Check(bar.Visible && bar.Width == Ui.S(host, 12), "List has a narrow theme scrollbar");
        Check(list.ClientSize.Width == host.ViewportBounds.Width && list.Width > list.ClientSize.Width,
            "Native list scrollbar lies completely outside the clipped viewport");
        bar.Value = bar.Maximum; await Task.Delay(40); host.RefreshScrollbars();
        Check(list.TopIndex == bar.Maximum && bar.Value > 0, "Themed list scrollbar reaches the final item");
        list.SelectedIndex = 0; await Task.Delay(40);
        Check(bar.Value == list.TopIndex && bar.Value == 0, "Keyboard/programmatic selection synchronizes scrollbar position");
        MouseBar(bar, "OnMouseWheel", new Point(5, 5), -120); await Task.Delay(30);
        Check(list.TopIndex > 0 && bar.Value == list.TopIndex, "Wheel over themed scrollbar scrolls list content");
        ScrollNative.SendMessage(list.Handle, 0x100, new IntPtr((int)Keys.End), IntPtr.Zero); await Task.Delay(30);
        Check(list.SelectedIndex == 49 && bar.Value == list.TopIndex, "Native End-key selection still scrolls to last list row");
        ScrollNative.SendMessage(list.Handle, 0x100, new IntPtr((int)Keys.Home), IntPtr.Zero); await Task.Delay(30);
        list.Focus(); bar.Value = 0;
        int mousePosition = (5 << 16) | (bar.Width / 2);
        ScrollNative.SendMessage(bar.Handle, 0x201, new IntPtr(1), new IntPtr(mousePosition));
        ScrollNative.SendMessage(bar.Handle, 0x202, IntPtr.Zero, new IntPtr(mousePosition));
        Check(list.Focused && !bar.Focused, "Dragging scrollbar keeps keyboard focus on the list");
        bar.Value = 0; Rectangle thumb = bar.ThumbBounds;
        MouseBar(bar, "OnMouseDown", new Point(bar.Width / 2, thumb.Top + 3));
        MouseBar(bar, "OnMouseMove", new Point(bar.Width / 2, bar.Height - 1));
        MouseBar(bar, "OnMouseUp", new Point(bar.Width / 2, bar.Height - 1));
        Check(bar.Value == bar.Maximum && list.TopIndex == bar.Maximum, "Thumb drag reaches bottom without changing selection");
        bar.Value = 0; MouseBar(bar, "OnMouseDown", new Point(bar.Width / 2, bar.Height - 3));
        MouseBar(bar, "OnMouseUp", new Point(bar.Width / 2, bar.Height - 3));
        Check(bar.Value > 0 && bar.Value <= bar.PageSize, "Track click pages the list");
        using (var shot = new Bitmap(bar.Width, bar.Height))
        {
            bar.Value = 0; bar.DrawToBitmap(shot, bar.ClientRectangle);
            Check(shot.GetPixel(bar.Width / 2, Math.Min(10, bar.ThumbBounds.Bottom - 2)).ToArgb() == Ui.ScrollThumb.ToArgb(), "Idle scrollbar thumb uses theme color");
            Check(shot.GetPixel(0, 10).ToArgb() == bar.BackColor.ToArgb(), "Scrollbar gutter has no native gray track");
        }
        list.Enabled = false; int disabled = bar.Value; Key(bar, Keys.End);
        Check(!bar.Enabled && bar.Value == disabled, "Disabled lists also disable scrollbar interactions"); list.Enabled = true;
        list.Items.Clear(); await Task.Delay(40); host.RefreshScrollbars();
        Check(!bar.Visible && host.ViewportBounds.Width == host.ClientSize.Width, "Empty list removes scrollbar and returns its width to content");
        for (int i = 0; i < 12; i++) list.Items.Add("Row " + i);
        await Task.Delay(40); form.ClientSize = new Size(480, 1000); await Task.Delay(40); host.RefreshScrollbars();
        Check(!bar.Visible, "Resizing to fit all rows removes unnecessary scrollbar");
        form.Controls.Remove(host);

        var page = new Panel { AutoScroll = true, BackColor = Ui.Surface };
        var longContent = new Panel { Height = 900, Dock = DockStyle.Top, BackColor = Ui.Surface }; page.Controls.Add(longContent);
        using var pageHost = new ThemedScrollHost(page); form.ClientSize = new Size(480, 300); form.Controls.Add(pageHost);
        await Task.Delay(70); pageHost.RefreshScrollbars();
        Check(pageHost.VerticalBar.Visible && page.ClientSize.Width == pageHost.ViewportBounds.Width, "Settings viewport clips native scrollbar while preserving client width");
        pageHost.VerticalBar.Value = pageHost.VerticalBar.Maximum;
        Check(-page.AutoScrollPosition.Y == pageHost.VerticalBar.Value && longContent.Bottom <= page.ClientSize.Height,
            "Settings scrollbar reaches bottom without clipping final controls");
        page.AutoScrollPosition = Point.Empty; await Task.Delay(30);
        Check(pageHost.VerticalBar.Value == 0, "Native settings scrolling updates themed thumb");
        form.Controls.Remove(pageHost);

        using var text = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, BackColor = Ui.Surface,
            Text = string.Join(Environment.NewLine, Enumerable.Range(0, 100).Select(i => "Diagnostic line " + i)) };
        using var textHost = new ThemedScrollHost(text); form.Controls.Add(textHost); await Task.Delay(50); textHost.RefreshScrollbars();
        Check(textHost.VerticalBar.Visible && text.ClientSize.Width == textHost.ViewportBounds.Width, "Diagnostic text uses themed scrollbar and hides native gutter");
        textHost.VerticalBar.Value = textHost.VerticalBar.Maximum; await Task.Delay(30);
        Check(textHost.VerticalBar.Value > 0 && textHost.VerticalBar.Value == ScrollNative.SendMessage(text.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero).ToInt32(), "Diagnostic thumb scrolls real text lines");
        text.Text = "short"; await Task.Delay(30); textHost.RefreshScrollbars();
        Check(!textHost.VerticalBar.Visible, "Short diagnostic report needs no scrollbar");
        form.Controls.Remove(textHost);

        // Horizontal and vertical overflow must coexist without leaking a native corner.
        using var both = new Panel { AutoScroll = true, BackColor = Ui.Surface, AutoScrollMinSize = new Size(900, 900) };
        using var bothHost = new ThemedScrollHost(both); form.Controls.Add(bothHost); await Task.Delay(40); bothHost.RefreshScrollbars();
        Check(bothHost.VerticalBar.Visible && bothHost.HorizontalBar.Visible, "Two-axis overflow has themed bars on both axes");
        bothHost.VerticalBar.Value = 200; bothHost.HorizontalBar.Value = 300;
        Check(both.AutoScrollPosition == new Point(-300, -200), "Two-axis scrolling retains the other axis position");
        form.Controls.Remove(bothHost);
        form.Close();

        new AppSettings { FfmpegPath = _caps.Ffmpeg, OutputFolder = _root, Scope = RecordingScope.FullScreen }.Save();
        using var main = new MainForm { ShowInTaskbar = false };
        main.Show(); await Until(() => Field<Capabilities>(main, "_caps").Available && !Field<bool>(main, "_busy"));
        main.Size = main.MinimumSize;
        foreach (var tab in Field<List<SoftButton>>(main, "_settingsTabs"))
        {
            tab.PerformClick(); await Task.Delay(30);
            var current = Field<List<Control>>(main, "_settingsPages").Single(p => p.Visible);
            Check(current is ThemedScrollHost, "Every settings tab uses unified themed scrolling: " + tab.Text);
        }
        Field<List<SoftButton>>(main, "_settingsTabs")[0].PerformClick();
        var choice = Field<ChoiceBox>(main, "_codecBox"); choice.DropDownHeight = 150; choice.DroppedDown = true; await Task.Delay(60);
        var popup = Field<ToolStripDropDown>(choice, "_popup"); var options = Field<ListBox>(choice, "_popupList");
        var choiceHost = (ThemedScrollHost)((ToolStripControlHost)popup.Items[0]).Control;
        Check(choiceHost.VerticalBar.Visible && !choiceHost.HorizontalBar.Visible, "Codec dropdown shares themed scrolling without horizontal overflow");
        Check(options.ClientSize.Width - Ui.S(options, 42) >= options.Items.Cast<object>().Max(item => TextRenderer.MeasureText(choice.GetItemText(item), options.Font).Width),
            "Dropdown reserves enough width for full codec labels and themed scrollbar");
        choiceHost.VerticalBar.Value = choiceHost.VerticalBar.Maximum; await Task.Delay(30);
        Check(choice.DroppedDown && options.TopIndex > 0, "Dragging dropdown scrollbar keeps popup open without committing selection");
        Snapshot(popup, "themed-scrollbar-dropdown");
        PolishSnapshot(popup, "themed-scrollbar-dropdown-screen");
        Key(options, Keys.Escape);
        Check(!choice.DroppedDown, "Escape closes scrolled dropdown normally");
        Snapshot(main, "themed-scrollbar-main");
        main.Activate(); await Task.Delay(40); PolishSnapshot(main, "themed-scrollbar-main-screen"); main.Close();
        using var picker = new WindowPicker(Enumerable.Range(1, 40).Select(i => new WindowInfo(new IntPtr(i), "Window " + i, new Rectangle(0, 0, 400, 300))).ToList()) { ShowInTaskbar = false };
        picker.Show(); await Task.Delay(50);
        Check(Descendants(picker).OfType<ThemedScrollHost>().Single().VerticalBar.Visible, "Window picker list uses themed scrollbar"); picker.Close();
        using var recovery = new RecoveryForm(_root, _caps);
        Check(Descendants(recovery).OfType<ThemedScrollHost>().Count() == 1, "Recovery list is wired to unified scrollbar");
        using var diagnostics = new HardwareDiagnosticsForm(_caps);
        Check(Descendants(diagnostics).OfType<ThemedScrollHost>().Count() == 1, "Device diagnostic report is wired to unified scrollbar");
    }
}
