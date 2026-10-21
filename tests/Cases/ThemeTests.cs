using System.Globalization;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static async Task TestThemesAsync()
    {
        var originalTheme=Ui.Theme;var originalLanguage=L10n.Preference;string? originalSettings=AppSettings.SettingsFileOverride;
        string folder=Path.Combine(_root,"themes");Directory.CreateDirectory(folder);
        AppSettings.SettingsFileOverride=Path.Combine(folder,"settings.json");
        try
        {
            File.WriteAllText(AppSettings.SettingsFileOverride,"{\"Fps\":30}");
            Check(AppSettings.Load().Theme==AppTheme.Light,"Existing settings without a theme keep day mode");
            var invalid=new AppSettings{Theme=(AppTheme)999};invalid.Normalize();
            Check(invalid.Theme==AppTheme.Light,"Invalid theme settings fall back to day mode");
            foreach(var language in new[]{AppLanguage.Chinese,AppLanguage.English})
            {
                L10n.Configure(language);Ui.Configure(AppTheme.Light);
                new AppSettings{Theme=AppTheme.Light,Language=language,FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,Scope=RecordingScope.FullScreen}.Save();
                using var main=new MainForm{ShowInTaskbar=false};main.Show();await Until(()=>main.CanChangeLanguage);
                var engine=Field<RecorderEngine>(main,"_engine");var originalBounds=main.Bounds;IntPtr handle=main.Handle;
                var toggle=Field<SoftButton>(main,"_themeButton");
                Check(toggle.AccessibleName==L10n.T("切换为黑夜模式"),"The theme action has a localized accessible label: "+language);
                Check(toggle.Visible&&toggle.Glyph=="moon"&&Descendants(main).OfType<SoftButton>().Count(button=>button.Glyph is "sun" or "moon")==1,
                    "Exactly one caption button offers switching from day to night");
                using var preview=new Bitmap(32,24);using(var graphics=Graphics.FromImage(preview))graphics.Clear(Color.Coral);
                Field<PreviewCanvas>(main,"_preview").ChangePicture((Bitmap)preview.Clone());
                var picture=Field<PreviewCanvas>(main,"_preview").Picture;
                foreach(var theme in new[]{AppTheme.Dark,AppTheme.Light,AppTheme.Dark})
                {
                    var tabs=Field<List<SoftButton>>(main,"_settingsTabs");tabs[3].PerformClick();
                    var codec=Field<ChoiceBox>(main,"_containerBox");int selected=codec.SelectedIndex;codec.DroppedDown=true;await Task.Delay(30);
                    toggle.PerformClick();await Task.Delay(40);
                    Check(Ui.Theme==theme&&AppSettings.Load().Theme==theme,"Theme selection applies immediately and persists: "+theme+" / "+language);
                    Check(main.Handle==handle&&ReferenceEquals(engine,Field<RecorderEngine>(main,"_engine"))&&main.Bounds==originalBounds,
                        "Theme changes preserve the live main window, recorder and window bounds");
                    Check(!codec.DroppedDown&&codec.SelectedIndex==selected&&tabs[3].Active&&ReferenceEquals(picture,Field<PreviewCanvas>(main,"_preview").Picture),
                        "Theme changes close stale menus without losing the current page, choice or preview");
                    Check(toggle.Glyph==(theme==AppTheme.Dark?"sun":"moon")&&toggle.AccessibleName==(theme==AppTheme.Dark?L10n.T("切换为白天模式"):L10n.T("切换为黑夜模式")),
                        "One theme action always offers the opposite mode");
                    Check(BorderContrast(Ui.Text,Ui.Surface)>=10&&BorderContrast(Ui.Subtle,Ui.Surface)>=4&&BorderContrast(Color.White,Ui.PrimaryFill)>=4.5,
                        "Body text, hints and primary actions remain readable: "+theme);
                    foreach(var tab in tabs){tab.PerformClick();await Task.Delay(15);CheckButtonsFit(main);}
                    if(theme==AppTheme.Dark)
                    {
                        var editors=Descendants(main).OfType<TextBox>().ToArray();
                        Check(editors.All(editor=>editor.BackColor.ToArgb()==Ui.Surface.ToArgb()&&editor.ForeColor.ToArgb()==Ui.Text.ToArgb()),"All native text editors use dark surfaces and readable text");
                        Check(Descendants(main).OfType<CardPanel>().All(card=>card.CardColor.GetBrightness()<.3f),"No light card surfaces remain in night mode");
                        Check(Descendants(main).Where(c=>c.Visible&&c.BackColor.A==255).All(c=>c.BackColor.GetBrightness()<.4f),"Nested status panels, actions and footer have no stale light surface properties");
                        var hint=Field<Label>(main,"_operationHint");
                        using(var shot=new Bitmap(main.Width,main.Height))
                        {
                            using(var graphics=Graphics.FromImage(shot)){var dc=graphics.GetHdc();try{PrintWindow(main.Handle,dc,2);}finally{graphics.ReleaseHdc(dc);}}
                            var point=main.PointToClient(hint.PointToScreen(new Point(hint.Width/2,hint.Height-4)));
                            Check(shot.GetPixel(point.X,point.Y).ToArgb()==Ui.Background.ToArgb(),"The native status surface repaints immediately instead of retaining day-mode pixels");
                        }
                        var hud=Field<RecordingHud>(main,"_recordingHud");
                        Check(Descendants(hud).OfType<CardPanel>().All(card=>card.CardColor.ToArgb()==Ui.Surface.ToArgb()),"The not-yet-opened recording HUD follows night mode");
                        Check(Field<NotifyIcon>(main,"_tray").ContextMenuStrip!.BackColor.ToArgb()==Ui.Surface.ToArgb(),"Tray menu follows the active theme");
                    }
                }
                Field<PreviewCanvas>(main,"_preview").ChangePicture(null);
                foreach(var tab in Field<List<SoftButton>>(main,"_settingsTabs")){tab.PerformClick();await Task.Delay(20);PolishSnapshot(main,"night-"+language+"-"+Field<List<SoftButton>>(main,"_settingsTabs").IndexOf(tab));}
                var formats=Field<ChoiceBox>(main,"_containerBox");formats.DroppedDown=true;await Task.Delay(40);
                var popup=Field<ToolStripDropDown>(formats,"_popup");
                Check(popup.BackColor.ToArgb()==Ui.Surface.ToArgb()&&popup.Region is not null&&!popup.DropShadowEnabled,"Night dropdown uses the dark rounded popup surface");
                Check(Descendants(popup).OfType<ListBox>().All(list=>list.BackColor.ToArgb()==Ui.Surface.ToArgb()),"Night dropdown list has no original white background");
                PolishSnapshot(popup,"night-menu-"+language);formats.DroppedDown=false;
                using(var dialog=new NumberSettingsForm("Theme","Theme",new[]{"X","Y"},new[]{10,20},_=>null){ShowInTaskbar=false})
                {
                    dialog.Show(main);await Task.Delay(25);CheckButtonsFit(dialog);
                    Check(dialog.BackColor.ToArgb()==Ui.Background.ToArgb()&&Descendants(dialog).OfType<TextBox>().All(c=>c.BackColor.ToArgb()==Ui.Surface.ToArgb()),"Settings dialog and input fields follow night mode");
                    Ui.ChangeTheme(AppTheme.Light);await Task.Delay(20);
                    Check(dialog.BackColor.ToArgb()==Ui.Background.ToArgb()&&Descendants(dialog).OfType<TextBox>().All(c=>c.BackColor.ToArgb()==Ui.Surface.ToArgb()),"An already-open settings dialog recolors in place");
                    dialog.Close();
                }
                toggle.PerformClick();main.Close();
                using var reopened=new ScreenLosslessApplicationContext();
                Check(Ui.IsDark&&reopened.Window.BackColor.ToArgb()==Ui.Background.ToArgb()&&Field<SoftButton>(reopened.Window,"_themeButton").Glyph=="sun",
                    "Reopening honors the remembered night theme before constructing the UI");
                reopened.Window.Show();await Until(()=>reopened.Window.CanChangeLanguage);
                var previous=reopened.Window;var nextLanguage=language==AppLanguage.Chinese?AppLanguage.English:AppLanguage.Chinese;
                Field<ChoiceBox>(previous,"_languageBox").SelectedIndex=(int)nextLanguage;
                await Until(()=>reopened.Window!=previous&&reopened.Window.CanChangeLanguage);
                Check(Ui.IsDark&&AppSettings.Load().Theme==AppTheme.Dark&&L10n.Language==nextLanguage&&Field<SoftButton>(reopened.Window,"_themeButton").Glyph=="sun",
                    "Changing language preserves night mode and the single theme action");
                reopened.Window.Close();
            }
            L10n.Configure(AppLanguage.Chinese);Ui.Configure(AppTheme.Light);
            var area=Screen.PrimaryScreen!.WorkingArea;var crop=new Rectangle(area.Left+180,area.Top+240,320,180);
            new AppSettings{Language=AppLanguage.Chinese,Theme=AppTheme.Light,FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,Scope=RecordingScope.Region,
                LastRegion=new(){X=crop.X,Y=crop.Y,Width=crop.Width,Height=crop.Height},Encoder=EncoderChoice.X264Rgb,Fps=15,
                RecordSystemAudio=false,RecordMicrophone=false,RecordCursor=false,Countdown=false,UseDxgi=false,MinimizeWhenRecording=false,ExcludeHudFromCapture=true}.Save();
            using var recording=new MainForm{ShowInTaskbar=false};recording.Show();await Until(()=>recording.CanChangeLanguage);
            using var target=new Form{FormBorderStyle=FormBorderStyle.None,TopMost=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=Rectangle.Inflate(crop,12,12),BackColor=Color.FromArgb(30,140,85)};
            target.Show();Field<SoftButton>(recording,"_recordButton").PerformClick();
            var active=Field<RecorderEngine>(recording,"_engine");await Until(()=>active.State==RecorderState.Recording&&!Field<bool>(recording,"_busy"));await Task.Delay(800);
            int? process=active.CaptureProcessId;var hudActive=Field<RecordingHud>(recording,"_recordingHud");var hudBounds=hudActive.Bounds;IntPtr hudHandle=hudActive.Handle;
            Field<SoftButton>(recording,"_themeButton").PerformClick();await Task.Delay(700);
            Check(Ui.IsDark&&active.State==RecorderState.Recording&&active.CaptureProcessId==process,"Switching to night mode during recording never restarts the encoder");
            Check(hudActive.Handle==hudHandle&&hudActive.Bounds==hudBounds&&hudActive.Visible,"Live HUD recolors without moving or recreating its window");
            await InvokeAsync(recording,"TogglePauseAsync");var elapsed=active.Duration;
            Field<SoftButton>(recording,"_themeButton").PerformClick();await Task.Delay(250);
            Check(active.State==RecorderState.Paused&&Math.Abs((active.Duration-elapsed).TotalMilliseconds)<50,"Changing theme while paused preserves the frozen recording time");
            await InvokeAsync(recording,"TogglePauseAsync");await Task.Delay(700);await InvokeAsync(recording,"StopRecordingAsync");
            Check(active.State==RecorderState.Idle&&File.Exists(active.OutputFile),"Recording still saves successfully after live day/night changes");
            var hashes=await Hashes(active.OutputFile);
            Console.WriteLine($"THEME FRAMES decoded={hashes.Length}, reported={active.FrameCount}, distinct={hashes.Distinct().Count()}, file={active.OutputFile}");
            Check(hashes.Length==active.FrameCount&&hashes.Distinct().Count()==1,"Theme changes preserve every recorded frame and never enter the selected pixels");
            Check(Field<Label>(recording,"_frameLabel").Text==L10n.F($"累计 {hashes.Length:N0} 帧 · {active.SegmentCount} 段"),"Completed recording immediately displays the exact decoded final frame count");
            recording.Close();target.Close();
        }
        finally{AppSettings.SettingsFileOverride=originalSettings;Ui.ChangeTheme(originalTheme);L10n.Configure(originalLanguage);}
    }
}
