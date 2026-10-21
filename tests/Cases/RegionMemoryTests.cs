using System.Reflection;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace ScreenLossless;

internal static partial class RegressionTests
{
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] private static extern IntPtr SendOwnMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    private static IntPtr PackedPoint(Point point)=>new(unchecked((int)(((uint)(ushort)point.Y<<16)|(ushort)point.X)));
    private static void BorderMouse(RegionIndicator border,string method,Point screen,MouseButtons button=MouseButtons.Left)
        =>Call(border,method,new MouseEventArgs(button,1,screen.X-border.Left,screen.Y-border.Top,0));

    private static double BorderContrast(Color a,Color b)
    {
        static double Linear(byte channel){double c=channel/255d;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4);}
        static double Luminance(Color color)=>.2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
        double first=Luminance(a),second=Luminance(b);
        return (Math.Max(first,second)+.05)/(Math.Min(first,second)+.05);
    }
    private static async Task TestRegionContrastAsync(Form target,Rectangle selection)
    {
        Color original=target.BackColor;
        using var border=new RegionIndicator();
        try
        {
            var backgrounds=new[]{Color.White,Color.Black,Color.FromArgb(118,118,118),Ui.Accent,Ui.AccentLight,Color.FromArgb(158,166,225)};
            foreach(var state in new[]{RecorderState.Idle,RecorderState.Recording,RecorderState.Paused})
            {
                border.SetEditable(state==RecorderState.Idle);border.SetSelection(selection,state);border.EnsureVisibleOnTop();
                using(var matrix=new Matrix())
                    Check(border.Region!.GetRegionScans(matrix).All(part=>!part.IntersectsWith(new Rectangle(border.Inset,border.Inset,selection.Width,selection.Height))),
                        "Both outline layers and corner accents remain outside the recorded crop: "+state);
                foreach(Color background in backgrounds)
                {
                    target.BackColor=background;target.Refresh();border.Refresh();border.EnsureVisibleOnTop();await Task.Delay(80);
                    var rails=new[]{
                        (new Point(selection.Left+22,selection.Top-4),new Point(selection.Left+22,selection.Top-3)),
                        (new Point(selection.Left+22,selection.Bottom+1),new Point(selection.Left+22,selection.Bottom+2)),
                        (new Point(selection.Left-4,selection.Top+22),new Point(selection.Left-3,selection.Top+22)),
                        (new Point(selection.Right+1,selection.Top+22),new Point(selection.Right+2,selection.Top+22))};
                    var samples=rails.Select(pair=>(Dark:PresentationNative.Pixel(pair.Item1),Light:PresentationNative.Pixel(pair.Item2))).ToArray();
                    string label=state+" / "+background.Name;
                    Check(samples.All(pair=>BorderContrast(pair.Dark,pair.Light)>10&&Math.Max(BorderContrast(pair.Dark,background),BorderContrast(pair.Light,background))>=3),
                        "Every side remains distinguishable on light, dark, gray or theme-colored backgrounds: "+label);
                    Point gap=new(selection.Left+14,selection.Top-3),inside=selection.Location+new Size(24,24);
                    Check(PresentationNative.Pixel(gap).ToArgb()==background.ToArgb()&&WindowFromPoint(gap)==target.Handle,
                        "High-contrast dashed gaps remain visually empty and click-through: "+label);
                    Check(PresentationNative.Pixel(inside).ToArgb()==background.ToArgb()&&WindowFromPoint(inside)==target.Handle,
                        "High-contrast border leaves interior pixels and mouse input untouched: "+label);
                }
            }
        }
        finally{target.BackColor=original;target.Refresh();}
    }

    private static async Task TestRegionMemoryAsync()
    {
        string? previousSettings=AppSettings.SettingsFileOverride;
        string folder=Path.Combine(_root,"region-memory");Directory.CreateDirectory(folder);
        AppSettings.SettingsFileOverride=Path.Combine(folder,"settings.json");
        try
        {
            Check(AppSettings.Load().Scope==RecordingScope.FullScreen&&AppSettings.Load().LastRegion is null,"First launch defaults to fullscreen without creating a phantom region");
            var negative=new RegionSelection{X=-1800,Y=-100,Width=640,Height=360};
            Check(negative.Restore(new Rectangle(-1920,-200,3840,1280))==new Rectangle(-1800,-100,640,360),"Saved negative-coordinate region restores exactly on the same desktop");
            var fitted=negative.Restore(new Rectangle(0,0,480,320));
            Check(fitted==new Rectangle(0,0,480,320),"Disconnected or smaller desktop safely repositions and fits a saved region");
            var invalid=new AppSettings{Scope=RecordingScope.Region,LastRegion=new(){X=int.MaxValue,Y=int.MinValue,Width=1,Height=1}};invalid.Normalize();
            Check(invalid.LastRegion is null&&invalid.Scope==RecordingScope.Region,"Invalid stored geometry is rejected without changing the remembered recording mode");
            new AppSettings{FfmpegPath=_caps.Ffmpeg,OutputFolder=folder,Countdown=false,RecordSystemAudio=false,RecordMicrophone=false,Encoder=EncoderChoice.X264Rgb,UseDxgi=false}.Save();
            var work=Screen.PrimaryScreen!.WorkingArea;
            var chosen=new Rectangle(work.Left+160,work.Top+220,380,220);
            using var target=new Form{FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=FormStartPosition.Manual,Bounds=Rectangle.Inflate(chosen,70,70),BackColor=Color.FromArgb(50,110,85)};
            target.Show();await TestRegionContrastAsync(target,chosen);
            Rectangle remembered;
            using(var main=new MainForm{Opacity=0,ShowInTaskbar=false})
            {
                main.Show();await Until(()=>Field<Capabilities>(main,"_caps").Available&&!Field<bool>(main,"_busy"));
                Check(Field<AppSettings>(main,"_settings").Scope==RecordingScope.FullScreen&&!Field<RegionIndicator>(main,"_regionIndicator").Visible,"Fresh main window selects fullscreen and leaves the desktop clear");
                var settings=Field<AppSettings>(main,"_settings");
                settings.Scope=RecordingScope.Region;Set(main,"_region",chosen);Invoke(main,"PlanChanged");
                var border=Field<RegionIndicator>(main,"_regionIndicator");
                var foreground=PresentationNative.GetForegroundWindow();border.EnsureVisibleOnTop();await Task.Delay(80);
                Check(PresentationNative.GetForegroundWindow()==foreground,"Restoring and raising the border does not steal foreground focus");
                Check(border.Editable&&border.Visible&&!Field<RecordingHud>(main,"_recordingHud").Visible,"Idle region shows editable border and keeps recording HUD hidden");
                Check((PresentationNative.GetWindowLong(border.Handle,-20)&0x20)==0&&(PresentationNative.GetWindowLong(border.Handle,-20)&0x8000000)==0,"Editable border permits deliberate edge clicks to own reliable foreground capture");
                var crop=new Rectangle(border.Inset,border.Inset,chosen.Width,chosen.Height);
                using(var matrix=new Matrix())
                    Check(border.Region!.GetRegionScans(matrix).All(part=>!part.IntersectsWith(crop)),"Every dash and resize grip stays entirely outside captured pixels");
                Check(border.HitEdge(new Point(border.MoveGrip.Left+8,border.MoveGrip.Top+4))==16&&!border.MoveGrip.IntersectsWith(crop),"Top-edge arrow block moves the region without entering its interior");
                Check(border.ResizeGrips().Length==8&&border.ResizeGrips().All(g=>border.HitEdge(new Point(g.Bounds.Left+g.Bounds.Width/2,g.Bounds.Top+g.Bounds.Height/2))==g.Edge),"All eight edge and corner grips expose the expected resize direction");
                Point inside=chosen.Location+new Size(40,40);
                Check(WindowFromPoint(inside)==target.Handle,"Native window routing sends region-interior clicks to the underlying application");
                Point dash=new(chosen.Left+20,chosen.Top-2),gap=new(chosen.Left+14,chosen.Top-2);
                Check(WindowFromPoint(dash)==border.Handle&&WindowFromPoint(gap)==target.Handle,"Only visible border pixels intercept the mouse; dashed gaps remain click-through");
                Check(SendOwnMessage(border.Handle,0x84,IntPtr.Zero,PackedPoint(dash))==new IntPtr(1)&&SendOwnMessage(border.Handle,0x84,IntPtr.Zero,PackedPoint(inside))==new IntPtr(-1),"Native hit testing distinguishes editable dashes from the empty interior");
                Check(SendOwnMessage(border.Handle,0x21,IntPtr.Zero,IntPtr.Zero)==new IntPtr(1),"Explicit edge clicks activate the drag window instead of losing input outside the thin border");
                PresentationNative.Snapshot(target,"region-editable-border");
                BorderMouse(border,"OnMouseDown",dash);
                BorderMouse(border,"OnMouseMove",dash+new Size(30,20));
                Check(Field<Rectangle>(main,"_region")==new Rectangle(chosen.X+30,chosen.Y+20,chosen.Width,chosen.Height),"Dragging a dash moves the selection live on the main form");
                Check(AppSettings.Load().LastRegion!.Restore(SystemInformation.VirtualScreen)==chosen,"Mouse movement does not rewrite settings before the drag completes");
                BorderMouse(border,"OnMouseUp",dash+new Size(40,25));
                var moved=new Rectangle(chosen.X+40,chosen.Y+25,chosen.Width,chosen.Height);
                Check(Field<Rectangle>(main,"_region")==moved&&AppSettings.Load().LastRegion!.Restore(SystemInformation.VirtualScreen)==moved,"Mouse release uses its final coordinates and persists the moved region");
                var bottomRight=border.ResizeGrips().Single(g=>g.Edge==10).Bounds;
                Point corner=border.PointToScreen(new Point(bottomRight.Left+4,bottomRight.Top+4));
                BorderMouse(border,"OnMouseDown",corner);BorderMouse(border,"OnMouseUp",corner+new Size(35,18));
                remembered=new Rectangle(moved.X,moved.Y,moved.Width+35,moved.Height+18);
                Check(Field<Rectangle>(main,"_region")==remembered&&AppSettings.Load().LastRegion!.Restore(SystemInformation.VirtualScreen)==remembered,"Dragging a corner resizes and persists the exact selection");
                var right=border.ResizeGrips().Single(g=>g.Edge==2).Bounds;
                Point edge=border.PointToScreen(new Point(right.Left+4,right.Top+8));
                BorderMouse(border,"OnMouseDown",edge);BorderMouse(border,"OnMouseUp",edge+new Size(-500,0));
                Check(border.Selection.Width==64&&border.Selection.Height==remembered.Height,"Side grip enforces minimum width without changing height");
                Set(main,"_region",remembered);Invoke(main,"PlanChanged");
                Point move=new(remembered.Left+20,remembered.Top-2);
                BorderMouse(border,"OnMouseDown",move);BorderMouse(border,"OnMouseMove",move+new Size(10,10));
                BorderMouse(border,"OnMouseDown",move+new Size(10,10),MouseButtons.Right);
                Check(border.Selection==remembered&&!border.IsDragging&&AppSettings.Load().LastRegion!.Restore(SystemInformation.VirtualScreen)==remembered,"Right-click during drag restores the prior rectangle and saved geometry");
                BorderMouse(border,"OnMouseDown",move);BorderMouse(border,"OnMouseMove",move+new Size(12,8));border.Capture=false;
                Check(border.Selection==remembered&&!border.IsDragging,"Interrupted mouse capture cancels the adjustment without leaving a stuck border");
                BorderMouse(border,"OnMouseDown",move);BorderMouse(border,"OnMouseMove",move+new Size(8,6));Call(border,"OnKeyDown",new KeyEventArgs(Keys.Escape));
                Check(border.Selection==remembered&&!border.IsDragging,"Escape cancels an active border adjustment");
                border.SetEditable(false);
                Check((PresentationNative.GetWindowLong(border.Handle,-20)&0x20)!=0&&border.Inset==4&&border.HitEdge(new Point(24,2))==0,"Locked border removes handles and restores native mouse pass-through");
                BorderMouse(border,"OnMouseDown",new Point(remembered.Left+20,remembered.Top-2));
                Check(!border.IsDragging&&border.Selection==remembered,"Recording-state border ignores attempted dragging");
                Invoke(main,"SyncRecordingUi");border.Close();
                Check(main.IsDisposed,"Closing the focused border routes through main-window shutdown without leaving orphaned overlays");
            }
            Check(AppSettings.Load().Scope==RecordingScope.Region&&AppSettings.Load().LastRegion!.Restore(SystemInformation.VirtualScreen)==remembered,"Closing preserves both the recording mode and exact region coordinates");
            using(var reopened=new MainForm{Opacity=0,ShowInTaskbar=false})
            {
                reopened.Show();await Until(()=>Field<Capabilities>(reopened,"_caps").Available&&!Field<bool>(reopened,"_busy"));
                var border=Field<RegionIndicator>(reopened,"_regionIndicator");
                Check(Field<AppSettings>(reopened,"_settings").Scope==RecordingScope.Region&&Field<Rectangle>(reopened,"_region")==remembered&&border.Visible&&border.Editable,"Reopening restores the prior region and immediately shows its topmost editable dashed border");
                Check(!Field<RecordingHud>(reopened,"_recordingHud").Visible&&Field<SoftButton>(reopened,"_regionEditButton").Enabled,"Restoring a region enables precise editing without prematurely showing a recording HUD");
                Check((PresentationNative.GetWindowLong(border.Handle,-20)&8)!=0,"Restored region border retains native topmost state");
                Field<AppSettings>(reopened,"_settings").Scope=RecordingScope.FullScreen;Invoke(reopened,"PlanChanged");reopened.Close();
            }
            using(var fullscreen=new MainForm{Opacity=0,ShowInTaskbar=false})
            {
                fullscreen.Show();await Until(()=>Field<Capabilities>(fullscreen,"_caps").Available&&!Field<bool>(fullscreen,"_busy"));
                Check(Field<AppSettings>(fullscreen,"_settings").Scope==RecordingScope.FullScreen&&!Field<RegionIndicator>(fullscreen,"_regionIndicator").Visible&&Field<Rectangle>(fullscreen,"_region")==remembered,"Remembered fullscreen hides the border while retaining the last region for later use");fullscreen.Close();
            }
            target.Close();
        }
        finally{AppSettings.SettingsFileOverride=previousSettings;}
    }
}