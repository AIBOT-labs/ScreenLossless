using System.Reflection;
using System.Text.RegularExpressions;
using System.Drawing.Imaging;

namespace ScreenLossless;

internal static partial class RegressionTests
{
    private static int _passed;
    private static bool _captureEnabled;
    private static string _root = "";
    private static Capabilities _caps = new();
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        _passed++; Console.WriteLine("PASS: " + name);
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static Task InvokeAsync(object instance, string name) => (Task)instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, null)!;
    private static async Task<string> Run(params string[] args) => await HardwareProbe.ExecuteTextAsync(_caps.Ffmpeg, args, 30000);
    private static async Task<string[]> Hashes(string file)
    {
        string text = await Run("-v", "error", "-i", file, "-map", "0:v:0", "-pix_fmt", "rgb24", "-fps_mode", "passthrough", "-f", "framemd5", "-");
        return text.Split('\n').Where(line => Regex.IsMatch(line, @"^\d+,"))
            .Select(line => line.Split(',')[^1].Trim()).ToArray();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        int fixtureIndex = Array.IndexOf(args, "--media-progress-fixture");
        if (fixtureIndex >= 0 && args.Length > fixtureIndex + 2) return MediaProgressFixture(args, fixtureIndex);
        if(args.Length==3&&args[0]=="--window-memory-fixture")
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
            using var fixture=new Form{Text="ScreenLossless memory fixture "+Path.GetFileName(args[1]),Size=new Size(320,180),ShowInTaskbar=false};
            using var lease=new System.Windows.Forms.Timer{Interval=100};
            var watch=System.Diagnostics.Stopwatch.StartNew();
            fixture.Shown+=(_,_)=>{File.WriteAllText(args[1],fixture.Handle.ToInt64().ToString());lease.Start();};
            lease.Tick+=(_,_)=>{if(File.Exists(args[2])||watch.Elapsed>TimeSpan.FromMinutes(2))fixture.Close();};
            Application.Run(fixture);return 0;
        }
        if (args.Length == 2 && args[0] == "--instance-probe")
        {
            using var instance = new SingleInstance(args[1]);
            return instance.IsPrimary ? 4 : instance.ActivateExistingAsync().GetAwaiter().GetResult() ? 0 : 5;
        }
        if (args.Length == 0) { Console.Error.WriteLine("Supply FFmpeg path"); return 2; }
        _root = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine("tests", "artifacts", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(_root); RegressionOutput.Root=_root;
        _captureEnabled = args.Contains("--capture");
        AppSettings.SettingsFileOverride = Path.Combine(_root, "settings.json");
        L10n.Configure(AppLanguage.Chinese);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        if (args.Contains("--language-context")) return RunLanguageContext(Path.GetFullPath(args[0]));
        if (args.Contains("--main-context"))
        {
            var rememberedRegion=new Rectangle(Screen.PrimaryScreen!.WorkingArea.Left+100,Screen.PrimaryScreen.WorkingArea.Top+240,320,180);
            new AppSettings { FfmpegPath=Path.GetFullPath(args[0]), OutputFolder=_root, Scope=RecordingScope.Region, LastRegion=RegionSelection.FromRectangle(rememberedRegion), Encoder=EncoderChoice.X264Rgb, RecordSystemAudio=false, RecordMicrophone=false, RecordCursor=false, Countdown=false, MinimizeWhenRecording=true, UseDxgi=false }.Save();
            int contextExit=0;
            using var main=new MainForm();
            main.Shown+=async (_,_)=>
            {
                try
                {
                    await Until(()=>Field<Capabilities>(main,"_caps").Available&&!Field<bool>(main,"_busy")&&!Field<bool>(main,"_detecting"));
                    var engine=Field<RecorderEngine>(main,"_engine");
                    var hud=Field<RecordingHud>(main,"_recordingHud");
                    var settings=Field<AppSettings>(main,"_settings");
                    Check(settings.Scope==RecordingScope.Region&&Field<Rectangle>(main,"_region")==rememberedRegion&&Field<RegionIndicator>(main,"_regionIndicator").Visible,"Application.Run(main): startup restores the saved recording mode and native dashed region");
                    Check(Field<RegionIndicator>(main,"_regionIndicator").Editable&&!hud.Visible,"Application.Run(main): restored region is editable without a premature recording HUD");
                    var work=Screen.PrimaryScreen!.WorkingArea;
                    var bounds=new Rectangle(work.Left+100,work.Top+240,320,180);
                    typeof(MainForm).GetField("_region",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,bounds);
                    settings.Scope=RecordingScope.Region;
                    Invoke(main,"PlanChanged");
                    Field<SoftButton>(main,"_recordButton").PerformClick();
                    await Until(()=>engine.State==RecorderState.Recording&&!Field<bool>(main,"_busy"));
                    Check(!main.Visible&&hud.Visible&&PresentationNative.IsWindowVisible(hud.Handle),"Application.Run(main): actual start button hides main and leaves native HUD visible");
                    Check(PresentationNative.GetWindow(hud.Handle,4)!=main.Handle,"Application.Run(main): native HUD owner is not the hidden main window");
                    Check(Screen.AllScreens.Any(s=>s.WorkingArea.Contains(hud.Bounds)),"Application.Run(main): entire HUD is on a connected display");
                    Console.WriteLine($"HUD native visibility={PresentationNative.IsWindowVisible(hud.Handle)} bounds={hud.Bounds} owner={PresentationNative.GetWindow(hud.Handle,4)} affinity-excluded={hud.CaptureExcluded}");
                    // Save what ordinary screen sharing can see WITHOUT altering display affinity.
                    PresentationNative.Snapshot(hud,"main-context-hud-screen");
                    Console.WriteLine($"HUD card screen pixel={PresentationNative.Pixel(hud.Location+new Size(5,30))}");
                    await Task.Delay(150);
                    Check(!hud.CaptureExcluded&&PresentationNative.Pixel(hud.Location+new Size(5,30)).ToArgb()==Color.White.ToArgb(),"Application.Run(main): default HUD appears in actual desktop pixels without changing affinity");
                    Check(hud.ShowInTaskbar&&(PresentationNative.GetWindowLong(hud.Handle,-20)&0x40000)!=0&&(PresentationNative.GetWindowLong(hud.Handle,-20)&0x80)==0,"Application.Run(main): HUD has an independent taskbar entry rather than a hidden tool-window style");
                    hud.Location=new Point(-30000,-30000);
                    Invoke(main,"SyncRecordingUi"); await Task.Delay(80);
                    Check(Screen.AllScreens.Any(s=>s.WorkingArea.Contains(hud.Bounds)),"Application.Run(main): offscreen HUD is moved back onto a connected display");
                    Field<CheckBox>(main,"_excludeHud").Checked=true; await Task.Delay(80);
                    Check(AppSettings.Load().ExcludeHudFromCapture&&hud.CaptureExcluded,"Application.Run(main): optional exclusion is applied and saved");
                    Invoke(main,"ShowRecordingControls"); await Task.Delay(100);
                    Check(!hud.CaptureExcluded&&!AppSettings.Load().ExcludeHudFromCapture&&PresentationNative.Pixel(hud.Location+new Size(5,30)).ToArgb()==Color.White.ToArgb(),"Application.Run(main): recovery entry disables exclusion and makes the HUD visible to screen sharing");
                    PresentationNative.Snapshot(hud,"main-context-compatible-hud");
                    hud.Close(); await Task.Delay(50);
                    Check(!hud.IsDisposed&&hud.Visible&&main.Visible&&engine.State==RecorderState.Recording,"Application.Run(main): taskbar close restores main without disposing active recording controls");
                    main.Hide();
                    await Task.Delay(1200);
                    Field<SoftButton>(hud,"_pause").PerformClick();
                    await Until(()=>engine.State==RecorderState.Paused&&!Field<bool>(main,"_busy"));
                    Check(Field<Label>(hud,"_elapsed").Text!="00:00:00"&&Field<SoftButton>(hud,"_pause").Text=="继续","Application.Run(main): HUD timer and pause work while main stays hidden");
                    Field<SoftButton>(hud,"_pause").PerformClick();
                    await Until(()=>engine.State==RecorderState.Recording&&!Field<bool>(main,"_busy"));
                    await Task.Delay(500);
                    Field<SoftButton>(hud,"_stop").PerformClick();
                    await Until(()=>engine.State==RecorderState.Idle&&!Field<bool>(main,"_busy"));
                    Check(main.Visible&&!hud.Visible&&File.Exists(engine.OutputFile),"Application.Run(main): HUD stop saves the actual video and restores main");
                    // Repeat real start/stop for window and full-screen scopes in the main message loop.
                    using var target=new Form { StartPosition=FormStartPosition.Manual, Bounds=new Rectangle(work.Left+100,work.Top+260,400,240), BackColor=Color.DarkGreen, ShowInTaskbar=false };
                    target.Show();
                    Field<ChoiceBox>(main,"_fpsBox").SelectedIndex=1;
                    foreach(var scope in new[]{RecordingScope.Window,RecordingScope.FullScreen})
                    {
                        settings.Scope=scope;
                        if(scope==RecordingScope.Window)typeof(MainForm).GetField("_window",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,new WindowInfo(target.Handle,"Regression recording target",target.Bounds));
                        Invoke(main,"PlanChanged");
                        Field<SoftButton>(main,"_recordButton").PerformClick();
                        await Until(()=>engine.State==RecorderState.Recording&&!Field<bool>(main,"_busy"));
                        await Task.Delay(200);
                        Check(!main.Visible&&PresentationNative.IsWindowVisible(hud.Handle)&&PresentationNative.Pixel(hud.Location+new Size(5,30)).ToArgb()==Color.White.ToArgb(),$"Application.Run(main): {scope} recording shows opaque HUD while main is hidden");
                        Field<SoftButton>(hud,"_stop").PerformClick();
                        await Until(()=>engine.State==RecorderState.Idle&&!Field<bool>(main,"_busy"));
                        Check(main.Visible&&!hud.Visible&&File.Exists(engine.OutputFile),$"Application.Run(main): {scope} HUD stop saves and restores main");
                    }
                    settings.Scope=RecordingScope.FullScreen;
                    Field<CheckBox>(main,"_countdown").Checked=true;
                    Field<SoftButton>(main,"_recordButton").PerformClick(); await Task.Delay(150);
                    Check(!main.Visible&&Field<Label>(hud,"_status").Text.Contains("3 秒")&&PresentationNative.Pixel(hud.Location+new Size(5,30)).ToArgb()==Color.White.ToArgb(),"Application.Run(main): countdown is visibly rendered after main hides");
                    Field<SoftButton>(hud,"_stop").PerformClick(); await Until(()=>Field<Capabilities>(main,"_caps").Available&&!Field<bool>(main,"_busy")&&!Field<bool>(main,"_detecting"));
                    Check(main.Visible&&!hud.Visible&&engine.State==RecorderState.Idle,"Application.Run(main): countdown cancel restores main and removes HUD");
                    Console.WriteLine($"ALL {_passed} MAIN-CONTEXT CHECKS PASSED");
                }
                catch(Exception e) { Console.Error.WriteLine(e);contextExit=1; }
                finally
                {
                    var engine=Field<RecorderEngine>(main,"_engine");
                    if(engine.State!=RecorderState.Idle)try { await InvokeAsync(main,"StopRecordingAsync"); }catch(Exception e){Console.Error.WriteLine(e.Message);}
                    main.Close();
                }
            };
            Application.Run(main);
            return contextExit;
        }
        int exitCode = 0;
        using var host = new Form { ShowInTaskbar = false, Opacity = 0, Size = new Size(1, 1) };
        host.Shown += async (_, _) =>
        {
            host.Hide();
            try
            {
                _caps = await HardwareProbe.ProbeAsync(Path.GetFullPath(args[0]));
                Check(_caps.Available && _caps.X264Rgb, "FFmpeg capability probe");
                if(args.Contains("--resource-profile")){await TestResourceUsageAsync(false);return;}
                if(args.Contains("--resources")){await TestResourceUsageAsync();Console.WriteLine($"ALL {_passed} RESOURCE CHECKS PASSED");return;}
                if(args.Contains("--bundle")){await TestBundledToolsAsync();Console.WriteLine($"ALL {_passed} BUNDLE CHECKS PASSED");return;}
                if(args.Contains("--themes")){await TestThemesAsync();Console.WriteLine($"ALL {_passed} THEME CHECKS PASSED");return;}
                if(args.Contains("--visual-polish")){await TestVisualPolishAsync();Console.WriteLine($"ALL {_passed} VISUAL POLISH CHECKS PASSED");return;}
                if(args.Contains("--scrollbars")){await TestScrollbarsAsync();Console.WriteLine($"ALL {_passed} SCROLLBAR CHECKS PASSED");return;}
                if(args.Contains("--localization")){await TestLocalizationAsync();Console.WriteLine($"ALL {_passed} LOCALIZATION CHECKS PASSED");return;}
                if(args.Contains("--media-only")){await TestCodeAuditAsync(false);await TestHevcLosslessAsync();await TestCoreAsync();await TestAudioTimelineAsync();Console.WriteLine($"ALL {_passed} MEDIA CHECKS PASSED");return;}
                if(args.Contains("--audit")){await TestCodeAuditAsync(_captureEnabled);Console.WriteLine($"ALL {_passed} CODE AUDIT CHECKS PASSED");return;}
                if(args.Contains("--second-audit")){await TestSecondAuditAsync();Console.WriteLine($"ALL {_passed} AUDIT CHECKS PASSED");return;}
                if(args.Contains("--hevc-lossless")){await TestHevcLosslessAsync(_captureEnabled);Console.WriteLine($"ALL {_passed} HEVC CHECKS PASSED");return;}
                if(args.Contains("--platform")){await TestPlatformAsync();Console.WriteLine($"ALL {_passed} PLATFORM CHECKS PASSED");return;}
                if(args.Contains("--audio-timeline")){await TestAudioTimelineAsync();Console.WriteLine($"ALL {_passed} AUDIO TIMELINE CHECKS PASSED");return;}
                if(args.Contains("--stability")){await TestStabilityAsync();Console.WriteLine($"ALL {_passed} STABILITY CHECKS PASSED");return;}
                if(args.Contains("--long-split")){await TestLongSplitAsync();Console.WriteLine($"ALL {_passed} LONG SPLIT CHECKS PASSED");return;}
                if(args.Contains("--output")){await TestOutputUpgradeAsync();Console.WriteLine($"ALL {_passed} OUTPUT CHECKS PASSED");return;}
                if(args.Contains("--encoder-choices")){await TestEncoderChoicesAsync();Console.WriteLine($"ALL {_passed} ENCODER CHOICE CHECKS PASSED");return;}
                if(args.Contains("--profiles")){await TestEncodingProfilesAsync();Console.WriteLine($"ALL {_passed} PROFILE CHECKS PASSED");return;}
                if(args.Contains("--completion")){await TestCompletionAsync();Console.WriteLine($"ALL {_passed} COMPLETION CHECKS PASSED");return;}
                if(args.Contains("--region-memory")){await TestRegionMemoryAsync();Console.WriteLine($"ALL {_passed} REGION MEMORY CHECKS PASSED");return;}
                if(args.Contains("--features")){await TestFeaturesAsync();Console.WriteLine($"ALL {_passed} FEATURE CHECKS PASSED");return;}
                if (args.Contains("--presentation")) { await TestPresentationAsync(); Console.WriteLine($"ALL {_passed} PRESENTATION CHECKS PASSED"); return; }
                if (args.Contains("--layout"))
                {
                    await TestCoreAsync(); await TestUiAsync(); await TestScaledLayoutsAsync();
                    Console.WriteLine($"ALL {_passed} LAYOUT CHECKS PASSED"); return;
                }
                await TestBundledToolsAsync();
                await TestThemesAsync();
                await TestResourceUsageAsync();
                await TestVisualPolishAsync();
                await TestScrollbarsAsync();
                await TestLocalizationAsync();
                await TestCodeAuditAsync(_captureEnabled);
                await TestEncoderChoicesAsync();
                await TestHevcLosslessAsync(_captureEnabled);
                await TestCoreAsync();
                await TestUiAsync();
                await TestFeaturesAsync();
                await TestCompletionAsync();
                await TestEncodingProfilesAsync();
                await TestOutputUpgradeAsync();
                await TestStabilityAsync();
                await TestRegionMemoryAsync();
                await TestSecondAuditAsync();
                await TestPresentationAsync();
                await TestRecoveryUiAsync();
                await TestScaledLayoutsAsync();
                if (args.Contains("--capture")) await TestCaptureAsync();
                Console.WriteLine($"ALL {_passed} CHECKS PASSED");
            }
            catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
            finally { host.Close(); }
        };
        Application.Run(host);
        return exitCode;
    }
    private static async Task DriveRegionSelectionAsync(MainForm form,Rectangle bounds,bool cancel)
    {
        using var driver=new System.Windows.Forms.Timer { Interval=30 };
        bool handled=false;
        driver.Tick+=(_,_)=>
        {
            var picker=Application.OpenForms.OfType<RegionPicker>().FirstOrDefault();
            if(picker is null||handled)return;
            handled=true; driver.Stop();
            Check(!Field<RegionIndicator>(form,"_regionIndicator").Visible&&!Field<RecordingHud>(form,"_recordingHud").Visible,"Region picker temporarily hides existing overlays");
            if(cancel) { picker.DialogResult=DialogResult.Cancel; picker.Close(); return; }
            var start=picker.PointToClient(bounds.Location); var end=start+bounds.Size;
            foreach(var item in new[] { ("OnMouseDown",start),("OnMouseMove",end),("OnMouseUp",end) })
                typeof(RegionPicker).GetMethod(item.Item1,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(picker,new object[]{new MouseEventArgs(MouseButtons.Left,1,item.Item2.X,item.Item2.Y,0)});
        };
        driver.Start();
        await (Task)typeof(MainForm).GetMethod("SelectScopeAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,new object[]{RecordingScope.Region})!;
        Check(handled,"Region picker dialog lifecycle ran to completion");
    }    private static async Task TestPresentationAsync()
    {
        new AppSettings { FfmpegPath=_caps.Ffmpeg, OutputFolder=_root, Countdown=true, Encoder=EncoderChoice.X264Rgb }.Save();
        using var form = new MainForm { StartPosition=FormStartPosition.Manual, Location=Screen.PrimaryScreen!.WorkingArea.Location+new Size(80,70), TopMost=true };
        form.Show(); await Until(()=>Field<Capabilities>(form,"_caps").Available&&!Field<bool>(form,"_busy")&&!Field<bool>(form,"_detecting")); await Task.Delay(100);
        Check(form.Opacity==1 && form.ClientSize==form.Size && form.PointToScreen(Point.Empty)==form.Location, "Opaque native window uses the entire rectangle as client content");
        Check((PresentationNative.GetWindowLong(form.Handle,-16)&0x00C00000)==0, "Actual native window style contains no system caption or border");
        WindowNative.RaiseWithoutActivation(form.Handle); await Task.Delay(100);
        Check(PresentationNative.Pixel(form.Location+new Size(20,15)).ToArgb()==Ui.Background.ToArgb(),
            "Own main window is visible before native screenshot checks");
        PresentationNative.Snapshot(form,"native-main-active");
        var durationBox=Field<ChoiceBox>(form,"_limitBox");
        Check(durationBox.Height>=Ui.S(durationBox,30) && durationBox.Parent!.ClientRectangle.Contains(durationBox.Bounds),
            "Duration dropdown field fits its complete border and text inside the helper page row");
        var settings=Field<AppSettings>(form,"_settings");
        var bounds=new Rectangle(form.Left+180,form.Top+180,600,320);
        var border=Field<RegionIndicator>(form,"_regionIndicator"); var hud=Field<RecordingHud>(form,"_recordingHud");
        await DriveRegionSelectionAsync(form,bounds,false);
        Check(settings.Scope==RecordingScope.Region&&Field<Rectangle>(form,"_region")==bounds&&border.Visible&&!hud.Visible,
            "Actual region confirmation shows only the topmost border without a premature HUD");
        await DriveRegionSelectionAsync(form,bounds,true);
        Check(Field<Rectangle>(form,"_region")==bounds&&border.Visible&&!hud.Visible,
            "Cancelling region selection preserves the previous border and keeps the idle HUD hidden");
        Field<List<SoftButton>>(form,"_settingsTabs")[2].PerformClick();
        var floating=Field<SoftButton>(form,"_floatingButton");
        floating.PerformClick(); Invoke(form,"ShowRecordingControls");
        Check(!floating.Enabled&&!Field<ToolStripItem>(form,"_floatingMenu").Enabled&&!hud.Visible,
            "Idle helper, tray and direct recovery paths cannot open a premature floating window");
        int inset=border.Inset;
        Check(border.Region!.IsVisible(inset+20,inset-2)&&!border.Region.IsVisible(inset+14,inset-2)&&border.Region.IsVisible(inset-2,inset-2),
            "Selection region consists of visible dashes, transparent gaps and defined corners");
        Field<CheckBox>(form,"_countdown").Checked=false; Field<CheckBox>(form,"_minimize").Checked=false;
        Field<CheckBox>(form,"_systemSound").Checked=false; Field<CheckBox>(form,"_microphone").Checked=false;
        Field<CheckBox>(form,"_dxgi").Checked=false; Field<ChoiceBox>(form,"_fpsBox").SelectedIndex=1;
        await InvokeAsync(form,"StartRecordingAsync");
        Check(hud.Visible&&floating.Enabled&&Field<ToolStripItem>(form,"_floatingMenu").Enabled,
            "Starting a real recording shows the floating controls and enables recovery entries");
        Check(!border.CaptureExcluded, "Outside-crop border remains visible in desktop screenshots");
        hud.Location=new Point(form.Left+650,form.Top+130);
        using var cover=new Form { FormBorderStyle=FormBorderStyle.None, ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, TopMost=true,
            Bounds=new Rectangle(form.Left+100,form.Top+70,1000,600), BackColor=Color.FromArgb(40,70,60) };
        WindowNative.RaiseWithoutActivation(form.Handle); await Task.Delay(80);
        cover.Show(); cover.Activate(); await Task.Delay(80);
        // Activation can be denied by the desktop. Compare actual focus across the refresh itself.
        var foregroundBeforeRefresh=PresentationNative.GetForegroundWindow();
        Invoke(form,"SyncRecordingUi");
        Check(PresentationNative.GetForegroundWindow()==foregroundBeforeRefresh, "Refreshing overlay z-order does not steal foreground focus");
        await Task.Delay(80);
        Check((PresentationNative.GetWindowLong(border.Handle,-20)&8)!=0&&(PresentationNative.GetWindowLong(hud.Handle,-20)&8)!=0, "Region border and HUD have native HWND_TOPMOST state");
        Check(PresentationNative.IsAbove(border.Handle,cover.Handle)&&PresentationNative.IsAbove(hud.Handle,cover.Handle), "Border and HUD rise above a newly activated competing topmost window");
        bool excluded=hud.CaptureExcluded;
        try
        {
            // Diagnostic screenshots temporarily include ONLY our own HUD, then restore affinity.
            if(excluded) PresentationNative.SetWindowDisplayAffinity(hud.Handle,0);
            border.Refresh();await Task.Delay(100);
            Color darkRing=PresentationNative.Pixel(new Point(bounds.Left-4,bounds.Top+20));
            Color lightRing=PresentationNative.Pixel(new Point(bounds.Left-3,bounds.Top+20));
            Check(BorderContrast(darkRing,lightRing)>10&&Math.Max(BorderContrast(darkRing,cover.BackColor),BorderContrast(lightRing,cover.BackColor))>=3,
                "Screen pixels show both high-contrast layers of the selection ring above the competing window");
            Point gapSample=new(bounds.Left-2,bounds.Top+12);
            Color gapPixel=PresentationNative.Pixel(gapSample);
            // The desktop compositor may quantize a channel by one level. Compare
            // with the same live background and validate the native hole as well.
            Color backgroundPixel=PresentationNative.Pixel(new Point(bounds.Left-24,bounds.Top+12));
            static bool SameBackground(Color a,Color b)=>Math.Abs(a.R-b.R)<=2&&Math.Abs(a.G-b.G)<=2&&Math.Abs(a.B-b.B)<=2;
            Check(SameBackground(gapPixel,backgroundPixel)&&!border.Region!.IsVisible(border.PointToClient(gapSample))&&WindowFromPoint(gapSample)==cover.Handle,
                "Dashed selection gaps reveal the underlying window without residual gray pixels");
            Check(SameBackground(PresentationNative.Pixel(bounds.Location+new Size(20,20)),backgroundPixel), "Selection interior stays visually empty above another application window");
            Check(PresentationNative.Pixel(hud.Location+new Size(5,30)).ToArgb()==Color.White.ToArgb(), "Screen pixels show the HUD card above the competing window");
            // Overlay assertions above intentionally cover the main. Raise only our inactive fixture
            // before sampling its frame, without changing the current foreground window.
            var frameForeground=PresentationNative.GetForegroundWindow();
            WindowNative.RaiseWithoutActivation(form.Handle); await Task.Delay(100);
            Check(PresentationNative.GetForegroundWindow()==frameForeground,"Frame pixel fixture is raised without changing activation");
            bool cleanFrame=new[] { new Point(20,15),new Point(3,300),new Point(form.Width-3,300),new Point(200,form.Height-3) }
                .All(p=>PresentationNative.Pixel(form.Location+new Size(p)).ToArgb()==Ui.Background.ToArgb());
            Check(cleanFrame,"Deactivation does not repaint a classic title bar or gray resize frame");
            PresentationNative.Snapshot(form,"native-overlays-topmost");
        }
        finally { if(excluded) PresentationNative.SetWindowDisplayAffinity(hud.Handle,0x11); }
        Descendants(form).OfType<CaptionButton>().Single(b=>b.Kind=="minimize").PerformClick();
        Invoke(form,"SyncRecordingUi");
        Check(hud.Visible&&border.Visible&&hud.Owner is null&&border.Owner is null,"Main-window minimization leaves independent overlays visible");
        Invoke(form,"RestoreWindow"); Invoke(form,"SyncRecordingUi"); await Task.Delay(50);
        Check(PresentationNative.Pixel(form.Location+new Size(20,15)).ToArgb()==Ui.Background.ToArgb(),"Restore keeps the themed caption free of native repaint artifacts");
        await InvokeAsync(form,"StopRecordingAsync");
        Check(!hud.Visible&&border.Visible&&!floating.Enabled,"Stopping saves the recording and returns to a border-only idle selection");
        Field<CheckBox>(form,"_countdown").Checked=true;
        Field<SoftButton>(form,"_recordButton").PerformClick(); await Task.Delay(50);
        Check(hud.Visible&&Field<CancellationTokenSource>(form,"_countdownCancellation") is not null&&Field<Label>(hud,"_status").Text.Contains("3 秒"),
            "Explicit main-window start displays the cancellable countdown in the HUD");
        Field<SoftButton>(hud,"_stop").PerformClick(); await Until(()=>!Field<bool>(form,"_busy"));
        Check(Field<RecorderEngine>(form,"_engine").State==RecorderState.Idle&&!hud.Visible&&border.Visible,
            "Cancelling countdown removes the HUD and retains only the dashed selection border");
        await DriveRegionSelectionAsync(form,new Rectangle(bounds.X+20,bounds.Y+20,320,180),false);
        Check(border.Visible&&!hud.Visible,"Reselecting after a recording never reopens an idle floating window");
        Snapshot(form,"selection-idle-dashed-no-hud");
        cover.Close(); form.Close();
    }    private static async Task TestSecondAuditAsync()
    {
        var invalid = new AppSettings { StopAfterMinutes = -1 }; invalid.Normalize();
        Check(invalid.StopAfterMinutes == 0, "Invalid duration limit falls back to manual stop");
        var limit = new RecordingLimit(1);
        Check(!limit.Reached(TimeSpan.FromSeconds(59)) && limit.Reached(TimeSpan.FromMinutes(1)) && limit.Remaining(TimeSpan.FromMinutes(2)) == TimeSpan.Zero,
            "Duration boundary is exact and remaining time never becomes negative");
        Check(RecordingLimit.Format(TimeSpan.FromHours(25)) == "25:00:00", "Long recordings retain hours beyond one day");

        var writes = Enumerable.Range(0, 16).Select(index => Task.Run(() => new AppSettings { OutputFolder = _root, StopAfterMinutes = index % 2 == 0 ? 5 : 10 }.Save()));
        await Task.WhenAll(writes);
        Check(AppSettings.Load().StopAfterMinutes is 5 or 10 && Directory.GetFiles(_root, "settings.json.*.tmp").Length == 0,
            "Concurrent atomic settings saves leave valid JSON and no temporary files");

        string instanceName = "ScreenLossless-test-" + Guid.NewGuid().ToString("N");
        using (var primary = new SingleInstance(instanceName))
        {
            Check(primary.IsPrimary, "First launch owns the application instance");
            int requests = 0; primary.Listen(() => Interlocked.Increment(ref requests));
            using (var secondary = new SingleInstance(instanceName))
            {
                Check(!secondary.IsPrimary && await secondary.ActivateExistingAsync(), "Second launch requests activation instead of opening another recorder");
            }
            var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(RegressionTests).Assembly.GetName().Name + ".dll"));
            start.ArgumentList.Add("--instance-probe"); start.ArgumentList.Add(instanceName);
            using var child = System.Diagnostics.Process.Start(start)!;
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(child.ExitCode == 0 && requests == 2, "Independent process honors the mutex and receives an activation acknowledgement");
            using var stalled = new System.IO.Pipes.NamedPipeClientStream(".", instanceName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            await stalled.ConnectAsync(3000);
            using var next = new SingleInstance(instanceName);
            Check(await next.ActivateExistingAsync() && requests == 3, "An incomplete activation request times out without blocking future launches");
        }
        using (var replacement = new SingleInstance(instanceName))
            Check(replacement.IsPrimary, "Closing the first instance releases the launch guard");
        using (var noServer = new SingleInstance("ScreenLossless-absent-" + Guid.NewGuid().ToString("N")))
            Check(!await noServer.ActivateExistingAsync(120), "Missing activation server returns promptly instead of hanging launch");

        new AppSettings { FfmpegPath=_caps.Ffmpeg, OutputFolder=_root, Encoder=EncoderChoice.X264Rgb, RecordSystemAudio=false,
            RecordMicrophone=false, Countdown=false, MinimizeWhenRecording=true, UseDxgi=false, Fps=30 }.Save();
        using var main = new MainForm();
        main.Show(); await Until(() => !Field<bool>(main,"_busy") && Field<Capabilities>(main,"_caps").Available);
        var settings = Field<AppSettings>(main,"_settings"); var engine=Field<RecorderEngine>(main,"_engine"); var hud=Field<RecordingHud>(main,"_recordingHud");
        engine.Message += message =>
        {
            if (!message.StartsWith("正在合并暂停前后的片段", StringComparison.Ordinal)) return;
            string evidence = Path.Combine(_root, "timed-source-frames"); Directory.CreateDirectory(evidence);
            foreach (string file in Directory.EnumerateFiles(engine.WorkingDirectory, "*.mkv"))
                File.Copy(file, Path.Combine(evidence, Path.GetFileName(file)), true);
        };
        var work=Screen.PrimaryScreen!.WorkingArea;
        typeof(MainForm).GetField("_region", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(main,new Rectangle(work.Left+100,work.Top+200,320,180));
        settings.Scope=RecordingScope.Region;
        var box=Field<ChoiceBox>(main,"_limitBox"); box.SelectedIndex=1;
        Check(AppSettings.Load().StopAfterMinutes==1, "Themed duration dropdown persists the selected limit");
        Field<List<SoftButton>>(main,"_settingsTabs")[2].PerformClick(); main.PerformLayout();
        Snapshot(main,"helpers-duration-limit");
        Field<SoftButton>(main,"_recordButton").PerformClick();
        await Until(() => engine.State==RecorderState.Recording && !Field<bool>(main,"_busy"));
        Check(!box.Enabled && !main.Visible && hud.Visible, "Timed recording locks its limit while the main window hides and HUD remains visible");
        await InvokeAsync(main,"TogglePauseAsync");
        string remaining=Field<Label>(hud,"_started").Text;
        await Task.Delay(1300);
        Check(engine.State==RecorderState.Paused && Field<Label>(hud,"_started").Text==remaining && remaining.Contains("剩余"),
            "Paused timed recording freezes the remaining duration in the floating controls");
        main.RequestActivation();
        Check(main.Visible && engine.State==RecorderState.Paused, "Repeat-launch activation restores the UI without interrupting a paused session");
        main.Hide();
        await InvokeAsync(main,"TogglePauseAsync");
        Console.WriteLine("RUNNING: waiting for the actual one-minute automatic stop and save");
        var deadline=DateTime.UtcNow.AddSeconds(75);
        while (engine.State!=RecorderState.Idle && DateTime.UtcNow<deadline) await Task.Delay(250);
        Check(engine.State==RecorderState.Idle && File.Exists(engine.OutputFile) && main.Visible && !hud.Visible,
            "Actual one-minute recording automatically stops, saves and restores the main window");
        Check(engine.Duration >= TimeSpan.FromMinutes(1) && engine.Duration < TimeSpan.FromSeconds(64),
            "Automatic stop uses cumulative capture time and tolerates only the UI timer interval");
        long decodedFrames = (await Hashes(engine.OutputFile)).LongLength;
        Console.WriteLine($"TIMED FRAMES: decoded={decodedFrames}, reported={engine.FrameCount}, file={engine.OutputFile}");
        Check(decodedFrames==engine.FrameCount, "Timed stop preserves every captured frame across pause and resume");
        Check(engine.LastStopReason.Contains("自动停止并保存"), "Automatic completion retains the current stop reason");
        main.Close();
    }
    private static async Task TestCoreAsync()
    {
        await TestMuxFramePreservationAsync();
        var settings = new AppSettings { OutputFolder = "", Scope = (RecordingScope)99, Encoder = (EncoderChoice)99, Fps = -1, SystemVolume = float.NaN, MicrophoneVolume = 3 };
        settings.Normalize();
        Check(settings.Fps == 0 && settings.Encoder == EncoderChoice.Auto && settings.Scope == RecordingScope.FullScreen && settings.SystemVolume == 1 && settings.MicrophoneVolume == 1 && settings.OutputFolder.Length > 0, "Corrupt settings normalize safely");
        bool rejected = false;
        try { RecordingManifest.ResolveFile(_root, "../outside.mkv"); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Recovery rejects path traversal");
        string directory = Path.Combine(_root, "中文测试'目录", ".screenlossless-work", "session1");
        Directory.CreateDirectory(directory);
        string video1 = Path.Combine(directory, "video-001.mkv"), video2 = Path.Combine(directory, "video-002.mkv");
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=128x72:rate=10:duration=1", "-c:v", "libx264rgb", "-preset", "ultrafast", "-crf", "0", "-pix_fmt", "bgr0", video1);
        File.Copy(video1, video2);
        using (var empty = new NAudio.Wave.WaveFileWriter(Path.Combine(directory, "audio-system-002.wav"), new NAudio.Wave.WaveFormat(48000, 16, 2))) { }
        File.WriteAllText(Path.Combine(directory, "audio-mic-002.wav"), "corrupt audio");
        string system = Path.Combine(directory, "audio-system-001.wav"), mic = Path.Combine(directory, "audio-mic-001.wav");
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1:sample_rate=48000", "-ac", "2", system);
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=880:duration=1:sample_rate=44100", "-ac", "1", mic);
        var manifest = new RecordingManifest
        {
            TargetFile = "recover.mkv", SystemAudio = true, Microphone = true, MixAudio = false,
            Segments = new() { new("video-001.mkv", Path.GetFileName(system), Path.GetFileName(mic)), new("video-002.mkv", "audio-system-002.wav", "audio-mic-002.wav") }
        };
        manifest.Save(directory);
        Check(RecordingManifest.Load(directory).Segments.Count == 2, "Session manifest roundtrip");
        string outputFolder = Path.GetDirectoryName(Path.GetDirectoryName(directory))!;
        Check(RecordingFinalizer.Discover(outputFolder).Count == 1, "Unfinished session discovered");
        string recovered = await RecordingFinalizer.ExportAsync(_caps, directory, manifest, Console.WriteLine);
        string streams = await Run("-hide_banner", "-i", recovered, "-map", "0", "-c", "copy", "-f", "null", "-");
        Check(Regex.Matches(streams.Split("Stream mapping:")[0], @"Stream #0:\d+.*Audio: flac").Count == 2 && streams.Contains("48000 Hz, stereo"), "Separate audio tracks stay compatible with missing devices");
        var originalHashes = await Hashes(video1); var outputHashes = await Hashes(recovered);
        Check(outputHashes.SequenceEqual(originalHashes.Concat(originalHashes)), "Pause concat preserves every video pixel and all 20 frames");
        Check(File.Exists(video1) && File.Exists(video2) && RecordingFinalizer.Discover(outputFolder).Count == 0, "Recovery keeps originals and marks completion");
        byte[] previous = await File.ReadAllBytesAsync(recovered);
        manifest.CompletedFile = null; manifest.MixAudio = true;
        string mixed = await RecordingFinalizer.ExportAsync(_caps, directory, manifest, Console.WriteLine);
        streams = await Run("-hide_banner", "-i", mixed, "-map", "0", "-c", "copy", "-f", "null", "-");
        Check(Regex.Matches(streams.Split("Stream mapping:")[0], @"Stream #0:\d+.*Audio: flac").Count == 1, "Mixed audio exports one playable FLAC track");
        Check(mixed != recovered && previous.SequenceEqual(await File.ReadAllBytesAsync(recovered)), "Existing recording never overwritten");
        string missing = Path.Combine(directory, "missing-video.mkv");
        var bad = new RecordingManifest { TargetFile = "must-not-exist.mkv", Segments = new() { new(Path.GetFileName(missing), null, null) } };
        bool failed = false;
        try { await RecordingFinalizer.ExportAsync(_caps, directory, bad, Console.WriteLine); } catch (InvalidDataException) { failed = true; }
        Check(failed && !File.Exists(Path.Combine(outputFolder, bad.TargetFile)) && File.Exists(video1), "Failed finalization publishes no broken output and retains source");
        var withoutProbe = new Capabilities { Available = true, Ffmpeg = _caps.Ffmpeg, X264Rgb = true };
        var single = new RecordingManifest { TargetFile = "without-probe.mkv", Segments = new() { new("video-001.mkv", null, null) } };
        string fallback = await RecordingFinalizer.ExportAsync(withoutProbe, directory, single, Console.WriteLine);
        Check((await Hashes(fallback)).SequenceEqual(originalHashes), "FFmpeg-only duration fallback preserves frames");
        string ffv1 = Path.Combine(directory, "ffv1.mkv");
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=128x72:rate=10:duration=1", "-c:v", "ffv1", "-level", "3", "-pix_fmt", "bgr0", ffv1);
        Check((await Hashes(ffv1)).SequenceEqual(originalHashes), "FFV1 and x264rgb decode to identical RGB pixels");
        string rateA=Path.Combine(directory,"rate-a.mkv"),rateB=Path.Combine(directory,"rate-b.mkv");
        foreach(var item in new[]{(rateA,"30"),(rateB,"30000/1001")})
            await Run("-v","error","-y","-f","lavfi","-i","testsrc=size=128x72:rate="+item.Item2,"-frames:v","5","-c:v","libx264rgb","-crf","0",item.Item1);
        var rateManifest=new RecordingManifest {TargetFile="varying-rate.mkv",Segments=new(){new("rate-a.mkv",null,null),new("rate-b.mkv",null,null)}};
        string rateFile=await RecordingFinalizer.ExportAsync(_caps,directory,rateManifest,Console.WriteLine);
        Check((await Hashes(rateFile)).SequenceEqual((await Hashes(rateA)).Concat(await Hashes(rateB))),"29.97 and 30 FPS metadata variation concatenates without dropping or changing video frames");
        string different = Path.Combine(directory, "different.mkv");
        await Run("-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=130x72:rate=10:duration=1", "-c:v", "libx264rgb", "-crf", "0", different);
        var incompatible = new RecordingManifest { TargetFile = "incompatible.mkv", Segments = new() { new("video-001.mkv", null, null), new("different.mkv", null, null) } };
        if (!string.IsNullOrEmpty(_caps.Ffprobe))
        {
            bool incompatibleRejected = false;
            try { await RecordingFinalizer.ExportAsync(_caps, directory, incompatible, Console.WriteLine); } catch (InvalidDataException) { incompatibleRejected = true; }
            Check(incompatibleRejected && !File.Exists(Path.Combine(outputFolder, "incompatible.mkv")), "Incompatible pause segments rejected before publishing");
        }
        string legacy = Path.Combine(outputFolder, ".screenlossless-work", "legacy");
        Directory.CreateDirectory(legacy); File.Copy(video1, Path.Combine(legacy, "video-001.mkv"));
        Check(RecordingManifest.Load(legacy).Segments.Count == 1 && RecordingFinalizer.Discover(outputFolder).Count == 1, "Legacy work folder discovered without a manifest");
    }
    private static async Task TestUiAsync()
    {
        new AppSettings { FfmpegPath = _caps.Ffmpeg, OutputFolder = _root }.Save();
        using var form = new MainForm { Opacity = 0, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
        Field<RecordingHud>(form, "_recordingHud").Opacity = 0;
        form.Show();
        await Task.Delay(50);
        await Until(()=>Field<Capabilities>(form,"_caps").Available&&!Field<bool>(form,"_busy")&&!Field<bool>(form,"_detecting"));
        Field<TextBox>(form, "_folderBox").Text = _root;
        Field<CheckBox>(form, "_minimize").Checked = false;
        Field<CheckBox>(form, "_countdown").Checked = true;
        typeof(MainForm).GetField("_caps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, _caps);
        var appSettings = Field<AppSettings>(form, "_settings");
        appSettings.Scope = RecordingScope.FullScreen;
        appSettings.Encoder = EncoderChoice.X264Rgb;
        SelectCodec(Field<ChoiceBox>(form, "_codecBox"), EncoderChoice.X264Rgb);
        form.CreateControl(); form.PerformLayout();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(_root, "main-window.png"), ImageFormat.Png);
        }
        typeof(MainForm).GetMethod("ShowNotice", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { "先前操作提示" });
        typeof(MainForm).GetMethod("ShowNotice", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { "当前操作提示" });
        Check(Field<Label>(form, "_operationHint").Text == "当前操作提示", "Current operation replaces earlier messages without accumulating history");
        Check(!Descendants(form).Any(c => c is RichTextBox || c.Text == "导出日志"), "Main window has neither a scrolling log nor an export-log action");
        var tabs = Field<List<SoftButton>>(form, "_settingsTabs");
        var pages = Field<List<Control>>(form, "_settingsPages");
        var originalSize = form.Size;
        for (int size = 0; size < 2; size++)
        {
            form.Size = size == 0 ? originalSize : form.MinimumSize;
            await Task.Delay(80);
            CheckButtonsFit(form);
            var hint = Field<Label>(form, "_operationHint");
            Console.WriteLine($"LAYOUT size={form.Size} client={form.ClientSize} min={form.MinimumSize} hint={hint.Bounds} parent={hint.Parent!.ClientSize} pad={hint.Parent.Padding}");
            using (var diagShot = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(diagShot, new Rectangle(Point.Empty, diagShot.Size));
                diagShot.Save(Path.Combine(_root, $"layout-{size}.png"), ImageFormat.Png);
            }
            Check(hint.Width >= hint.Parent!.ClientSize.Width - hint.Parent.Padding.Horizontal - 2 && hint.Height >= 30,
                $"Current-operation hint fills its container at {(size == 0 ? "default" : "minimum")} window size (DPI {form.DeviceDpi})");
            for (int tab = 0; tab < tabs.Count; tab++)
            {
                tabs[tab].PerformClick(); form.PerformLayout(); await Task.Delay(30); CheckButtonsFit(form);
                Check(pages.Count(p => p.Visible) == 1 && pages[tab].Visible && tabs[tab].Active,
                    $"Settings tab {tab} is exclusively visible at window size {size}");
                if (tab == 2 && size == 0)
                {
                    var floating=Descendants(form).OfType<SoftButton>().Single(button=>button.Text=="显示录制浮窗");
                    var viewport=Field<Panel>(form,"_settingsHost");
                    Check(viewport.RectangleToScreen(viewport.ClientRectangle).Contains(floating.RectangleToScreen(floating.ClientRectangle)),
                        "Default helper viewport shows the entire floating-controls recovery button");
                }
                foreach (var button in new[] { "_recordButton", "_pauseButton", "_stopButton", "_testButton", "_refreshButton" })
                {
                    var control = Field<SoftButton>(form, button);
                    if (!control.Parent!.ClientRectangle.Contains(control.Bounds)) throw new Exception("Clipped action button: " + button);
                }
                using var shot = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                shot.Save(Path.Combine(_root, $"ui-size{size}-tab{tab}.png"), ImageFormat.Png);
            }
        }
        form.Size = originalSize; tabs[0].PerformClick();
        var record = Field<SoftButton>(form, "_recordButton");
        bool cornersClean = true;
        foreach (bool enabled in new[] { false, true })
        {
            record.Enabled = enabled;
            using var buttonShot = new Bitmap(record.Width, record.Height);
            record.DrawToBitmap(buttonShot, record.ClientRectangle);
            cornersClean &= buttonShot.GetPixel(0, 0).ToArgb() == Ui.ParentColor(record).ToArgb();
            cornersClean &= buttonShot.GetPixel(record.Width - 1, record.Height - 1).ToArgb() == Ui.ParentColor(record).ToArgb();
        }
        Check(cornersClean, "Rounded action buttons clear all corner pixels in enabled and disabled states");
        Field<Label>(form, "_operationHint").Text = "选择录制范围与编码，点击开始录制。";
        using (var finalShot = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(finalShot, new Rectangle(Point.Empty, finalShot.Size));
            finalShot.Save(Path.Combine(_root, "main-window.png"), ImageFormat.Png);
        }
        await TestThemedControlsAsync(form);
        await TestChromeAsync(form);
        await TestAuditAsync(form);
        Task start = InvokeAsync(form, "StartRecordingAsync");
        await Task.Delay(100);
        Check(Field<CancellationTokenSource>(form, "_countdownCancellation") is not null && Field<SoftButton>(form, "_recordButton").Enabled, "Countdown exposes an enabled cancel button");
        var hud = Field<RecordingHud>(form, "_recordingHud");
        Check(hud.Visible && Field<Label>(hud, "_status").Text.Contains("3 秒") && Field<SoftButton>(hud, "_stop").Enabled && !Field<SoftButton>(hud, "_pause").Enabled, "Floating countdown is visible with cancel enabled and pause disabled");
        Snapshot(hud, "hud-countdown"); CheckButtonsFit(hud);
        Field<SoftButton>(hud, "_stop").PerformClick();
        await start;
        Check(!hud.Visible, "Floating cancel closes the HUD and returns to idle");
        if (_captureEnabled) await TestHudCaptureAsync(form);
        Check(Field<RecorderEngine>(form, "_engine").State == RecorderState.Idle && !Directory.Exists(Path.Combine(_root, ".screenlossless-work")), "Cancel countdown never launches capture");
        using var recovery = new RecoveryForm(_root, _caps);
        recovery.CreateControl();
        Check(true, "Main and recovery forms initialize");
        form.Close();
    }
    private static async Task TestAuditAsync(MainForm form)
    {
        using(var box=new ChoiceBox())
        {
            box.Items.AddRange(new object[]{"alpha","beta","gamma"});box.SelectedIndex=1;
            box.Items.Insert(0,"zero");Check(box.SelectedItem as string=="beta"&&box.SelectedIndex==2,"Dropdown insertion before selection preserves the selected item");
            box.Items.RemoveAt(0);Check(box.SelectedItem as string=="beta"&&box.SelectedIndex==1,"Dropdown removal before selection preserves the selected item");
            int changes=0;box.SelectedIndexChanged+=(_,_)=>changes++;box.Items[1]="replacement";
            Check(changes==1&&Field<ToolTip>(box,"_tooltip").GetToolTip(box)=="replacement","Replacing a selected dropdown item updates notification and tooltip");
            box.Items.RemoveAt(1);Check(box.SelectedIndex==-1,"Removing the selected dropdown item clears selection");
        }
        using(var box=new ChoiceBox())
        {
            var choices=new[]{new AudioDeviceChoice("","默认设备"),new AudioDeviceChoice("speaker-1","测试播放设备")};
            AudioDevices.Populate(box,choices,"speaker-1");Check((box.SelectedItem as AudioDeviceChoice)?.Id=="speaker-1","Audio device picker restores its saved endpoint");
            AudioDevices.Populate(box,choices,"disconnected-id");Check(box.SelectedItem is AudioDeviceChoice {Available:false,Id:"disconnected-id"},"Disconnected endpoint remains explicitly selected instead of silently switching devices");
        }
        var durationBox=Field<ChoiceBox>(form,"_limitBox");
        Check(durationBox.Height>=Ui.S(durationBox,30) && durationBox.Parent!.ClientRectangle.Contains(durationBox.Bounds),
            "Duration dropdown field fits its complete border and text inside the helper page row");
        var settings=Field<AppSettings>(form,"_settings");
        var systemBox=Field<ChoiceBox>(form,"_systemDeviceBox");var micBox=Field<ChoiceBox>(form,"_microphoneDeviceBox");
        Check(systemBox.Items.Count>=1&&micBox.Items.Count>=1,"Audio device enumeration always offers default endpoint choices");
        bool wasSystem=Field<CheckBox>(form,"_systemSound").Checked,wasMic=Field<CheckBox>(form,"_microphone").Checked;
        Field<CheckBox>(form,"_systemSound").Checked=false;Field<CheckBox>(form,"_microphone").Checked=false;
        Check(!systemBox.Enabled&&!micBox.Enabled&&!Field<VolumeSlider>(form,"_systemVolume").Enabled&&!Field<CheckBox>(form,"_mixAudio").Enabled,"Disabled audio tracks disable endpoint, volume and irrelevant mixing controls");
        Field<CheckBox>(form,"_systemSound").Checked=true;Field<CheckBox>(form,"_microphone").Checked=true;
        Check(systemBox.Enabled&&micBox.Enabled&&Field<CheckBox>(form,"_mixAudio").Enabled,"Enabling both audio tracks enables endpoint and mixing controls immediately");
        Field<CheckBox>(form,"_systemSound").Checked=wasSystem;Field<CheckBox>(form,"_microphone").Checked=wasMic;
        var messages=new System.Collections.Concurrent.ConcurrentQueue<string>();
        using(var audio=new AudioSession(true,true,_root,987,messages.Enqueue,"invalid-endpoint-1","invalid-endpoint-2"))
        {
            audio.Start();await Until(()=>messages.Count>=2);await audio.StopAsync();
            Check(audio.SystemAudio is not null&&audio.MicrophoneAudio is not null&&!audio.SystemAvailable&&!audio.MicrophoneAvailable
                &&messages.All(m=>m.Contains("补静音")),"Unavailable explicit endpoints retain retryable silent tracks without selecting another device");
        }
        using(var picker=new RegionPicker {Opacity=0})
        {
            picker.Show();var origin=picker.Location;
            typeof(RegionPicker).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(picker,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,30,0)});
            typeof(RegionPicker).GetMethod("OnMouseUp",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(picker,new object[]{new MouseEventArgs(MouseButtons.Left,1,158,102,0)});
            Check(picker.SelectedBounds==new Rectangle(origin.X+30,origin.Y+30,128,72),"Region confirmation uses final mouse-up coordinates even when the last move event is missing");
        }
        using(var picker=new RegionPicker {Opacity=0})
        {
            picker.Show();typeof(RegionPicker).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(picker,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,30,0)});
            picker.Capture=false;Check(!Field<bool>(picker,"_selecting"),"Losing mouse capture clears the interrupted region drag");picker.Close();
        }
        var endpoint=AudioDevices.Enumerate(NAudio.CoreAudioApi.DataFlow.Render).FirstOrDefault(d=>d.Id.Length>0);
        if(endpoint is not null)
        {
            string audioFolder=Path.Combine(_root,"endpoint-capture");Directory.CreateDirectory(audioFolder);
            var audioMessages=new List<string>();using var audio=new AudioSession(true,false,audioFolder,1,audioMessages.Add,endpoint.Id);
            audio.Start();await Task.Delay(300);await audio.StopAsync();
            if(audio.SystemAudio is {} file&&File.Exists(file))
            {using var reader=new NAudio.Wave.WaveFileReader(file);Check(reader.Length>0,"Selected real playback endpoint starts, pads silence and writes a valid WAV");}
            else Console.WriteLine("SKIP actual endpoint capture: "+string.Join(";",audioMessages));
        }
        else Console.WriteLine("SKIP actual endpoint capture: no active playback endpoint");
        var workFit=new Rectangle(-1920,0,1920,1040);var fitted=MainForm.FitBounds(new Rectangle(-2200,-100,1860,1323),workFit);
        Check(workFit.Contains(fitted)&&fitted.Height==1040,"Oversized or offscreen main-window bounds fit the monitor work area, including negative coordinates");
        var systemOld=systemBox.SelectedIndex;var micOld=micBox.SelectedIndex;
        systemBox.Items.Add(new AudioDeviceChoice("fixture-system","测试播放设备"));micBox.Items.Add(new AudioDeviceChoice("fixture-mic","测试麦克风"));
        systemBox.SelectedIndex=systemBox.Items.Count-1;micBox.SelectedIndex=micBox.Items.Count-1;
        var planWithDevices=(RecordPlan)typeof(MainForm).GetMethod("GetPlan",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,null)!;
        Check(planWithDevices.SystemDeviceId=="fixture-system"&&planWithDevices.MicrophoneDeviceId=="fixture-mic"&&AppSettings.Load().MicrophoneDeviceId=="fixture-mic","Selected endpoint IDs persist and flow into the actual recording plan");
        systemBox.SelectedIndex=systemOld;micBox.SelectedIndex=micOld;Invoke(form,"RefreshAudioDevices");
        var desktop=new Rectangle(-1920,0,3840,1080);
        ScreenCapture.ValidateBounds(new Rectangle(-1800,50,128,72),desktop);
        bool rejected=false;try{ScreenCapture.ValidateBounds(new Rectangle(2000,50,128,72),desktop);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Disconnected or offscreen capture bounds are rejected rather than replaced by full desktop");
        rejected=false;try{ScreenCapture.ValidateBitmapSize(new Size(15000,10000));}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Oversized screenshot allocations are rejected before allocating a bitmap");
        var work=Screen.PrimaryScreen!.WorkingArea;
        using var target=new Form {FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(work.Left+100,work.Top+100,300,180),BackColor=Color.FromArgb(25,125,80)};
        target.Show();await Task.Delay(80);
        var capture=Rectangle.Inflate(target.Bounds,-10,-10);
        typeof(MainForm).GetField("_region",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(form,capture);
        settings.Scope=RecordingScope.Region;Invoke(form,"PlanChanged");
        string screenshots=Path.Combine(_root,"audit-screenshots");Field<TextBox>(form,"_folderBox").Text=screenshots;
        Task preview=InvokeAsync(form,"RefreshPreviewAsync");
        Check(Field<bool>(form,"_busy")&&!Field<SoftButton>(form,"_recordButton").Enabled&&!Field<ChoiceBox>(form,"_codecBox").Enabled,"Preview locks start and editable settings before hiding the main window");
        await preview;
        Check(Field<PreviewCanvas>(form,"_preview").Picture?.Size==capture.Size,"Preview captures exactly the selected rectangle");
        await InvokeAsync(form,"SaveScreenshotAsync");
        string png=Directory.GetFiles(screenshots,"*.png").Single();
        using(var bitmap=new Bitmap(png))Check(bitmap.Size==capture.Size&&bitmap.GetPixel(30,30).ToArgb()==target.BackColor.ToArgb(),"Screenshot pixels and dimensions match the selected target without overlays");
        Check(Field<SoftButton>(form,"_lastFileButton").Enabled&&Field<string>(form,"_lastSavedFile")==png,"Successful screenshot enables direct access to the actual saved file");
        typeof(MainForm).GetField("_region",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(form,new Rectangle(capture.Left,capture.Top,128,72));Invoke(form,"PlanChanged");
        Check(Field<PreviewCanvas>(form,"_preview").Picture is null,"Changing capture bounds clears the stale preview image");
        target.Close();settings.Scope=RecordingScope.FullScreen;Field<TextBox>(form,"_folderBox").Text=_root;Invoke(form,"PlanChanged");
        using var driver=new System.Windows.Forms.Timer {Interval=30};bool sawDiagnostics=false;
        driver.Tick+=(_,_)=>
        {
            var diagnostics=Application.OpenForms.OfType<HardwareDiagnosticsForm>().FirstOrDefault();if(diagnostics is null)return;
            driver.Stop();sawDiagnostics=true;
            Check(Field<int>(form,"_dialogDepth")==1&&!Field<SoftButton>(form,"_recordButton").Enabled,"Device diagnostics modal prevents starting a recording underneath it");
            diagnostics.Close();
        };
        driver.Start();Invoke(form,"ShowHardwareDiagnostics");
        Check(sawDiagnostics&&Field<int>(form,"_dialogDepth")==0&&Field<SoftButton>(form,"_recordButton").Enabled,"Closing diagnostics restores normal recording controls");
    }
    private static async Task Until(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (watch.Elapsed.TotalSeconds > 20) throw new TimeoutException("UI state did not settle: "+string.Join(";",Application.OpenForms.OfType<MainForm>().Select(f=>$"state={Field<RecorderEngine>(f,"_engine").State}, busy={Field<bool>(f,"_busy")}, hint={Field<Label>(f,"_operationHint").Text}"))); await Task.Delay(30); }
    }
    private static async Task TestChromeAsync(MainForm form)
    {
        Check(form.FormBorderStyle == FormBorderStyle.None && Descendants(form).OfType<CaptionButton>().Count() == 3, "Main window replaces native chrome with three themed controls");
        Check(ThemedForm.ResizeHitTest(new Size(800,600),new Point(1,1),6)==13 && ThemedForm.ResizeHitTest(new Size(800,600),new Point(799,599),6)==17 && ThemedForm.ResizeHitTest(new Size(800,600),new Point(200,200),6)==1, "Borderless window preserves corner resize and normal content hit testing");
        Rectangle original = form.Bounds;
        // Move only our invisible test window onto a real monitor for maximize calculations.
        form.Location = Screen.PrimaryScreen!.WorkingArea.Location + new Size(80,80);
        Rectangle normal = form.Bounds;
        Descendants(form).OfType<CaptionButton>().Single(b => b.Kind == "maximize").PerformClick(); await Task.Delay(70);
        Check(form.WindowState == FormWindowState.Maximized && Screen.FromHandle(form.Handle).WorkingArea.Contains(form.Bounds), "Themed maximize respects the monitor work area and taskbar");
        Descendants(form).OfType<CaptionButton>().Single(b => b.Kind == "restore").PerformClick(); await Task.Delay(70);
        Check(form.WindowState == FormWindowState.Normal && form.Bounds == normal, "Themed restore returns to the original window bounds");
        Descendants(form).OfType<CaptionButton>().Single(b => b.Kind == "minimize").PerformClick(); await Task.Delay(40);
        Check(form.WindowState == FormWindowState.Minimized, "Themed minimize invokes normal window behavior");
        var restoreMessage = Message.Create(form.Handle,0x112,new IntPtr(0xF120),IntPtr.Zero);
        typeof(MainForm).GetMethod("WndProc",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(form,new object[]{restoreMessage}); await Task.Delay(60);
        Check(form.WindowState==FormWindowState.Normal && form.Bounds==normal, "System restore also preserves the borderless window bounds");
        form.Bounds = original;
        using var dialog = new WindowPicker(new List<WindowInfo>()) { Opacity=0, ShowInTaskbar=false };
        dialog.Show(); Descendants(dialog).OfType<CaptionButton>().Single(b=>b.Kind=="close").PerformClick();
        Check(dialog.IsDisposed, "Themed close follows the form close lifecycle");
        var capture = new Rectangle(-1500,100,600,500); var work = new Rectangle(-1920,0,1920,1040); var size = new Size(360,158);
        var position = RecordingHud.ChoosePosition(capture,work,size,true);
        Check(work.Contains(new Rectangle(position,size)) && !capture.IntersectsWith(new Rectangle(position,size)), "HUD placement avoids the region on a negative-coordinate monitor");
    }
    private static async Task TestHudCaptureAsync(MainForm form)
    {
        var hud = Field<RecordingHud>(form,"_recordingHud");
        var indicator = Field<RegionIndicator>(form,"_regionIndicator");
        var engine = Field<RecorderEngine>(form,"_engine");
        var settings = Field<AppSettings>(form,"_settings");
        // Set opacity before showing/applying affinity, matching the actual recording HUD.
        hud.Opacity = 1;
        using var target = new Form { FormBorderStyle=FormBorderStyle.None, ShowInTaskbar=false, TopMost=true, StartPosition=FormStartPosition.Manual,
            Bounds=new Rectangle(Screen.PrimaryScreen!.WorkingArea.Left+100,Screen.PrimaryScreen.WorkingArea.Top+100,680,400), BackColor=Color.FromArgb(30,140,85) };
        target.Show(); await Task.Delay(80);
        Rectangle bounds = Rectangle.Inflate(target.Bounds,-10,-10);
        typeof(MainForm).GetField("_region",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(form,bounds);
        settings.Scope=RecordingScope.Region; settings.Purpose=RecordingPurpose.Desktop;
        Field<TextBox>(form,"_folderBox").Text=Path.Combine(_root,"hud-capture");
        Field<CheckBox>(form,"_minimize").Checked=true; Field<CheckBox>(form,"_countdown").Checked=false;
        Field<CheckBox>(form,"_excludeHud").Checked=true;
        Field<CheckBox>(form,"_dxgi").Checked=false; Field<CheckBox>(form,"_systemSound").Checked=false; Field<CheckBox>(form,"_microphone").Checked=false; Field<CheckBox>(form,"_mouseCursor").Checked=false;
        Field<ChoiceBox>(form,"_fpsBox").SelectedIndex=1;
        Invoke(form,"PlanChanged");
        Check(indicator.Visible && indicator.Opacity >= .9 && indicator.Opacity < 1 && !indicator.Region!.IsVisible(indicator.Width/2,indicator.Height/2), "Selected region keeps a high-contrast lightly translucent border with an entirely empty interior");
        Check(indicator.Bounds == Rectangle.Inflate(bounds,indicator.Inset,indicator.Inset), "Region border lies outside the captured pixels");
        await InvokeAsync(form,"StartRecordingAsync");
        Check(engine.State==RecorderState.Recording && !form.Visible && hud.Visible && Field<Label>(hud,"_started").Text.Contains("开始于"), "Recording hides the main window while its HUD shows the actual start time");
        // Deliberately overlap the capture target: supported Windows should exclude the HUD.
        hud.Location=bounds.Location+new Size(20,20); hud.Opacity=1;
        await Task.Delay(1200);
        bool excluded = hud.CaptureExcluded;
        Console.WriteLine($"HUD capture exclusion supported: {excluded}");
        using(var screen = new Bitmap(1,1))
        {
            using(var g=Graphics.FromImage(screen)) g.CopyFromScreen(hud.Left+30,hud.Top+30,0,0,new Size(1,1));
            Color pixel = screen.GetPixel(0,0);
            Console.WriteLine($"HUD exclusion pixel={pixel} expected={target.BackColor} HUD={hud.Bounds} target={target.Bounds}");
            if (excluded && pixel.ToArgb() != target.BackColor.ToArgb()) PresentationNative.Snapshot(target, "hud-exclusion-failure");
            Check(!excluded || pixel.ToArgb()==target.BackColor.ToArgb(), "Supported capture exclusion reveals the target beneath the floating HUD");
        }
        Snapshot(hud,"hud-recording");
        Field<SoftButton>(hud,"_pause").PerformClick();
        await Until(()=>engine.State==RecorderState.Paused&&!Field<bool>(form,"_busy"));
        Invoke(form,"UpdateTick"); string paused=Field<Label>(hud,"_elapsed").Text;
        await Task.Delay(650); Invoke(form,"UpdateTick");
        Check(Field<Label>(hud,"_elapsed").Text==paused && Field<SoftButton>(hud,"_pause").Text=="继续", "Floating pause freezes the displayed duration and offers resume");
        Snapshot(hud,"hud-paused");
        Field<SoftButton>(hud,"_main").PerformClick();
        Check(form.Visible && engine.State==RecorderState.Paused && hud.Visible, "HUD can restore the main window without interrupting the recording");
        form.Hide();
        Field<SoftButton>(hud,"_pause").PerformClick(); await Until(()=>engine.State==RecorderState.Recording&&!Field<bool>(form,"_busy"));
        await Task.Delay(650);
        Check(Field<SoftButton>(hud,"_pause").Text=="暂停" && Field<SoftButton>(hud,"_stop").Enabled, "Floating resume restarts capture and enables stop");
        Field<SoftButton>(hud,"_stop").PerformClick();
        Check(!Field<SoftButton>(hud,"_pause").Enabled && Field<SoftButton>(hud,"_stop").Enabled && Field<SoftButton>(hud,"_stop").Text=="取消保存", "HUD locks pause and exposes cancellation while finalizing the recording");
        Snapshot(hud,"hud-saving");
        await Until(()=>engine.State==RecorderState.Idle&&!Field<bool>(form,"_busy"));
        Check(File.Exists(engine.OutputFile)&&!hud.Visible&&form.Visible&&indicator.Visible, "Floating stop saves the recording, hides the HUD and restores the main window with its selected region");
        var hashes=await Hashes(engine.OutputFile);
        Check(hashes.Length==engine.FrameCount&&hashes.Length>0, "HUD-driven pause and resume preserve all recorded frames");
        Check(!excluded || hashes.Distinct().Count()==1, "HUD and selection border remain absent from every frame of a static target");
        Field<TextBox>(form,"_folderBox").Text=_root; settings.Scope=RecordingScope.FullScreen; Invoke(form,"PlanChanged");
        Check(!indicator.Visible,"Switching away from region mode removes its persistent border");
        hud.Opacity=0;
        target.Close();
    }
    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static void CheckButtonsFit(Form form)
    {
        // Invisible pages are not laid out until selected; check each visible tab in turn.
        var buttons=Descendants(form).OfType<SoftButton>().Where(button=>button.Visible).ToList();
        foreach(var button in buttons.Where(b=>!b.Parent!.ClientRectangle.Contains(b.Bounds)))
            Console.WriteLine($"OUTSIDE: {button.Text} bounds={button.Bounds} parent={button.Parent!.ClientRectangle} min={button.MinimumSize} DPI={button.DeviceDpi}");
        Check(buttons.All(button=>button.Parent!.ClientRectangle.Contains(button.Bounds)),form.GetType().Name+" visible action buttons fit inside their containers");
        foreach(var b in buttons)
        {
            int required=TextRenderer.MeasureText(b.Text,b.Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix).Width+Ui.S(b,b.Glyph.Length>0?38:14);
            if(required>b.Width)Console.WriteLine($"CLIPPED: {b.Text} required={required} actual={b.Width} DPI={b.DeviceDpi} font={b.Font.SizeInPoints} form={form.Bounds} scale={form.AutoScaleDimensions} current={form.CurrentAutoScaleDimensions} parent={b.Parent!.Bounds}");
        }
        Check(buttons.All(button=>TextRenderer.MeasureText(button.Text,button.Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix).Width+Ui.S(button,button.Glyph.Length>0?38:14)<=button.Width),form.GetType().Name+" visible button captions fit without ellipsis");
    }    private static void Key(Control control, Keys key) => control.GetType().GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { new KeyEventArgs(key) });
    private static void Snapshot(Control control, string name)
    {
        using var picture = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
        picture.Save(Path.Combine(_root, name + ".png"), ImageFormat.Png);
    }
    private static void Invoke(object instance, string method) => instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, null);
    private static async Task TestThemedControlsAsync(MainForm form)
    {
        // Native popup focus needs a visible foreground owner, just as in normal use.
        double opacity = form.Opacity; form.Opacity = 1; form.Activate();
        await Task.Delay(100);
        var fps = Field<ChoiceBox>(form, "_fpsBox");
        int original = fps.SelectedIndex;
        Key(fps, Keys.End); Check(fps.SelectedIndex == fps.Items.Count - 1, "Custom dropdown supports End key");
        Key(fps, Keys.Home); Check(fps.SelectedIndex == 0, "Custom dropdown supports Home key");
        Key(fps, Keys.Down); Check(fps.SelectedIndex == 1, "Custom dropdown supports arrow navigation");
        fps.SelectedIndex = original;
        var codecs = Field<ChoiceBox>(form, "_codecBox");
        int selected = codecs.SelectedIndex;
        int ffv1Index = CodecIndex(codecs, EncoderChoice.Ffv1);
        codecs.DroppedDown = true; await Task.Delay(80);
        Check(codecs.DroppedDown, "Custom dropdown opens its themed popup");
        var popup = Field<ToolStripDropDown>(codecs, "_popup");
        Snapshot(popup, "dropdown-codecs");
        var choices = Field<ListBox>(codecs, "_popupList");
        choices.SelectedIndex = ffv1Index; Key(choices, Keys.Escape);
        Check(!codecs.DroppedDown && codecs.SelectedIndex == selected, "Escape cancels pending popup selection");
        codecs.DroppedDown = true; await Task.Delay(40);
        choices = Field<ListBox>(codecs, "_popupList");
        choices.SelectedIndex = ffv1Index; Key(choices, Keys.Enter);
        Check(!codecs.DroppedDown && codecs.SelectedIndex == ffv1Index && Field<AppSettings>(form, "_settings").Encoder == EncoderChoice.Ffv1,
            "Enter commits dropdown selection and updates recording settings");
        codecs.SelectedIndex = selected;
        codecs.DroppedDown = true; await Task.Delay(30); codecs.Enabled = false;
        Check(!codecs.DroppedDown, "Disabling a dropdown closes its popup");
        codecs.Enabled = true; form.Opacity = opacity;
        using (var cleared = new ChoiceBox())
        {
            cleared.Items.AddRange(new object[] { "a", "b" }); cleared.SelectedIndex = 1; cleared.Items.Clear();
            Check(cleared.SelectedIndex == -1 && cleared.SelectedItem is null, "Clearing dropdown items removes stale selection");
        }
        var volume = Field<VolumeSlider>(form, "_systemVolume");
        int originalVolume = volume.Value;
        Key(volume, Keys.Home); Key(volume, Keys.Right);
        Check(volume.Value == volume.Minimum + volume.SmallChange && Math.Abs(Field<AppSettings>(form, "_settings").SystemVolume - volume.Value / 100f) < 0.001f,
            "Themed volume slider keyboard updates persisted audio volume");
        volume.Value = 1000; Check(volume.Value == volume.Maximum, "Volume slider clamps values to its valid range");
        volume.Enabled = false; Key(volume, Keys.Home);
        Check(volume.Value == volume.Maximum, "Disabled volume slider ignores keyboard changes");
        volume.Enabled = true; volume.Value = originalVolume;
        foreach (var field in new[] { "_monitorBox", "_fpsBox", "_codecBox" })
        {
            var control = Field<ChoiceBox>(form, field);
            using var fieldShot = new Bitmap(control.Width, control.Height); control.DrawToBitmap(fieldShot, control.ClientRectangle);
            Check(fieldShot.GetPixel(0, 0).ToArgb() == Ui.ParentColor(control).ToArgb(), field + " paints rounded field corners without native borders");
        }
        Snapshot(form, "main-window");
        var fixtures = new List<WindowInfo> { new(new IntPtr(1), "工作文档 - 测试窗口", new Rectangle(100, 80, 1280, 720)), new(new IntPtr(2), "浏览器 - 窗口选择示例", new Rectangle(20, 30, 960, 640)) };
        using (var picker = new WindowPicker(fixtures) { Opacity = 0, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) })
        {
            picker.Show(); await Task.Delay(40);
            Check(picker.SelectedWindow?.Handle == new IntPtr(1) && Field<SoftButton>(picker, "_ok").Enabled, "Window picker selects an available window and enables confirmation");
            Snapshot(picker, "window-picker");
            var pickerSize = picker.Size; picker.Size = picker.MinimumSize; picker.PerformLayout(); await Task.Delay(40);
            CheckButtonsFit(picker); Snapshot(picker, "window-picker-small"); picker.Size = pickerSize;
            var search = Field<TextBox>(picker, "_search"); search.Text = "浏览器";
            Check(picker.SelectedWindow?.Handle == new IntPtr(2), "Window picker search filters the selected capture target");
            search.Text = "does-not-exist";
            Check(picker.SelectedWindow is null && !Field<SoftButton>(picker, "_ok").Enabled && Field<Label>(picker, "_empty").Visible,
                "Empty window search disables confirmation and shows guidance");
            Snapshot(picker, "window-picker-empty");
            search.Text = ""; Field<SoftButton>(picker, "_ok").PerformClick();
            Check(picker.DialogResult == DialogResult.OK && picker.SelectedWindow?.Handle == new IntPtr(1), "Window picker confirmation returns the intended window");
        }
        using (var recovery = new RecoveryForm(_root, _caps) { Opacity = 0, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) })
        {
            recovery.Show(); await Task.Delay(40); Snapshot(recovery, "recovery-window");
            recovery.Size = recovery.MinimumSize; recovery.PerformLayout(); await Task.Delay(40); CheckButtonsFit(recovery); Snapshot(recovery, "recovery-small");
            Check(Field<Label>(recovery, "_empty").Visible && !Field<SoftButton>(recovery, "_recover").Enabled && !Field<SoftButton>(recovery, "_open").Enabled,
                "Recovery empty state disables unavailable actions");
            recovery.Close();
        }
    }
    private static async Task TestRecoveryUiAsync()
    {
        string folder=Path.Combine(_root,"audit-recovery"),directory=Path.Combine(folder,".screenlossless-work","ui-session");Directory.CreateDirectory(directory);
        string core=Path.Combine(_root,"中文测试'目录",".screenlossless-work","session1");
        File.Copy(Path.Combine(core,"rate-a.mkv"),Path.Combine(directory,"video-001.mkv"));File.Copy(Path.Combine(core,"rate-b.mkv"),Path.Combine(directory,"video-002.mkv"));
        new RecordingManifest {TargetFile="ui-recovered.mkv",Segments=new(){new("video-001.mkv",null,null),new("video-002.mkv",null,null)}}.Save(directory);
        using(var missing=new RecoveryForm(folder,new Capabilities()) {Opacity=0,ShowInTaskbar=false})
        {
            missing.Show();await Task.Delay(30);
            Check(!Field<SoftButton>(missing,"_recover").Enabled&&Field<SoftButton>(missing,"_open").Enabled,"Recovery with missing FFmpeg disables export while preserving access to source files");missing.Close();
        }
        using var recovery=new RecoveryForm(folder,_caps) {Opacity=0,ShowInTaskbar=false};recovery.Show();await Task.Delay(30);
        Field<SoftButton>(recovery,"_recover").PerformClick();
        Check(Field<bool>(recovery,"_busy")&&!Field<SoftButton>(recovery,"_refresh").Enabled&&!Field<DetailListBox>(recovery,"_list").Enabled,"Actual recovery action locks conflicting controls while merging");
        recovery.Close();Check(!recovery.IsDisposed,"Recovery window refuses closing while a real merge is running");
        await Until(()=>!Field<bool>(recovery,"_busy"));
        string output=Path.Combine(folder,"ui-recovered.mkv");
        Check(File.Exists(output)&&Field<DetailListBox>(recovery,"_list").Items.Count==0&&File.Exists(Path.Combine(directory,"video-001.mkv")),"Recovery UI exports the video, removes the completed session from the list and retains originals");
        Check((await Hashes(output)).Length==10,"Recovery UI preserves every frame across differing rate metadata");
        Snapshot(recovery,"recovery-complete");recovery.Close();
    }
    private static async Task TestScaledLayoutsAsync()
    {
        foreach(float scale in new[]{1.25f,1.5f})
        {
            new AppSettings {FfmpegPath=_caps.Ffmpeg,OutputFolder=_root}.Save();
            using var form=new MainForm {Opacity=0,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)};
            form.Show();await Until(()=>Field<Capabilities>(form,"_caps").Available&&!Field<bool>(form,"_busy"));
            var controls=new[]{(Control)form}.Concat(Descendants(form)).ToArray();var fonts=controls.ToDictionary(c=>c,c=>c.Font);
            form.Scale(new SizeF(scale,scale));
            foreach(var c in controls)c.Font=new Font(fonts[c].FontFamily,fonts[c].SizeInPoints*scale,fonts[c].Style);
            foreach(var tab in Field<List<SoftButton>>(form,"_settingsTabs")){tab.PerformClick();form.PerformLayout();await Task.Delay(30);CheckButtonsFit(form);}
            Check(Field<ChoiceBox>(form,"_systemDeviceBox").Width>Ui.S(form,160),$"{scale:P0} simulated layout gives audio device dropdowns usable width");
            Snapshot(form,$"scaled-layout-{(int)(scale*100)}");form.Close();
        }
    }
    private static async Task TestCaptureAsync()
    {
        string folder = Path.Combine(_root, "capture"); Directory.CreateDirectory(folder);
        var plan = new RecordPlan(new Rectangle(0, 0, 128, 72), null, RecordingScope.Region, RecordingPurpose.Desktop,
            EncoderChoice.X264Rgb, CaptureBackend.Gdi, 10, false, false, false, 1, 1);
        using var engine = new RecorderEngine(); engine.Message += Console.WriteLine;
        await engine.StartAsync(plan, _caps, folder, false);
        await Task.Delay(600);
        TimeSpan beforePause=engine.Duration;Task pause=engine.PauseAsync();
        Check(engine.Duration>=beforePause,"Duration retains the current segment while pause is stopping FFmpeg");
        await pause;
        long frames = engine.FrameCount; TimeSpan duration = engine.Duration;
        Check(frames > 0 && engine.State == RecorderState.Paused, "Real GDI capture pauses and retains frames");
        await Task.Delay(200);
        Check(engine.Duration == duration, "Paused duration does not advance");
        await engine.ResumeAsync(); await Task.Delay(600);
        Check(!Directory.EnumerateFiles(engine.WorkingDirectory, "*.log", SearchOption.AllDirectories).Any(), "Active capture does not create a session log");
        TimeSpan beforeStop=engine.Duration;Task<string> stopping=engine.StopAsync();
        Check(engine.Duration>=beforeStop,"Duration retains all segments while stop is finalizing");
        string file=await stopping;
        Check(!File.Exists(Path.ChangeExtension(file, ".log")), "Completed capture does not create a log sidecar");
        Check(engine.State == RecorderState.Idle && File.Exists(file) && engine.FrameCount > frames, "Real capture resumes, accumulates frames and saves");
        Check((await Hashes(file)).Length == engine.FrameCount, "Recorded frame count matches decoded output");
    }
}













internal static class PresentationNative
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr handle,int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr handle);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr handle,uint affinity);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr handle,uint command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    internal static bool IsAbove(IntPtr window,IntPtr other)
    {
        var previous=other;
        for(int i=0;i<10000;i++) { previous=GetWindow(previous,3); if(previous==window)return true; if(previous==IntPtr.Zero)return false; }
        return false;
    }
    internal static Color Pixel(Point point)
    {
        using var bitmap=new Bitmap(1,1);
        using(var g=Graphics.FromImage(bitmap)) g.CopyFromScreen(point,Point.Empty,new Size(1,1));
        return bitmap.GetPixel(0,0);
    }
    internal static void Snapshot(Form form,string name)
    {
        using var bitmap=new Bitmap(form.Width,form.Height);
        using(var g=Graphics.FromImage(bitmap)) g.CopyFromScreen(form.Location,Point.Empty,form.Size);
        bitmap.Save(Path.Combine(RegressionOutput.Root,name+".png"),ImageFormat.Png);
    }
}
internal static class RegressionOutput { internal static string Root=""; }