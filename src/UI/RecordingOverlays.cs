namespace ScreenLossless;

internal abstract class CaptureOverlay : Form
{
    internal bool CaptureExcluded { get; private set; }
    protected CaptureOverlay()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96); Font = Ui.Font(); BackColor = Ui.Background;
        DoubleBuffered = true;
    }
    protected virtual bool ExcludeFromRecording => false;
    internal bool EnsureVisibleOnTop()
    {
        if (IsDisposed) return false;
        // An independent tool window must not follow main-window hiding/minimization.
        if (Owner is not null) Owner = null;
        if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
        if (!Visible) Show();
        return WindowNative.RaiseWithoutActivation(Handle) && WindowNative.IsWindowVisible(Handle);
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); WindowNative.RaiseWithoutActivation(Handle); }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x8000000; return cp; } }
    protected void RefreshCaptureExclusion()
    {
        if (!IsHandleCreated) return;
        WindowNative.SetCaptureExclusion(Handle, ExcludeFromRecording);
        CaptureExcluded = WindowNative.IsCaptureExcluded(Handle);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); RefreshCaptureExclusion(); }
}

internal sealed class RecordingHud : CaptureOverlay
{
    private bool _excludeFromCapture, _sessionActive;
    protected override bool ExcludeFromRecording => _excludeFromCapture;
    protected override CreateParams CreateParams
    {
        get { var cp=base.CreateParams; cp.ExStyle=(cp.ExStyle & ~0x80) | 0x40000; return cp; }
    }
    internal void SetCaptureExclusion(bool exclude)
    {
        if (_excludeFromCapture == exclude) return;
        _excludeFromCapture = exclude;
        RefreshCaptureExclusion();
    }
    internal void EnsureOnScreen()
    {
        if (Screen.AllScreens.Any(s => s.WorkingArea.Contains(Bounds))) return;
        var work=Screen.FromRectangle(Bounds).WorkingArea;
        Location=new Point(Math.Clamp(Left,work.Left,Math.Max(work.Left,work.Right-Width)),Math.Clamp(Top,work.Top,Math.Max(work.Top,work.Bottom-Height)));
    }
    private readonly Label _status = Label(L10n.T("准备开始"), 9, true, Ui.Accent);
    private readonly Label _elapsed = Label("00:00:00", 23, true, Ui.Text);
    private readonly Label _started = Label(L10n.T("即将开始录制"), 8, false, Ui.Subtle);
    private readonly SoftButton _pause = new() { Text = L10n.T("暂停"), Glyph = "pause" };
    private readonly SoftButton _stop = new() { Text = L10n.T("停止"), Glyph = "stop", Primary = true };
    private readonly SoftButton _main = new() { Text = L10n.T("主界面"), Glyph = "window" };
    private readonly CaptionButton _dismiss = new("close", L10n.T("收起浮窗")) { BaseColor = Ui.Surface, Dock = DockStyle.Right, Width = 28 };
    internal event Action? PauseRequested, StopRequested, ShowMainRequested, DismissRequested;
    public RecordingHud()
    {
        // The base form may have observed the startup monitor's DPI before these
        // controls exist. Build once at 96 DPI, then let WinForms scale the whole tree.
        SuspendLayout(); AutoScaleDimensions = new SizeF(96, 96);
        _elapsed.Font = new Font("Consolas", 27, FontStyle.Bold);
        ShowInTaskbar = true;
        ClientSize = new Size(360, 158);
        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 12), Margin = Padding.Empty };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        foreach (int height in new[] { 24, 42, 22, 40 }) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        grid.Controls.Add(_status,0,0); grid.Controls.Add(_dismiss,1,0);
        grid.Controls.Add(_elapsed,0,1); grid.SetColumnSpan(_elapsed,2);
        grid.Controls.Add(_started,0,2); grid.SetColumnSpan(_started,2);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i=0;i<3;i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f/3));
        int col=0; foreach (var button in new[] {_pause,_stop,_main}) { button.Dock=DockStyle.Fill; button.Margin=new Padding(col==0?0:6,4,0,0); actions.Controls.Add(button,col++,0); }
        grid.Controls.Add(actions,0,3); grid.SetColumnSpan(actions,2); card.Controls.Add(grid); Controls.Add(card);
        foreach (var control in new Control[] {card,grid,_status,_elapsed,_started}) control.MouseDown += (_,e) => { if(e.Button==MouseButtons.Left) WindowNative.Drag(Handle); };
        _dismiss.Click += (_,_) => DismissRequested?.Invoke();
        _pause.Click += (_,_) => PauseRequested?.Invoke(); _stop.Click += (_,_) => StopRequested?.Invoke(); _main.Click += (_,_) => ShowMainRequested?.Invoke();
        Text = L10n.T("ScreenLossless · 录制控制"); AccessibleName = Text;
        ResumeLayout(true);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        EnsureOnScreen();
    }
    internal event Action<Point>? PositionCommitted;
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if(m.Msg==0x232)PositionCommitted?.Invoke(Location);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (_sessionActive) ShowMainRequested?.Invoke(); else DismissRequested?.Invoke();
        }
        base.OnFormClosing(e);
    }
    private static Label Label(string text,float size,bool bold,Color color) => new() { Text=text,Font=Ui.Font(size,bold),ForeColor=color,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty,BackColor=Color.Transparent,AutoEllipsis=true };
    internal void UpdateState(RecorderState state, bool busy, int countdown, TimeSpan elapsed, DateTime? started, bool starting = false, bool canCancelSaving = false, double saveProgress = 0, RecordingLimit? limit = null, PerformanceSnapshot? performance = null, bool warn = false)
    {
        bool ready = state == RecorderState.Idle && countdown == 0 && !starting;
        _sessionActive = !ready;
        _dismiss.Visible = ready;
        string status = ready ? L10n.T("●  已就绪 · 等待录制") : countdown > 0 ? L10n.F($"●  {countdown} 秒后开始录制") : state switch { RecorderState.Recording => L10n.T("●  正在录制"), RecorderState.Paused => L10n.T("Ⅱ  已暂停"), RecorderState.Finalizing => L10n.F($"正在保存 {saveProgress:P0}"), _ => L10n.T("正在启动录制…") };
        if(state==RecorderState.Recording&&performance is {HasSpeed:true})
            status=warn?L10n.F($"● 编码偏慢 {performance.Speed:0.00}×"):L10n.F($"● 正在录制 {performance.Speed:0.00}×");
        _status.Text=status;
        _status.ForeColor = state == RecorderState.Paused||state==RecorderState.Recording&&warn ? Ui.Warning : Ui.Accent;
        _elapsed.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        string startedText = ready ? L10n.T("可直接开始录制   ·   拖动上方可移动") : started is { } time ? L10n.F($"开始于 {time:HH:mm:ss}   ·   拖动上方可移动") : L10n.T("拖动上方可移动   ·   等待录制开始");
        bool active = state is RecorderState.Recording or RecorderState.Paused;
        if(limit is {Minutes:>0}&&active)startedText+=L10n.T("   ·   剩余 ")+RecordingLimit.Format(limit.Remaining(elapsed));
        _started.Text=startedText;
        _pause.Enabled = !busy && active; _pause.Text = state == RecorderState.Paused ? L10n.T("继续") : L10n.T("暂停"); _pause.Glyph = state == RecorderState.Paused ? "play" : "pause";
        _stop.Text = state==RecorderState.Finalizing?L10n.T("取消保存"):ready ? L10n.T("开始") : countdown > 0 ? L10n.T("取消") : L10n.T("停止"); float stopSize=state==RecorderState.Finalizing?8.5f:9.5f; if(_stop.Font.Size!=stopSize)_stop.Font=Ui.Font(stopSize); _stop.Glyph = ready ? "play" : countdown > 0 ? "close" : "stop"; _stop.Enabled = countdown > 0 || (!busy && (active || ready)) || (state==RecorderState.Finalizing&&canCancelSaving);
    }
    internal static Point ChoosePosition(Rectangle capture, Rectangle work, Size size, bool region)
    {
        int gap=12;
        if(region)
        {
            var candidates = new[] { new Point(capture.Left,capture.Top-size.Height-gap),new Point(capture.Left,capture.Bottom+gap),new Point(capture.Right+gap,capture.Top),new Point(capture.Left-size.Width-gap,capture.Top) };
            foreach(var p in candidates) { var r=new Rectangle(p,size); if(work.Contains(r)&&!capture.IntersectsWith(r)) return p; }
        }
        return new Point(Math.Max(work.Left,work.Right-size.Width-gap),Math.Max(work.Top,work.Top+gap));
    }
    internal void RestorePosition(Point saved)
    {
        var work=Screen.FromPoint(saved).WorkingArea;
        Location=new Point(Math.Clamp(saved.X,work.Left,Math.Max(work.Left,work.Right-Width)),
            Math.Clamp(saved.Y,work.Top,Math.Max(work.Top,work.Bottom-Height)));
    }
    internal void PlaceNear(Rectangle capture, bool region) => Location = ChoosePosition(capture,Screen.FromRectangle(capture).WorkingArea,Size,region);
}
