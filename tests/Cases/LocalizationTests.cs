using System.Globalization;
using System.Text.Json;

namespace ScreenLossless;
internal static partial class RegressionTests
{
    private static int RunLanguageContext(string ffmpeg)
    {
        new AppSettings{Language=AppLanguage.English,FfmpegPath=ffmpeg,OutputFolder=_root,RecordSystemAudio=false,RecordMicrophone=false,Countdown=false}.Save();
        using var context=new ScreenLosslessApplicationContext();int result=0;var first=context.Window;
        first.Shown+=async(_,_)=>
        {
            try
            {
                await Until(()=>context.Window.CanChangeLanguage);
                foreach(var language in new[]{AppLanguage.Chinese,AppLanguage.English,AppLanguage.Automatic,AppLanguage.English})
                {
                    var previous=context.Window;Field<ChoiceBox>(previous,"_languageBox").SelectedIndex=(int)language;
                    await Until(()=>context.Window!=previous&&context.Window.CanChangeLanguage);
                    Check(Application.MessageLoop&&context.Window.Visible&&!context.Window.IsDisposed&&previous.IsDisposed,
                        "Application.Run(context) survives live language switch: "+language);
                    Check(L10n.Language==L10n.Resolve(language)&&AppSettings.Load().Language==language,
                        "Application context applies and persists language: "+language);
                    context.Window.Hide();context.RequestActivation();await Task.Delay(30);
                    Check(context.Window.Visible,"Single-instance activation targets the replacement window: "+language);
                }
                Console.WriteLine($"ALL {_passed} LANGUAGE CONTEXT CHECKS PASSED");
            }
            catch(Exception error){Console.Error.WriteLine(error);result=1;}
            finally{context.Window.Close();}
        };
        Application.Run(context);L10n.Configure(AppLanguage.Chinese);return result;
    }
    private static bool Han(string? value)=>System.Text.RegularExpressions.Regex.IsMatch(value??"",@"[\u3400-\u9fff]");
    private static async Task TestLocalizationAsync()
    {
        var original=L10n.Preference;string? settingsFile=AppSettings.SettingsFileOverride;
        ScreenLosslessApplicationContext? context=null;
        try
        {
            foreach(string language in new[]{"zh-CN","zh-TW","zh-HK","zh-SG","zh-Hans","zh-Hant"})
                Check(L10n.Resolve(AppLanguage.Automatic,CultureInfo.GetCultureInfo(language))==AppLanguage.Chinese,"Chinese Windows display language defaults to Chinese: "+language);
            foreach(string language in new[]{"en-US","en-GB","ja-JP","ko-KR","de-DE","fr-FR","ar-SA",""})
                Check(L10n.Resolve(AppLanguage.Automatic,CultureInfo.GetCultureInfo(language))==AppLanguage.English,"Non-Chinese Windows display language defaults to English: "+language);
            Check(L10n.Resolve(AppLanguage.English,CultureInfo.GetCultureInfo("zh-CN"))==AppLanguage.English&&L10n.Resolve(AppLanguage.Chinese,CultureInfo.GetCultureInfo("en-US"))==AppLanguage.Chinese,
                "Manual language choice overrides system language");
            AppSettings.SettingsFileOverride=Path.Combine(_root,"localization-settings.json");
            File.WriteAllText(AppSettings.SettingsFileOverride,"{}");Check(AppSettings.Load().Language==AppLanguage.Automatic,"Existing settings without a language remain automatic");
            File.WriteAllText(AppSettings.SettingsFileOverride,"{\"Language\":999}");Check(AppSettings.Load().Language==AppLanguage.Automatic,"Unknown language settings safely fall back to system selection");
            var folder=Path.Combine(_root,"localization 中文 {recordings}");Directory.CreateDirectory(folder);
            var work=Screen.PrimaryScreen!.WorkingArea;var bounds=new Rectangle(work.Left+100,work.Top+220,320,180);
            new AppSettings{Language=AppLanguage.English,FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,Scope=RecordingScope.Region,
                LastRegion=RegionSelection.FromRectangle(bounds),Encoder=EncoderChoice.X264Rgb,Fps=15,Countdown=false,
                RecordSystemAudio=false,RecordMicrophone=false,RecordCursor=false,UseDxgi=false,MinimizeWhenRecording=true}.Save();
            Check(AppSettings.Load().Language==AppLanguage.English,"Manual English selection persists alongside existing recording preferences");
            L10n.Configure(AppLanguage.English);
            Check(new[]{"开始录制","跟随系统","录制范围","设备诊断","恢复录制"}.All(L10n.Catalog.ContainsKey)&&L10n.Catalog.All(item=>!string.IsNullOrWhiteSpace(item.Value)&&!Han(item.Value)),"English resource covers all application messages without untranslated Chinese");
            Check(L10n.Catalog.All(item=>System.Text.RegularExpressions.Regex.Matches(item.Key,@"\{\d+(?:[^{}]*)\}").Select(m=>m.Value)
                .SequenceEqual(System.Text.RegularExpressions.Regex.Matches(item.Value,@"\{\d+(?:[^{}]*)\}").Select(m=>m.Value))),"Every translation preserves format arguments and numeric/date formats");
            string userPath=@"C:\中文 {unchanged}\example.mkv";
            Check(L10n.F($"已选择：{userPath}")=="Selected: "+userPath,"Localization preserves user filenames, Chinese text and braces exactly");
            Check(RecordingProfiles.Names.All(name=>!Han(name))&&VideoEncoding.Names.All(name=>!Han(name))&&OutputOptions.ResolutionNames.All(name=>!Han(name)),"Presets, encoders and output choices use English");
            L10n.Configure(AppLanguage.Chinese);Check(VideoEncoding.Names[0].Contains("自动")&&RecordingProfiles.Names[0].Contains("自定义"),"Static option lists refresh when switching back to Chinese");
            L10n.Configure(AppLanguage.English);Check(!Han(VideoEncoding.Names[0])&&!Han(RecordingProfiles.Names[0]),"Option lists switch repeatedly without stale language caching");
            Check(new PerformanceSnapshot(12.5,.75,2,true).Text.Contains("Encoding 12.5 FPS")&&new RecordingLimit(7).ToString()=="Auto-save after 7 minutes","Dynamic performance and duration messages format in English");
            context=new ScreenLosslessApplicationContext();var main=context.Window;main.Show();await Until(()=>main.CanChangeLanguage);
            Check(Field<SoftButton>(main,"_recordButton").Text=="Start recording"&&main.Text.Contains("Screen recorder"),"English startup localizes the main window and recording controls");
            Check(Field<ChoiceBox>(main,"_languageBox").SelectedItem is LanguageOption{Language:AppLanguage.English},"Title-bar language selector restores the saved selection");
            Check(!Han(Field<NotifyIcon>(main,"_tray").Text)&&Field<NotifyIcon>(main,"_tray").ContextMenuStrip!.Items.Cast<ToolStripItem>().All(item=>!Han(item.Text)),"Tray status and menu are localized");
            Check(Field<TextBox>(main,"_folderBox").Text==folder&&Field<Rectangle>(main,"_region")==bounds,"Language selection preserves the output path and region geometry");
            var originalSize=main.Size;
            foreach(var size in new[]{originalSize,main.MinimumSize})
            {
                main.Size=size;await Task.Delay(50);
                foreach(var tab in Field<List<SoftButton>>(main,"_settingsTabs")){tab.PerformClick();main.PerformLayout();await Task.Delay(30);CheckButtonsFit(main);}
            }
            main.Size=originalSize;Field<List<SoftButton>>(main,"_settingsTabs")[0].PerformClick();Snapshot(main,"language-main-English");
            using(var picker=new WindowPicker(new(){new WindowInfo(IntPtr.Zero,"Fixture window",bounds)}))
            {picker.Show();await Task.Delay(50);Check(!Han(picker.Text)&&Field<SoftButton>(picker,"_ok").Text=="Select window","Window picker title, search and buttons are English");CheckButtonsFit(picker);picker.Size=picker.MinimumSize;picker.PerformLayout();CheckButtonsFit(picker);picker.Close();}
            using(var recovery=new RecoveryForm(folder,_caps))
            {recovery.Show();await Task.Delay(50);Check(!Han(recovery.Text)&&Field<SoftButton>(recovery,"_recover").Text=="Recover selected","Recovery dialogs and actions are English");CheckButtonsFit(recovery);recovery.Size=recovery.MinimumSize;recovery.PerformLayout();CheckButtonsFit(recovery);recovery.Close();}
            using(var keys=new HotkeySettingsForm(HotkeyBindings.Defaults,_=>null))
            {keys.Show();await Task.Delay(50);Check(!Han(keys.Text),"Hotkey settings are English");CheckButtonsFit(keys);keys.Close();}
            using(var numbers=new NumberSettingsForm(L10n.T("微调录制区域"),L10n.T("坐标与尺寸均为实际屏幕像素，支持跨显示器选区。"),new[]{L10n.T("宽度"),L10n.T("高度")},new[]{320,180},_=>null,true))
            {numbers.Show();await Task.Delay(50);Check(!Han(numbers.Text),"Region and duration input dialogs are English");CheckButtonsFit(numbers);numbers.Close();}
            using(var diagnostics=new HardwareDiagnosticsForm(_caps))
            {diagnostics.Show();await Task.Delay(50);Check(diagnostics.Text=="Device diagnostics","Hardware diagnostics title and actions are English");CheckButtonsFit(diagnostics);diagnostics.Close();}
            main.Activate();await InvokeAsync(main,"StartRecordingAsync");await Until(()=>Field<RecorderEngine>(main,"_engine").State==RecorderState.Recording&&!Field<bool>(main,"_busy"));
            var engine=Field<RecorderEngine>(main,"_engine");var hud=Field<RecordingHud>(main,"_recordingHud");
            Check(hud.Visible&&Field<SoftButton>(hud,"_pause").Text=="Pause"&&Field<SoftButton>(hud,"_stop").Text=="Stop","Actual English recording shows localized floating controls");CheckButtonsFit(hud);Snapshot(hud,"language-hud-English");
            var languageBox=Field<ChoiceBox>(main,"_languageBox");Check(!languageBox.Enabled,"Language switching is disabled while recording");languageBox.SelectedIndex=(int)AppLanguage.Chinese;
            Check(context.Window==main&&AppSettings.Load().Language==AppLanguage.English&&languageBox.SelectedIndex==(int)AppLanguage.English,"Programmatic changes cannot interrupt recording or overwrite language preferences");
            await Task.Delay(600);await InvokeAsync(main,"TogglePauseAsync");Check(Field<SoftButton>(hud,"_pause").Text=="Resume","Paused floating controls show Resume in English");await InvokeAsync(main,"TogglePauseAsync");await Task.Delay(600);
            await InvokeAsync(main,"StopRecordingAsync");Check(File.Exists(engine.OutputFile)&&(await Hashes(engine.OutputFile)).LongLength==engine.FrameCount,"English recording, pause/resume and save retain every accepted frame");
            string lastFile=engine.OutputFile;Field<List<SoftButton>>(main,"_settingsTabs")[2].PerformClick();var normalBounds=main.Bounds;main.ToggleMaximize();languageBox.SelectedIndex=(int)AppLanguage.Chinese;
            await Until(()=>context.Window!=main&&context.Window.CanChangeLanguage);var chinese=context.Window;
            Check(main.IsDisposed&&L10n.Language==AppLanguage.Chinese&&Field<SoftButton>(chinese,"_recordButton").Text=="开始录制","Manual Chinese selection immediately rebuilds the UI without exiting the application");
            Check(AppSettings.Load().Language==AppLanguage.Chinese&&AppSettings.Load().LastRegion?.Restore(SystemInformation.VirtualScreen)==bounds&&AppSettings.Load().OutputFolder==folder&&AppSettings.Load().Fps==15,"Switching language preserves recording options, region and save location");
            Check(Field<SoftButton>(chinese,"_lastFileButton").Enabled&&Field<string>(chinese,"_lastSavedFile")==lastFile&&Field<List<Control>>(chinese,"_settingsPages")[2].Visible,"UI recreation preserves the recent file and selected settings page");
            Check(chinese.WindowState==FormWindowState.Maximized,"Language switching preserves maximized window state");chinese.ToggleMaximize();
            Check(chinese.Bounds==normalBounds,"Restoring after a language switch preserves the previous normal window size and position");
            Snapshot(chinese,"language-main-Chinese");
            Field<ChoiceBox>(chinese,"_languageBox").SelectedIndex=(int)AppLanguage.English;
            await Until(()=>context.Window!=chinese&&context.Window.CanChangeLanguage);var english=context.Window;
            Check(Field<SoftButton>(english,"_recordButton").Text=="Start recording"&&!Han(VideoEncoding.Names[0]),"Switching back to English updates UI, tray and encoder choices in the same process");
            Field<ChoiceBox>(english,"_languageBox").SelectedIndex=(int)AppLanguage.Automatic;
            await Until(()=>context.Window!=english&&context.Window.CanChangeLanguage);
            Check(AppSettings.Load().Language==AppLanguage.Automatic&&L10n.Language==L10n.Resolve(AppLanguage.Automatic),"Selecting System default restores Windows-language detection and persists automatic mode");
            context.Window.Close();context.Dispose();context=null;
        }
        finally
        {
            if(context is not null)
            {
                var window=context.MainForm as MainForm;
                if(window is not null&&!window.IsDisposed){if(Field<RecorderEngine>(window,"_engine").State!=RecorderState.Idle)try{await InvokeAsync(window,"StopRecordingAsync");}catch{}window.Close();}
                context.Dispose();
            }
            AppSettings.SettingsFileOverride=settingsFile;L10n.Configure(original);
        }
    }
}
