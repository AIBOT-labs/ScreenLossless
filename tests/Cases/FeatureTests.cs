using System.Reflection;

namespace ScreenLossless;

internal static partial class RegressionTests
{
    private static object? Call(object value,string method,params object[] arguments)=>value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,arguments);
    private static void Set(object value,string field,object content)=>value.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(value,content);
    private static async Task TestFeaturesAsync()
    {
        var custom=new AppSettings{StopAfterMinutes=37,Hotkeys=new[]{1,2,3}};custom.Normalize();
        Check(custom.StopAfterMinutes==37&&custom.Hotkeys.SequenceEqual(HotkeyBindings.Defaults),"Custom duration survives normalization; invalid hotkeys restore defaults");
        var duplicate=HotkeyBindings.Defaults.ToArray();duplicate[1]=duplicate[0];
        Check(HotkeyBindings.Validate(duplicate)is not null&&HotkeyBindings.Validate(HotkeyBindings.Defaults)is null,"Hotkeys reject duplicates and accept distinct supported combinations");
        var desktop=new Rectangle(-1920,-200,3840,1280);
        Check(MainForm.ValidateRegion(new[]{-1800,-100,300,200},desktop)is null&&MainForm.ValidateRegion(new[]{0,0,int.MaxValue,200},desktop)is not null,"Exact region coordinates support negative monitors and reject overflow");
        Check(RegionPicker.Adjust(new Rectangle(20,20,100,100),new Point(-200,900),16,new Size(400,300))==new Rectangle(0,200,100,100),"Region movement clamps to desktop boundaries");
        Check(RegionPicker.Adjust(new Rectangle(20,20,100,100),new Point(100,100),5,new Size(400,300))==new Rectangle(56,56,64,64),"Region resize preserves the minimum size when opposite corners collide");
        using(var picker=new RegionPicker(new Rectangle(SystemInformation.VirtualScreen.Left+40,SystemInformation.VirtualScreen.Top+40,200,120)){Opacity=0})
        {
            picker.Show();
            foreach(var pair in new[]{("OnMouseDown",new Point(100,90)),("OnMouseMove",new Point(120,100)),("OnMouseUp",new Point(120,100))})
                Call(picker,pair.Item1,new MouseEventArgs(MouseButtons.Left,1,pair.Item2.X,pair.Item2.Y,0));
            Check(!picker.IsDisposed&&picker.DialogResult!=DialogResult.OK,"Region adjustment stays open after mouse release");
            Call(picker,"OnKeyDown",new KeyEventArgs(Keys.Enter));
            Check(picker.SelectedBounds==new Rectangle(SystemInformation.VirtualScreen.Left+60,SystemInformation.VirtualScreen.Top+50,200,120),"Enter confirms the adjusted physical screen coordinates");
        }
        using(var numbers=new NumberSettingsForm("精确设置","检查数字输入",new[]{"X","Y","宽度","高度"},new[]{0,0,200,100},v=>v[2]<64?"宽度不足":null,true){Opacity=0})
        {
            numbers.Show();await Task.Delay(30);CheckButtonsFit(numbers);Snapshot(numbers,"feature-number-dialog");
            Field<TextBox[]>(numbers,"_inputs")[2].Text="invalid";
            Descendants(numbers).OfType<SoftButton>().Single(b=>b.Text=="应用").PerformClick();
            Check(numbers.DialogResult!=DialogResult.OK&&Field<Label>(numbers,"_error").Text.Length>0,"Numeric dialog keeps invalid input open with inline feedback");
            numbers.Scale(new SizeF(1.5f,1.5f));numbers.PerformLayout();CheckButtonsFit(numbers);Snapshot(numbers,"feature-number-dialog-scaled");numbers.Close();
        }
        using(var keys=new HotkeySettingsForm(HotkeyBindings.Defaults,_=>null){Opacity=0})
        {
            keys.Show();CheckButtonsFit(keys);Snapshot(keys,"feature-hotkeys");
            Field<ChoiceBox[]>(keys,"_keys")[1].SelectedIndex=Field<ChoiceBox[]>(keys,"_keys")[0].SelectedIndex;
            Descendants(keys).OfType<SoftButton>().Single(b=>b.Text=="应用").PerformClick();
            Check(keys.DialogResult!=DialogResult.OK,"Hotkey dialog rejects duplicate bindings before registering");keys.Close();
        }
        string folder=Path.Combine(_root,"feature-media");Directory.CreateDirectory(folder);
        string directory=Path.Combine(folder,".screenlossless-work","cancel");Directory.CreateDirectory(directory);
        string video=Path.Combine(directory,"video-001.mkv");
        await Run("-v","error","-y","-f","lavfi","-i","testsrc=size=128x72:rate=10:duration=2","-c:v","libx264rgb","-crf","0",video);
        using(var cancellation=new CancellationTokenSource())
        {
            var manifest=new RecordingManifest{TargetFile="cancelled.mkv",Segments=new(){new("video-001.mkv",null,null)}};manifest.Save(directory);
            bool cancelled=false;
            try{await RecordingFinalizer.ExportAsync(_caps,directory,manifest,_=>{},p=>{if(p>=.5)cancellation.Cancel();},cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&File.Exists(video)&&!File.Exists(Path.Combine(folder,manifest.TargetFile))&&RecordingManifest.Load(directory).CompletedFile is null,"Cancelling a save keeps original segments and publishes no incomplete recording");
            var progress=new List<double>();
            string result=await RecordingFinalizer.ExportAsync(_caps,directory,manifest,_=>{},progress.Add);
            Check(progress.First()==0&&progress.Last()==1&&progress.SequenceEqual(progress.Order())&&File.Exists(result),"Retry after cancellation reports monotonic progress and publishes a playable recording");
            Check((await Hashes(result)).SequenceEqual(await Hashes(video)),"Progress-aware saving preserves every original RGB frame");
        }
        using(var cancellation=new CancellationTokenSource(250))
        {
            bool cancelled=false;var watch=System.Diagnostics.Stopwatch.StartNew();
            try{await MediaProcess.RunAsync(_caps.Ffmpeg,new[]{"-v","error","-re","-f","lavfi","-i","color=size=128x72:rate=10","-t","30","-f","null","-"},30,null,cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&watch.Elapsed<TimeSpan.FromSeconds(5),"Cancelling a running media process terminates its own FFmpeg promptly");
        }
        var pending=new RecordingManifest{TargetFile="recovery-cancel.mkv",Segments=new(){new("video-001.mkv",null,null)}};pending.Save(directory);
        using(var recovery=new RecoveryForm(folder,_caps){Opacity=0})
        {
            recovery.Show();await Task.Delay(30);
            Field<SoftButton>(recovery,"_recover").PerformClick();
            Check(Field<SoftButton>(recovery,"_recover").Text=="取消恢复"&&Field<SoftButton>(recovery,"_recover").Enabled,"Recovery exposes a cancel action while processing");
            Field<SoftButton>(recovery,"_recover").PerformClick();await Until(()=>!Field<bool>(recovery,"_busy"));
            Check(!File.Exists(Path.Combine(folder,pending.TargetFile))&&RecordingFinalizer.Discover(folder).Any()&&File.Exists(video),"Recovery cancellation retains its source and publishes no partial file");recovery.Close();
        }
        new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,RecordSystemAudio=false,RecordMicrophone=false,RecordCursor=false,Countdown=false,MinimizeWhenRecording=true,Encoder=EncoderChoice.X264Rgb,UseDxgi=false,UseModernWindowCapture=false}.Save();
        using var main=new MainForm{Opacity=0,ShowInTaskbar=false};
        main.Show();await Until(()=>!Field<bool>(main,"_busy")&&Field<Capabilities>(main,"_caps").Available);
        var settings=Field<AppSettings>(main,"_settings");var hud=Field<RecordingHud>(main,"_recordingHud");hud.Opacity=0;
        var engine=Field<RecorderEngine>(main,"_engine");Field<ChoiceBox>(main,"_fpsBox").SelectedIndex=1;
        Call(main,"SetLimit",37);Check(AppSettings.Load().StopAfterMinutes==37&&Field<ChoiceBox>(main,"_limitBox").SelectedItem is RecordingLimit{Minutes:37},"Custom stop time appears in the themed dropdown and persists");
        Call(main,"SetLimit",0);
        using(var conflict=new Form{Opacity=0})
        {
            conflict.Show();
            var bindings=new[]{(int)(Keys.Control|Keys.Alt|Keys.Shift|Keys.F21),(int)(Keys.Control|Keys.Alt|Keys.Shift|Keys.F22),(int)(Keys.Control|Keys.Alt|Keys.Shift|Keys.F23)};
            Check(HotkeyBindings.Register(conflict.Handle,8101,bindings[0]),"Hotkey conflict fixture owns its test-only combination");
            int[] before=settings.Hotkeys.ToArray();
            var failure=Call(main,"ApplyHotkeys",new object[]{bindings});
            Check(failure is string&&settings.Hotkeys.SequenceEqual(before),"Occupied hotkeys roll back without changing saved bindings");
            WindowsInterop.UnregisterHotKey(conflict.Handle,8101);
            Check(Call(main,"ApplyHotkeys",new object[]{bindings})is null&&AppSettings.Load().Hotkeys.SequenceEqual(bindings),"Available custom hotkeys register and persist transactionally");
            Snapshot(main,"feature-main");conflict.Close();
        }
        Rectangle work=Screen.PrimaryScreen!.WorkingArea;
        using var target=new Form{FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(work.Left+80,work.Top+100,320,180),BackColor=Color.FromArgb(20,140,80),TopMost=true,ShowInTaskbar=false};
        target.Show();await Task.Delay(80);
        settings.Scope=RecordingScope.Window;Set(main,"_window",new WindowInfo(target.Handle,"Owned test target",target.Bounds));Invoke(main,"PlanChanged");
        var plan=(RecordPlan)Call(main,"GetPlan")!;
        Check(plan.WindowHandle==target.Handle.ToInt64()&&plan.Backend==CaptureBackend.Gdi&&plan.Bounds.Size==target.ClientSize,"Window plan uses client pixels and stable HWND capture");
        using(var frame=ScreenCapture.TakeWindow(target.Handle))Check(frame.Size==target.ClientSize&&frame.GetPixel(100,80).ToArgb()==target.BackColor.ToArgb(),"Window preview reads the selected client content");
        await InvokeAsync(main,"StartRecordingAsync");await Task.Delay(300);
        target.Location+=new Size(180,110);await Task.Delay(700);Invoke(main,"UpdateTick");
        Check(engine.State==RecorderState.Recording,"Moving the selected window keeps recording active");
        await InvokeAsync(main,"StopRecordingAsync");
        var hashes=await Hashes(engine.OutputFile);
        Check(hashes.Length>10&&hashes.Distinct().Count()==1,"Actual HWND recording follows a moving solid window without switching to old desktop coordinates");
        await InvokeAsync(main,"StartRecordingAsync");target.ClientSize+=new Size(20,10);Invoke(main,"UpdateTick");
        await Until(()=>engine.State==RecorderState.Idle&&!Field<bool>(main,"_busy"));
        Check(File.Exists(engine.OutputFile)&&engine.LastStopReason.Contains("窗口尺寸发生变化"),"Window resize automatically saves the existing recording");
        settings.Scope=RecordingScope.Region;Set(main,"_region",target.Bounds);Invoke(main,"PlanChanged");
        Check(!hud.Visible&&Field<RegionIndicator>(main,"_regionIndicator").Visible,"Idle region selection remains border-only after the feature upgrade");
        await InvokeAsync(main,"StartRecordingAsync");
        Set(main,"_sessionDisplays","controlled previous topology");
        Call(main,"QueueDeviceRefresh",true);Set(main,"_refreshDevicesAt",DateTime.MinValue);Invoke(main,"UpdateTick");
        await Until(()=>engine.State==RecorderState.Idle&&!Field<bool>(main,"_busy"));Invoke(main,"UpdateTick");
        Check(File.Exists(engine.OutputFile)&&!Field<bool>(main,"_devicesPending"),"Display configuration changes save the current session then refresh devices");
        var desktopBounds=SystemInformation.VirtualScreen;
        var offscreenRegion=new Rectangle(desktopBounds.Right+100,desktopBounds.Bottom+100,320,180);
        Set(main,"_region",offscreenRegion);Invoke(main,"PlanChanged");
        Call(main,"QueueDeviceRefresh",true);Set(main,"_refreshDevicesAt",DateTime.MinValue);Invoke(main,"UpdateTick");
        var fittedRegion=RegionSelection.FromRectangle(offscreenRegion)!.Restore(desktopBounds);
        Check(Field<Rectangle>(main,"_region")==fittedRegion&&Field<RegionIndicator>(main,"_regionIndicator").Visible,
            "Display refresh fits the existing selection onto the desktop instead of clearing its border");
        Check(AppSettings.Load().LastRegion!.Restore(desktopBounds)==fittedRegion&&!hud.Visible,
            "Display refresh persists corrected region coordinates without opening an idle HUD");
        Set(main,"_region",target.Bounds);Invoke(main,"PlanChanged");
        await InvokeAsync(main,"StartRecordingAsync");
        bool seenSave=false;
        void CancelSave(double p)
        {
            if(seenSave)return;seenSave=true;
            Check(Field<SoftButton>(main,"_stopButton").Enabled&&Field<SoftButton>(hud,"_stop").Enabled&&!Field<SoftButton>(hud,"_pause").Enabled,"Finalizing exposes cancel on main and HUD while pause remains locked");
            Snapshot(hud,"feature-save-progress");
            Field<SoftButton>(hud,"_stop").PerformClick();
        }
        engine.FinalizationProgress+=CancelSave;
        await InvokeAsync(main,"StopRecordingAsync");engine.FinalizationProgress-=CancelSave;
        Check(seenSave&&engine.State==RecorderState.Idle&&main.Visible&&!hud.Visible&&RecordingFinalizer.Discover(folder).Any(),"HUD cancel returns to the main window with recoverable source material");
        await InvokeAsync(main,"StartRecordingAsync");
        string disposedSession=Field<string>(engine,"_temporaryDirectory");
        var ownedProcess=Field<System.Diagnostics.Process>(engine,"_process");
        using var processObserver=System.Diagnostics.Process.GetProcessById(ownedProcess.Id);
        main.Dispose();
        await Until(()=>processObserver.HasExited);
        Check(!Field<System.Windows.Forms.Timer>(main,"_ticker").Enabled&&Field<bool>(engine,"_disposed"),
            "Direct main-window disposal stops its timer and releases the recorder");
        Check(Field<RegionIndicator>(main,"_regionIndicator").IsDisposed&&hud.IsDisposed,
            "Direct main-window disposal removes both independent overlays");
        var disposedManifest=RecordingManifest.Load(disposedSession);
        Check(disposedManifest.CompletedFile is null&&disposedManifest.Segments.Count==1&&
            ContinuousMedia.VideoFiles(disposedSession,disposedManifest.Segments[0]) is {Length: >0} retainedParts && retainedParts.All(File.Exists),
            "Direct disposal retains its exact unfinished session and capture segment for recovery");
        target.Close();
    }
}