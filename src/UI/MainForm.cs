using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenLossless;

public sealed class MainForm : ThemedForm
{
    private const int HotkeyRecord = 7101, HotkeyPause = 7102, HotkeyScreenshot = 7103;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ChoiceBox _languageBox = new();
    private readonly SoftButton _themeButton=new(){Glyph="moon",Appearance=SoftButtonStyle.Quiet};
    internal event EventHandler? LanguageChangeRequested;
    private bool _languageSwitchPending;
    internal bool CanApplyLanguageChange => _hasShown && !_resourcesReleased && !_busy && !_detecting && !_startingSession && _dialogDepth==0 && _countdownCancellation is null && _engine.State==RecorderState.Idle;
    internal bool CanChangeLanguage => CanApplyLanguageChange && !_languageSwitchPending;
    private readonly RecorderEngine _engine = new();
    private Capabilities _caps = new();
    private readonly List<MonitorInfo> _monitors = new();
    private Rectangle _region;
    private readonly RegionIndicator _regionIndicator = new();
    private readonly RecordingHud _recordingHud = new();
    private bool _startingSession, _suspendOverlays, _hasShown, _resourcesReleased;
    private int _countdownSeconds;
    private DateTime? _sessionStartedAt;
    private RecordingLimit _sessionLimit = new(0);
    private readonly ChoiceBox _limitBox = new();
    private readonly SoftButton _limitCustomButton=new(){Text=L10n.T("自定义")};
    private readonly SoftButton _regionEditButton=new(){Text=L10n.T("微调选区"),Font=Ui.Font(8.5f)};
    private readonly SoftButton _hotkeyEditButton=new(){Text=L10n.T("设置快捷键")};
    private readonly Label[] _hotkeyLabels={Caption(""),Caption(""),Caption("")};
    private readonly Label _shortcutHint=Caption("",8,false,Ui.Subtle);
    private CancellationTokenSource? _saveCancellation;
    private double _saveProgress;
    private bool _displayPending, _devicesPending;
    private DateTime _refreshDevicesAt;

    private readonly Label _hotkeyStatus = Caption(L10n.T("正在注册全局快捷键…"), 8.5f, false, Ui.Subtle);
    private WindowInfo? _window;
    private bool _busy, _closeAfterCountdown, _detecting, _reallyClosing, _restoreAfterRecord;
    private CancellationTokenSource? _countdownCancellation;
    private readonly System.Windows.Forms.Timer _ticker = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _audioTicker=new(){Interval=100};
    private readonly AudioLevelBar _systemLevelBar=new(),_micLevelBar=new();
    private AudioLevelPreview? _audioPreview;
    private string _audioPreviewKey="";
    private DateTime _nextPreviewRetry, _nextPreviewFailure;
    private Func<string,long?> _spaceReader=StorageGuard.AvailableBytes;
    private Func<string,long> _workspaceSizer=StorageGuard.WorkspaceBytes;
    private readonly NotifyIcon _tray = new();
    private bool _applyingProfile;
    private readonly ChoiceBox _resolutionBox = new(), _containerBox = new(), _segmentMinutesBox = new(), _segmentSizeBox = new();
    private DateTime _nextPerformanceWarning;
    private readonly ToggleCheck _autoPerformance = new() { Text = L10n.T("性能不足时自动降低帧率（有损编码）") };
    private readonly AdaptivePerformance _adaptivePerformance = new();
    private string _sessionDisplays = "";
    private readonly ChoiceBox _profileBox = new(), _qualityBox = new();
    private readonly Label _encodingNote = Caption("", 8, false, Ui.Subtle);
    private readonly Label _profileHint = Caption("", 8.5f, false, Ui.Subtle);
    private readonly ChoiceBox _monitorBox = new(), _fpsBox = new(), _codecBox = new(), _systemDeviceBox = new(), _microphoneDeviceBox = new();
    private readonly SoftButton _floatingButton = new() { Text = L10n.T("显示录制浮窗"), Glyph = "window", Enabled = false };
    private ToolStripItem? _floatingMenu;
    private readonly SoftButton _audioRefresh = new() { Text = L10n.T("刷新设备"), Glyph = "refresh" };
    private readonly SoftButton _lastFileButton = new() { Text = L10n.T("打开最近"), Glyph = "play", Enabled = false, Font = Ui.Font(8.5f) };
    private readonly Label _storage = Caption(L10n.T("空间待检测"), 8, false, Ui.Subtle);
    private readonly ToolTip _hints = new ThemeToolTip();
    private bool _refreshingAudio, _lowSpaceWarned;
    private int _dialogDepth;
    private DateTime _nextSpaceCheck;
    private Rectangle _previewBounds;
    private string? _lastSavedFile;
    private readonly TextBox _folderBox = new();
    private readonly SoftButton _folderBrowse = new() { Text = L10n.T("浏览") };
    private readonly ToggleCheck _systemSound = new() { Text = L10n.T("录制系统声音") };
    private readonly ToggleCheck _microphone = new() { Text = L10n.T("录制麦克风") };
    private readonly ToggleCheck _mixAudio = new() { Text = L10n.T("合并为一个音轨") };
    private readonly ToggleCheck _mouseCursor = new() { Text = L10n.T("显示鼠标指针") };
    private readonly ToggleCheck _countdown = new() { Text = L10n.T("开始前倒计时 3 秒") };
    private readonly ToggleCheck _minimize = new() { Text = L10n.T("录制时隐藏主界面") };
    private readonly ToggleCheck _excludeHud = new() { Text = L10n.T("防止浮窗被录入（可能影响远程显示）") };
    private readonly ToggleCheck _modernWindow=new(){Text=L10n.T("窗口录制优先使用现代捕获（减少遮挡影响）")};
    private readonly ToggleCheck _dxgi = new() { Text = L10n.T("优先使用 DXGI GPU 抓屏") };
    private readonly VolumeSlider _systemVolume = new(), _micVolume = new();
    private readonly SelectCard[] _scopeCards = new SelectCard[3], _purposeCards = new SelectCard[2];
    private readonly Label _gpuName = Caption(L10n.T("正在识别显卡…"), 10, true);
    private readonly Label _gpuHint = Caption(L10n.T("自动识别 · 无需手动选择"), 8.5f, false, Ui.Subtle);
    private readonly Label _status = Caption(L10n.T("准备就绪"), 10, true, Ui.Mint);
    private readonly Label _timerLabel = Caption("00:00:00", 24, true);
    private readonly Label _frameLabel = Caption(L10n.T("0 帧"), 9, false, Ui.Subtle);
    private readonly Label _summary = Caption(L10n.T("正在检测录制能力…"), 10, true);
    private readonly Label _details = Caption("", 9, false, Ui.Subtle);
    private readonly Label _selectionSummary = Caption(L10n.T("全屏 · 保持屏幕原始分辨率"), 8.5f, false, Ui.Subtle);
    private readonly Label _operationHint = new();
    private readonly PreviewCanvas _preview = new();
    private Bitmap? _brandImage;
    private Icon? _ownedIcon;
    private readonly SoftButton _recordButton = new() { Text = L10n.T("开始录制"), Primary = true, Glyph = "play" };
    private readonly SoftButton _pauseButton = new() { Text = L10n.T("暂停"), Glyph = "pause" };
    private readonly SoftButton _stopButton = new() { Text = L10n.T("停止"), Destructive = true, Glyph = "stop" };
    private readonly SoftButton _testButton = new() { Text = L10n.T("刷新预览"), Glyph = "refresh", Font = Ui.Font(L10n.IsEnglish?9:9.5f) };
    private readonly SoftButton _refreshButton = new() { Text = L10n.T("重新检测"), Glyph = "refresh" };
    private readonly Panel _settingsHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Ui.Surface };
    private readonly List<Control> _settingsPages = new();
    private readonly List<SoftButton> _settingsTabs = new();

    private readonly List<(TableLayoutPanel Panel, float[] Columns, float[] Rows)> _dpiTables = new();

    public MainForm()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L10n.F($"ScreenLossless {AppVersion.Display} · 屏幕录制");
        using var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("ScreenLossless.AppIcon");
        Icon = iconStream is null ? SystemIcons.Application : (_ownedIcon=new Icon(iconStream));
        ClientSize = new Size(1240, 850);
        MinimumSize = new Size(1080, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Background;
        Font = Ui.Font();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) _countdownCancellation?.Cancel(); };
        BuildInterface();
        CaptureDpiTableMetrics(this);
        RestoreSettings();
        RefreshMonitors();
        ConfigureTray();
        Ui.ThemeChanged+=OnThemeChanged;UpdateThemeButton();
        HookEvents();
        _regionIndicator.SelectionEdited += OnRegionEdited;
        _regionIndicator.CloseRequested += ()=>Close();
        _recordingHud.PauseRequested += async () => await TogglePauseAsync();
        _recordingHud.StopRequested += async () =>
        {
            if (_engine.State == RecorderState.Idle && !_startingSession && _countdownCancellation is null) await StartRecordingAsync();
            else await StopRecordingAsync();
        };
        _recordingHud.ShowMainRequested += RestoreWindow;
        _recordingHud.PositionCommitted+=point=>{_settings.HudPosition=new(){X=point.X,Y=point.Y};_settings.Save();};
        _timerLabel.Font = new Font("Consolas", 27, FontStyle.Bold);
        _ticker.Tick += (_, _) => UpdateTick();
        _ticker.Start();_audioTicker.Tick+=(_,_)=>UpdateAudioLevels();
        VisibleChanged+=(_,_)=>UpdateAudioLevels();SizeChanged+=(_,_)=>UpdateAudioLevels();
        Shown += async (_, _) => { _hasShown=true;RestoreRememberedWindow();RestoreDpiTableMetrics();FitToCurrentScreen();SyncRecordingUi();await RefreshCapabilitiesAsync(); };
        FormClosing += OnClosing;
        ResumeLayout(true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Release the old monitor minimum before WinForms applies the suggested bounds.
        MinimumSize = Size.Empty;
        base.OnDpiChanged(e);
        // WinForms applies the new monitor bounds after this notification. Moving here
        // can reenter WM_DPICHANGED and scale nested table columns repeatedly.
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed) return;
                RestoreDpiTableMetrics();
                SetMinimumViewport();
                FitToCurrentScreen();
            }));
    }
    private void CaptureDpiTableMetrics(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is TableLayoutPanel table)
                _dpiTables.Add((table,
                    table.ColumnStyles.Cast<ColumnStyle>().Select(style => style.SizeType == SizeType.Absolute ? style.Width : float.NaN).ToArray(),
                    table.RowStyles.Cast<RowStyle>().Select(style => style.SizeType == SizeType.Absolute ? style.Height : float.NaN).ToArray()));
            CaptureDpiTableMetrics(child);
        }
    }
    private void RestoreDpiTableMetrics()
    {
        // Nested tables can receive more than one native DPI scaling pass during startup.
        // Derive absolute tracks from their design values instead of compounding old sizes.
        float scale = DeviceDpi / 96f;
        foreach (var (table, columns, rows) in _dpiTables)
        {
            table.SuspendLayout();
            for (int i = 0; i < columns.Length; i++)
                if (!float.IsNaN(columns[i])) table.ColumnStyles[i].Width = columns[i] * scale;
            for (int i = 0; i < rows.Length; i++)
                if (!float.IsNaN(rows[i])) table.RowStyles[i].Height = rows[i] * scale;
            table.ResumeLayout(true);
        }
    }
    private void SetMinimumViewport()
    {
        var work=Screen.FromHandle(Handle).WorkingArea;
        MinimumSize=new Size(Math.Min(Ui.S(this,1060),work.Width),Math.Min(Ui.S(this,720+ChromeHeight),work.Height));
    }
    internal static Rectangle FitBounds(Rectangle bounds,Rectangle work)
    {
        var size=new Size(Math.Min(bounds.Width,work.Width),Math.Min(bounds.Height,work.Height));
        return new Rectangle(Math.Clamp(bounds.Left,work.Left,work.Right-size.Width),Math.Clamp(bounds.Top,work.Top,work.Bottom-size.Height),size.Width,size.Height);
    }
    private void FitToCurrentScreen()
    {
        if(WindowState!=FormWindowState.Normal)return;
        var fit=FitBounds(Bounds,Screen.FromHandle(Handle).WorkingArea);
        if(fit!=Bounds)Bounds=fit;
    }
    private static Label Caption(string text, float size = 9.5f, bool bold = false, Color? color = null)
        => new() { Text = text, Font = Ui.Font(size, bold), ForeColor = color ?? Ui.Text,
            AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent };

    private static TableLayoutPanel Grid(float[] columns, params float[] rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = Padding.Empty, BackColor = Color.Transparent, ColumnCount = columns.Length, RowCount = rows.Length };
        foreach (float width in columns) grid.ColumnStyles.Add(new ColumnStyle(width < 0 ? SizeType.Absolute : SizeType.Percent, Math.Abs(width)));
        foreach (float height in rows) grid.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, Math.Abs(height)));
        return grid;
    }
    private static CardPanel Card() => new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(18) };
    private static void ButtonCell(TableLayoutPanel grid, SoftButton button, int column, int row = 0, int gap = 8)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 0, column == grid.ColumnCount - 1 ? 0 : gap, 0);
        grid.Controls.Add(button, column, row);
    }
    private static Control Field(string title, Control editor)
    {
        var grid = Grid(new[] { 100f }, 24, 36);
        grid.Margin = new Padding(0, 0, 0, 12);
        grid.Controls.Add(Caption(title, 9, true), 0, 0);
        editor.Dock = DockStyle.Fill; editor.Margin = new Padding(0, 3, 0, 0);
        grid.Controls.Add(editor, 0, 1);
        return grid;
    }
    private static void StyleCombo(ChoiceBox box)
    {
        box.Font = Ui.Font(); box.BackColor = Ui.Surface; box.ForeColor = Ui.Text;
        box.DropDownHeight = 280;
    }
    private static void StyleCheck(CheckBox box)
    {
        box.Dock = DockStyle.Fill; box.Margin = Padding.Empty;
        box.AutoSize = false; box.ForeColor = Ui.Text; box.Font = Ui.Font();
    }
    private static void SetupTrackbar(VolumeSlider bar)
    {
        bar.Minimum = 0; bar.Maximum = 100; bar.Value = 100;
        bar.Dock = DockStyle.Fill; bar.Margin = Padding.Empty;
        bar.BackColor = Ui.Surface; bar.SmallChange = 5; bar.LargeChange = 10;
    }
    private void BuildInterface()
    {
        var root = Grid(new[] { 100f }, 90, -100, 30);
        root.Padding = new Padding(24, 10, 24, 8);
        _languageBox.Font=Ui.Font(9);_languageBox.CaptionStyle=true;_languageBox.Glyph="language";_languageBox.AccessibleName=L10n.T("界面语言");
        _languageBox.Items.AddRange(Enum.GetValues<AppLanguage>().Select(value=>(object)new LanguageOption(value)).ToArray());
        _languageBox.SelectedIndex=(int)_settings.Language;_languageBox.Dock=DockStyle.Fill;_languageBox.Margin=new Padding(0,4,10,4);
        _hints.SetToolTip(_languageBox,L10n.T("录制或处理文件时暂不能切换语言。"));
        var captionTools=Grid(new[]{-184f,-48f},-100);
        captionTools.AccessibleName=L10n.T("界面主题");captionTools.Controls.Add(_languageBox,0,0);
        _themeButton.Click+=(_,_)=>ChangeTheme(Ui.IsDark?AppTheme.Light:AppTheme.Dark);
        _themeButton.Dock=DockStyle.Fill;_themeButton.Margin=new Padding(0,2,4,2);
        captionTools.Controls.Add(_themeButton,1,0);
        SetContent(root, caption: false, captionAccessory: captionTools, captionAccessoryWidth: 232);
        var header = Grid(new[] { -60f, 100f, -340f }, -100);
        header.Margin = new Padding(0, 0, 0, 14);
        root.Controls.Add(header, 0, 0);
        header.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Margin = new Padding(0, 11, 12, 13),
            SizeMode = PictureBoxSizeMode.Zoom, Image = _brandImage=(Icon ?? SystemIcons.Application).ToBitmap() }, 0, 0);
        var brand = Grid(new[] { 100f }, 43, -100);
        brand.Margin = new Padding(0, 5, 0, 7);
        brand.Controls.Add(Caption("ScreenLossless", 23, true), 0, 0);
        brand.Controls.Add(Caption(L10n.F($"{AppVersion.Display}  /  无损与轻量录屏    ·    留住每一个细节"), 9, false, Ui.Subtle), 0, 1);
        header.Controls.Add(brand, 1, 0);
        EnableWindowDrag(header); EnableWindowDrag(brand);
        foreach (Control title in brand.Controls) EnableWindowDrag(title);
        EnableWindowDrag(header.Controls[0]);
        var menu = Grid(new[] { 56f, 44f }, -100);
        menu.Margin = new Padding(0, 21, 0, 17);
        var ffmpeg = new SoftButton { Text = L10n.T("FFmpeg 设置"), Glyph = "settings", Font = Ui.Font(9), Appearance = SoftButtonStyle.Quiet };
        ffmpeg.Click += (_, _) => ChooseFfmpeg();
        var recover = new SoftButton { Text = L10n.T("恢复录制"), Glyph = "recover", Font = Ui.Font(9), Appearance = SoftButtonStyle.Quiet };
        recover.Click += (_, _) => ShowRecovery();
        ButtonCell(menu, ffmpeg, 0); ButtonCell(menu, recover, 1);
        header.Controls.Add(menu, 2, 0);

        var columns = Grid(new[] { 57f, 43f }, -100);
        root.Controls.Add(columns, 0, 1);
        var left = Grid(new[] { 100f }, 158, -100, 76);
        left.Margin = new Padding(0, 0, 16, 0);
        columns.Controls.Add(left, 0, 0);
        BuildLeft(left);
        var right = Grid(new[] { 100f }, -45, 142, -55, 64, 40);
        columns.Controls.Add(right, 1, 0);
        BuildRight(right);
        var footer=Grid(new[]{70f,30f},-100);
        footer.Controls.Add(_shortcutHint,0,0);
        _storage.TextAlign=ContentAlignment.MiddleRight;footer.Controls.Add(_storage,1,0);root.Controls.Add(footer,0,2);
    }
    private void BuildLeft(TableLayoutPanel host)
    {
        var scope = Card(); scope.Margin = new Padding(0, 0, 0, 12);
        host.Controls.Add(scope, 0, 0);
        var layout = Grid(new[] { 100f }, 32, -100, 25);
        scope.Controls.Add(layout);
        var scopeHeader=Grid(new[]{100f,-120f},-100);
        scopeHeader.Controls.Add(Caption(L10n.T("录制范围"),11,true),0,0);
        _regionEditButton.Click+=async(_,_)=>await EditRegionAsync();ButtonCell(scopeHeader,_regionEditButton,1);
        layout.Controls.Add(scopeHeader,0,0);
        var options = Grid(new[] { 33.34f, 33.33f, 33.33f }, -100);
        layout.Controls.Add(options, 0, 1);
        var labels = new[] { (L10n.T("全屏录制"), L10n.T("所选显示器"), "screen"), (L10n.T("区域录制"), L10n.T("拖拽选择范围"), "region"), (L10n.T("窗口录制"), L10n.T("选择一个窗口"), "window") };
        for (int i = 0; i < 3; i++)
        {
            int id = i;
            var item = new SelectCard { Title = labels[i].Item1, Subtitle = labels[i].Item2, Glyph = labels[i].Item3, AccessibleName = labels[i].Item1,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, i == 2 ? 0 : 8, 0) };
            item.Click += async (_, _) => await SelectScopeAsync((RecordingScope)id);
            options.Controls.Add(item, i, 0); _scopeCards[i] = item;
        }
        layout.Controls.Add(_selectionSummary, 0, 2);
        var settings = Card(); settings.Margin = new Padding(0, 0, 0, 12);
        host.Controls.Add(settings, 0, 1);
        var content = Grid(new[] { 100f }, 28, 52, -100);
        settings.Controls.Add(content);
        content.Controls.Add(Caption(L10n.T("录制设置"), 11, true), 0, 0);
        var tabs = new SegmentedTabsPanel { Margin = new Padding(0, 4, 0, 12) };
        content.Controls.Add(tabs, 0, 1);
        string[] names = { L10n.T("视频"), L10n.T("声音"), L10n.T("辅助"), L10n.T("输出") };
        for (int i = 0; i < names.Length; i++)
        {
            int id = i;
            var tab = new SoftButton { Text = names[i], Appearance = SoftButtonStyle.Tab };
            tab.Click += (_, _) => ShowSettingsPage(id);
            _settingsTabs.Add(tab); ButtonCell(tabs, tab, i, gap: 4);
        }
        content.Controls.Add(_settingsHost, 0, 2);
        AddSettingsPage(BuildVideoPage());
        AddSettingsPage(BuildAudioPage());
        AddSettingsPage(BuildHelpersPage());
        AddSettingsPage(BuildOutputPage());
        ShowSettingsPage(0);
        var gpu = Card(); gpu.Padding = new Padding(18, 10, 18, 10);
        host.Controls.Add(gpu, 0, 2);
        var gpuGrid = Grid(new[] { 100f, L10n.IsEnglish ? -180f : -150f }, 26, -100);
        gpuGrid.Controls.Add(_gpuName, 0, 0); gpuGrid.Controls.Add(_gpuHint, 0, 1);
        var diagnostics = new SoftButton { Text = L10n.T("设备诊断"), Glyph = "settings", Dock = DockStyle.Fill, Margin = new Padding(8, 1, 0, 1) };
        diagnostics.Click += (_, _) => ShowHardwareDiagnostics();
        gpuGrid.Controls.Add(diagnostics, 1, 0); gpuGrid.SetRowSpan(diagnostics, 2);
        gpu.Controls.Add(gpuGrid);
    }
    private void AddSettingsPage(Control content)
    {
        // The scroll host is only needed on unusually small screens or large text settings.
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Surface, Margin = Padding.Empty };
        content.Dock = DockStyle.Top;
        page.Controls.Add(content);
        var scrollHost = new ThemedScrollHost(page);
        _settingsHost.Controls.Add(scrollHost); _settingsPages.Add(scrollHost);
    }
    private void ShowSettingsPage(int index)
    {
        for (int i = 0; i < _settingsPages.Count; i++)
        {
            _settingsPages[i].Visible = i == index;
            _settingsTabs[i].Active = i == index;
        }
        _settingsPages[index].BringToFront();UpdateAudioLevels();
    }
    private Control BuildVideoPage()
    {
        var page = Grid(new[] { 100f }, 72, 84, 72, 72, 72, 36, 36);
        page.Height = 444;
        var display = Grid(new[] { 68f, 32f }, -100);
        _monitorBox.AccessibleName = L10n.T("显示器"); _fpsBox.AccessibleName = L10n.T("帧率"); _codecBox.AccessibleName = L10n.T("视频编码");
        StyleCombo(_monitorBox); StyleCombo(_fpsBox); StyleCombo(_codecBox);
        _fpsBox.Items.AddRange(new object[] { L10n.T("自动（推荐）"), "30 FPS", "60 FPS", "120 FPS", "15 FPS", "24 FPS" });
        StyleCombo(_profileBox); _profileBox.AccessibleName = L10n.T("配置预设");
        _profileBox.Items.AddRange(RecordingProfiles.Names.Cast<object>().ToArray());
        _codecBox.Items.AddRange(VideoEncoding.Choices(_caps)); _codecBox.DropDownHeight = 360;
        StyleCombo(_qualityBox); _qualityBox.AccessibleName = L10n.T("压缩画质");
        _qualityBox.Items.AddRange(VideoEncoding.QualityNames.Cast<object>().ToArray());
        var monitor = Field(L10n.T("显示器"), _monitorBox); monitor.Margin = new Padding(0, 0, 12, 12);
        display.Controls.Add(monitor, 0, 0); display.Controls.Add(Field(L10n.T("帧率（FPS）"), _fpsBox), 1, 0);
        page.Controls.Add(display, 0, 0);
        var purpose = Grid(new[] { 100f }, 24, -100);
        purpose.Margin = new Padding(0, 0, 0, 12);
        purpose.Controls.Add(Caption(L10n.T("录制用途"), 9, true), 0, 0);
        var choices = Grid(new[] { 50f, 50f }, -100); purpose.Controls.Add(choices, 0, 1);
        var titles = new[] { (L10n.T("游戏 · 低占用"), L10n.T("硬件优先 · 可回退 CPU"), "game"), (L10n.T("桌面 · RGB 无损"), L10n.T("保留原始色彩"), "screen") };
        for (int i = 0; i < 2; i++)
        {
            int id = i;
            var item = new SelectCard { Title = titles[i].Item1, Subtitle = titles[i].Item2, Glyph = titles[i].Item3, AccessibleName = titles[i].Item1,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, i == 0 ? 8 : 0, 0) };
            item.Click += (_, _) => { _settings.Purpose = (RecordingPurpose)id; RefreshSelected(); PlanChanged(); };
            choices.Controls.Add(item, i, 0); _purposeCards[i] = item;
        }
        page.Controls.Add(purpose, 0, 1);
        var encoding = Grid(new[] { 40f, 60f }, -100);
        var profile = Field(L10n.T("配置预设"), _profileBox); profile.Margin = new Padding(0, 0, 10, 12);
        encoding.Controls.Add(profile, 0, 0); encoding.Controls.Add(Field(L10n.T("视频编码"), _codecBox), 1, 0);
        page.Controls.Add(encoding, 0, 2);
        var quality = Grid(new[] { 40f, 60f }, -100);
        var qualityField = Field(L10n.T("压缩画质"), _qualityBox); qualityField.Margin = new Padding(0, 0, 10, 12);
        quality.Controls.Add(qualityField, 0, 0);
        _profileHint.AutoEllipsis = false; _profileHint.Margin = new Padding(0, 3, 0, 6);
        quality.Controls.Add(_profileHint, 1, 0); page.Controls.Add(quality, 0, 3);
        var folder = Grid(new[] { 100f, -96f }, -100);
        _folderBox.BorderStyle = BorderStyle.FixedSingle; _folderBox.Font = Ui.Font(9);
        _folderBox.Dock = DockStyle.Fill; _folderBox.Margin = new Padding(0, 3, 8, 0);
        var folderField = new TextField(_folderBox) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        folder.Controls.Add(folderField, 0, 0);
        _folderBrowse.Click += (_, _) => BrowseFolder(); ButtonCell(folder, _folderBrowse, 1);
        page.Controls.Add(Field(L10n.T("保存位置"), folder), 0, 4);
        StyleCheck(_dxgi); page.Controls.Add(_dxgi, 0, 5);
        StyleCheck(_modernWindow);page.Controls.Add(_modernWindow,0,6);
        return page;
    }
    private Control BuildOutputPage()
    {
        var page = Grid(new[] { 100f }, 72, 72, 36, 44, 44, 36, 36);
        page.Height = 340;
        foreach (var box in new[] { _resolutionBox, _containerBox, _segmentMinutesBox, _segmentSizeBox }) StyleCombo(box);
        _resolutionBox.AccessibleName = L10n.T("输出分辨率"); _containerBox.AccessibleName = L10n.T("输出格式");
        _segmentMinutesBox.AccessibleName = L10n.T("按时间自动分段"); _segmentSizeBox.AccessibleName = L10n.T("按大小自动分段");
        _resolutionBox.Items.AddRange(OutputOptions.ResolutionNames.Cast<object>().ToArray());
        _containerBox.Items.AddRange(OutputOptions.ContainerNames.Cast<object>().ToArray());
        _segmentMinutesBox.Items.AddRange(OutputOptions.SegmentMinutes.Select(n => (object)(n == 0 ? L10n.T("不按时间分段") : L10n.F($"每 {n} 分钟"))).ToArray());
        _segmentSizeBox.Items.AddRange(OutputOptions.SegmentGigabytes.Select(n => (object)(n == 0 ? L10n.T("不按视频大小分段") : L10n.F($"每 {n} GB"))).ToArray());
        var output = Grid(new[] { 55f, 45f }, -100);
        var resolution = Field(L10n.T("输出分辨率"), _resolutionBox); resolution.Margin = new Padding(0,0,10,12);
        output.Controls.Add(resolution,0,0); output.Controls.Add(Field(L10n.T("保存格式"),_containerBox),1,0); page.Controls.Add(output,0,0);
        var segments = Grid(new[] { 55f, 45f }, -100);
        var time = Field(L10n.T("自动分段 · 时间"),_segmentMinutesBox); time.Margin = new Padding(0,0,10,12);
        segments.Controls.Add(time,0,0); segments.Controls.Add(Field(L10n.T("自动分段 · 视频大小"),_segmentSizeBox),1,0); page.Controls.Add(segments,0,1);
        var segmentHint = Caption(L10n.T("连续分段不重启采集；停止后合并。缩放会改变原始像素。"),8,false,Ui.Subtle);
        segmentHint.AutoEllipsis=false; page.Controls.Add(segmentHint,0,2);
        _hints.SetToolTip(_resolutionBox,L10n.T("保持比例，不放大；720p / 1080p 按宽高上限缩小。原始尺寸适合逐像素无损。对已采集画面缩放，无法消除所有抓屏开销。"));
        _hints.SetToolTip(_containerBox,L10n.T("MKV 保留 FLAC 无损音频；MP4 使用 AAC 有损音频，视频不再次编码。FFV1 只能选择 MKV。"));
        _hints.SetToolTip(_segmentMinutesBox,L10n.T("按时间或视频大小先到者标记分组；后台连续写入保护块，大小并非严格上限。音频每 256 MB 切分，不中断设备采集。"));
        var note=Caption(L10n.T("低配预设使用 720p；中配使用 1080p。只缩小、不放大，保留选区比例。"),9,false,Ui.Subtle);
        note.AutoEllipsis=false; page.Controls.Add(note,0,3);
        var compatibility=Caption(L10n.T("MP4 不支持 FFV1。需要 RGB 原像素与无损音频时，选择原始尺寸 + MKV。"),9,false,Ui.Subtle);
        compatibility.AutoEllipsis=false; page.Controls.Add(compatibility,0,4);
        StyleCheck(_autoPerformance); page.Controls.Add(_autoPerformance,0,5);
        page.Controls.Add(Caption(L10n.T("持续偏慢才降帧；保持尺寸，不修改下次设置。调整时可能短暂间隔。"),8,false,Ui.Subtle),0,6);
        _hints.SetToolTip(_autoPerformance,L10n.T("默认关闭。仅有损编码：启动稳定后持续 15 秒低于 0.85×，逐级降至 30 / 24 / 15 / 10 FPS，调整之间至少 60 秒。无损编码不会自动降帧。"));
        return page;
    }
    private Control BuildAudioPage()
    {
        var page=Grid(new[]{100f},28,36,30,26,28,36,30,26,30,46,36);
        page.Height=352;
        StyleCheck(_systemSound); StyleCheck(_microphone); StyleCheck(_mixAudio);
        _systemSound.Font=Ui.Font(10,true);_microphone.Font=Ui.Font(10,true);
        StyleCombo(_systemDeviceBox);StyleCombo(_microphoneDeviceBox);
        _systemDeviceBox.Dock=DockStyle.Fill;_microphoneDeviceBox.Dock=DockStyle.Fill;_systemDeviceBox.Margin=_microphoneDeviceBox.Margin=new Padding(0,2,0,2);
        _systemDeviceBox.AccessibleName=L10n.T("系统声音播放设备");_microphoneDeviceBox.AccessibleName=L10n.T("麦克风设备");
        page.Controls.Add(_systemSound,0,0);page.Controls.Add(_systemDeviceBox,0,1);
        SetupTrackbar(_systemVolume);SetupTrackbar(_micVolume);page.Controls.Add(_systemVolume,0,2);
        page.Controls.Add(_systemLevelBar,0,3);_systemLevelBar.AccessibleName=L10n.T("系统声音电平");
        page.Controls.Add(_microphone,0,4);page.Controls.Add(_microphoneDeviceBox,0,5);page.Controls.Add(_micVolume,0,6);
        page.Controls.Add(_micLevelBar,0,7);_micLevelBar.AccessibleName=L10n.T("麦克风电平");
        page.Controls.Add(_mixAudio,0,8);
        var hint=Caption(L10n.T("打开声音页检测实时电平，试听检测不会保存文件。设备断开时显示不可用，录制素材补静音。"),8.5f,false,Ui.Subtle);
        hint.AutoEllipsis=false;page.Controls.Add(hint,0,9);
        var bottom=Grid(new[]{100f,-160f},-100);
        bottom.Controls.Add(Caption(L10n.T("勾选混音：播放时可同时听到两种声音"),8.5f,false,Ui.Subtle),0,0);
        _audioRefresh.Click+=(_,_)=>{if(!_busy&&_engine.State==RecorderState.Idle)RefreshAudioDevices();};ButtonCell(bottom,_audioRefresh,1);page.Controls.Add(bottom,0,10);
        return page;
    }
    private void RefreshAudioDevices()
    {
        _refreshingAudio=true;
        try
        {
            foreach(var item in new[]{(_systemDeviceBox,NAudio.CoreAudioApi.DataFlow.Render,_settings.SystemDeviceId),(_microphoneDeviceBox,NAudio.CoreAudioApi.DataFlow.Capture,_settings.MicrophoneDeviceId)})
            {
                List<AudioDeviceChoice> choices;
                try { choices=AudioDevices.Enumerate(item.Item2); }
                catch(Exception e){ShowNotice(L10n.T("音频设备检测失败：")+e.Message);choices=new(){new("",L10n.T("跟随系统默认设备"))};}
                AudioDevices.Populate(item.Item1,choices,item.Item3);
            }
        }
        finally{_refreshingAudio=false;}
        UpdateButtons();
    }
    private Control BuildHelpersPage()
    {
        var page = Grid(new[] { 100f }, 44, 40, 40, 40, 72, 28, 40, 40, 32, 32, 32, 32, 32, 40, 64);
        page.Height = 608;
        StyleCheck(_mouseCursor); StyleCheck(_countdown); StyleCheck(_minimize); StyleCheck(_excludeHud);
        page.Controls.Add(_mouseCursor, 0, 1); page.Controls.Add(_countdown, 0, 2); page.Controls.Add(_minimize, 0, 3);
        StyleCombo(_limitBox); _limitBox.AccessibleName = L10n.T("定时停止");
        _limitBox.Items.AddRange(RecordingLimit.Presets.Select(minutes => (object)new RecordingLimit(minutes)).ToArray());
        var limitEditor=Grid(new[]{100f,-112f},-100);
        _limitBox.Margin=new Padding(0,0,8,0);limitEditor.Controls.Add(_limitBox,0,0);
        _limitCustomButton.Click+=(_,_)=>EditLimit();ButtonCell(limitEditor,_limitCustomButton,1);
        page.Controls.Add(Field(L10n.T("定时停止"),limitEditor),0,4);
        var limitHint = Caption(L10n.T("按有效录制时长计时，暂停不计入；达到时长后自动停止并保存。"), 8.5f, false, Ui.Subtle);
        limitHint.AutoEllipsis = false; page.Controls.Add(limitHint, 0, 5);
        page.Controls.Add(_excludeHud, 0, 6);
        var compatibility = Caption(L10n.T("默认兼容显示。开启防录入后，浮窗可能不出现在远程桌面或屏幕共享中；点击「显示录制浮窗」可切回兼容显示。"), 8.5f, false, Ui.Subtle);
        compatibility.AutoEllipsis=false; page.Controls.Add(compatibility,0,7);
        _floatingButton.Dock = DockStyle.Fill; _floatingButton.Margin = new Padding(0, 4, 0, 4);
        _floatingButton.Click += (_, _) => ShowRecordingControls(); page.Controls.Add(_floatingButton, 0, 0);
        _hints.SetToolTip(_floatingButton, L10n.T("点击开始录制后显示；录制中可从这里找回浮窗。"));
        page.Controls.Add(Caption(L10n.T("全局快捷键"), 10, true), 0, 8);
        page.Controls.Add(_hotkeyLabels[0],0,9);
        page.Controls.Add(_hotkeyLabels[1],0,10);
        page.Controls.Add(_hotkeyLabels[2],0,11);
        _hotkeyStatus.AutoEllipsis = false; page.Controls.Add(_hotkeyStatus, 0, 12);
        var hint = Caption(L10n.T("可在「输出」页选择格式与自动分段。意外中断后，可通过右上角「恢复录制」找回临时片段。"), 9, false, Ui.Subtle);
        _hotkeyEditButton.Click+=(_,_)=>EditHotkeys();_hotkeyEditButton.Dock=DockStyle.Fill;page.Controls.Add(_hotkeyEditButton,0,13);
        hint.AutoEllipsis = false; page.Controls.Add(hint, 0, 14);
        return page;
    }
    private void BuildRight(TableLayoutPanel host)
    {
        var preview = Card(); preview.Margin = new Padding(0, 0, 0, 12);
        host.Controls.Add(preview, 0, 0);
        var previewGrid = Grid(new[] { 100f }, 30, -100, 24);
        preview.Controls.Add(previewGrid);
        previewGrid.Controls.Add(Caption(L10n.T("画面预览"), 11, true), 0, 0);
        _preview.Dock = DockStyle.Fill; _preview.Margin = new Padding(0, 4, 0, 0);
        previewGrid.Controls.Add(_preview, 0, 1);
        previewGrid.Controls.Add(Caption(L10n.T("单帧预览 · 点击下方「刷新预览」查看"), 8, false, Ui.Subtle), 0, 2);
        var info = Card(); info.Margin = new Padding(0, 0, 0, 12);
        info.CardColor = Ui.AccentLight;
        host.Controls.Add(info, 0, 1);
        var plan = Grid(new[] { 100f }, 24, 28, 26, -100);
        info.Controls.Add(plan);
        plan.Controls.Add(Caption(L10n.T("当前录制方案"), 10, true), 0, 0);
        plan.Controls.Add(_summary, 0, 1); plan.Controls.Add(_details, 0, 2);
        plan.Controls.Add(_encodingNote, 0, 3);
        var statusCard = Card(); statusCard.Margin = new Padding(0, 0, 0, 12);
        host.Controls.Add(statusCard, 0, 2);
        var statusGrid = Grid(new[] { 100f }, 32, 48, -100);
        statusCard.Controls.Add(statusGrid);
        var statusHeader = Grid(new[] { 100f, -116f }, -100);
        statusHeader.Controls.Add(Caption(L10n.T("录制状态"), 11, true), 0, 0);
        _lastFileButton.Click += (_, _) => OpenLastFile(); ButtonCell(statusHeader, _lastFileButton, 1);
        statusGrid.Controls.Add(statusHeader, 0, 0);
        var counters = Grid(new[] { 65f, 35f }, -100);
        counters.Controls.Add(_timerLabel, 0, 0);
        var state = Grid(new[] { 100f }, 24, -100);
        _status.TextAlign = ContentAlignment.MiddleRight;
        _frameLabel.TextAlign = ContentAlignment.MiddleRight;
        state.Controls.Add(_status, 0, 0); state.Controls.Add(_frameLabel, 0, 1);
        counters.Controls.Add(state, 1, 0); statusGrid.Controls.Add(counters, 0, 1);
        var hintBackground = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0), Padding = new Padding(8), CardColor = Ui.Background, BorderColor = Ui.Background, Radius = 10 };
        _operationHint.Dock = DockStyle.Fill; _operationHint.AutoEllipsis = true;
        _operationHint.BackColor = Ui.Background; _operationHint.ForeColor = Ui.Subtle; _operationHint.Font = Ui.Font(9);
        _operationHint.Text = L10n.T("选择录制范围与编码，点击开始录制。");
        hintBackground.Controls.Add(_operationHint); statusGrid.Controls.Add(hintBackground, 0, 2);
        var actions = Grid(new[] { 50f, 25f, 25f }, -100);
        actions.Margin = new Padding(0, 0, 0, 12); host.Controls.Add(actions, 0, 3);
        _recordButton.Font = Ui.Font(12, true);
        _recordButton.Click += async (_, _) => await ToggleRecordingAsync();
        _pauseButton.Enabled = false; _pauseButton.Click += async (_, _) => await TogglePauseAsync();
        _stopButton.Enabled = false; _stopButton.Click += async (_, _) => await StopRecordingAsync();
        ButtonCell(actions, _recordButton, 0); ButtonCell(actions, _pauseButton, 1); ButtonCell(actions, _stopButton, 2);
        var bottom = Grid(L10n.IsEnglish ? new[] { 36f, 30f, 34f } : new[] { 33.34f, 33.33f, 33.33f }, -100);
        host.Controls.Add(bottom, 0, 4);
        _testButton.Click += async (_, _) => await RefreshPreviewAsync();
        _refreshButton.Click += async (_, _) => await RefreshCapabilitiesAsync();
        var open = new SoftButton { Text = L10n.T("打开文件夹"), Glyph = "folder" }; open.Click += (_, _) => OpenFolder();
        ButtonCell(bottom, _testButton, 0); ButtonCell(bottom, _refreshButton, 1); ButtonCell(bottom, open, 2);
    }
    private void RestoreSettings()
    {
        _region=_settings.LastRegion?.Restore(SystemInformation.VirtualScreen)??Rectangle.Empty;
        _folderBox.Text = _settings.OutputFolder;
        _systemSound.Checked = _settings.RecordSystemAudio;
        _microphone.Checked = _settings.RecordMicrophone;
        _mixAudio.Checked = _settings.MixAudio;
        _mouseCursor.Checked = _settings.RecordCursor;
        _countdown.Checked = _settings.Countdown;
        SetLimit(_settings.StopAfterMinutes);
        UpdateHotkeyLabels();
        _minimize.Checked = _settings.MinimizeWhenRecording;
        _dxgi.Checked = _settings.UseDxgi;_modernWindow.Checked=_settings.UseModernWindowCapture;
        _excludeHud.Checked = _settings.ExcludeHudFromCapture;
        _recordingHud.Icon = Icon;
        RefreshAudioDevices();
        _systemVolume.Value = Math.Clamp((int)Math.Round(_settings.SystemVolume * 100),0,100);
        _micVolume.Value = Math.Clamp((int)Math.Round(_settings.MicrophoneVolume * 100),0,100);
        _fpsBox.SelectedIndex = Math.Max(0, Array.IndexOf(RecordingProfiles.FpsValues, _settings.Fps));
        _profileBox.SelectedIndex = (int)_settings.Profile;
        SelectEncoder(_settings.Encoder);
        _qualityBox.SelectedIndex = (int)_settings.Quality;
        _resolutionBox.SelectedIndex = (int)_settings.Resolution; _containerBox.SelectedIndex = (int)_settings.Container;
        _segmentMinutesBox.SelectedIndex = Array.IndexOf(OutputOptions.SegmentMinutes,_settings.SegmentMinutes);
        _segmentSizeBox.SelectedIndex = Array.IndexOf(OutputOptions.SegmentGigabytes,_settings.SegmentGigabytes);
        _autoPerformance.Checked = _settings.AutoPerformance;
        RefreshSelected();
    }

    private void OnRegionEdited(Rectangle bounds,bool committed)
    {
        _region=bounds;_preview.ChangePicture(null);
        if(committed)PlanChanged();
        else
        {
            RefreshSelected();
            _details.Text=L10n.F($"{bounds.Width}×{bounds.Height}  ·  拖动虚线移动，边角与边中方块缩放");
        }
    }
    private void SetLimit(int minutes)
    {
        int index=Enumerable.Range(0,_limitBox.Items.Count).FirstOrDefault(i=>(_limitBox.Items[i] as RecordingLimit)?.Minutes==minutes,-1);
        if(index<0){index=_limitBox.Items.Count;_limitBox.Items.Add(new RecordingLimit(minutes));}
        _limitBox.SelectedIndex=index;
    }
    private void EditLimit()
    {
        if(_busy||_engine.State!=RecorderState.Idle||_dialogDepth>0)return;
        _dialogDepth++;UpdateButtons();
        try
        {
            using var dialog=new NumberSettingsForm(L10n.T("自定义定时停止"),L10n.T("按有效录制时长计时，暂停时间不计入。"),new[]{L10n.T("录制时长（分钟，1～1440）")},new[]{Math.Max(1,_settings.StopAfterMinutes)},v=>v[0] is <1 or >1440?L10n.T("请输入 1～1440 分钟。"):null);
            if(dialog.ShowDialog(this)==DialogResult.OK)SetLimit(dialog.Values[0]);
        }
        finally{_dialogDepth--;UpdateButtons();}
    }
    internal static string? ValidateRegion(int[] values,Rectangle desktop)
    {
        if(values.Length!=4||values[2]<64||values[3]<64)return L10n.T("宽度和高度至少为 64 像素。");
        if(values[0]<desktop.Left||values[1]<desktop.Top||(long)values[0]+values[2]>desktop.Right||(long)values[1]+values[3]>desktop.Bottom)return L10n.T("选区必须位于当前屏幕范围内。");
        return null;
    }
    private async Task EditRegionAsync()
    {
        if(_busy||_engine.State!=RecorderState.Idle||_dialogDepth>0||_settings.Scope!=RecordingScope.Region||_region.Width<64)return;
        _busy=true;UpdateButtons();
        var windowState=WindowState;
        try
        {
            using var dialog=new NumberSettingsForm(L10n.T("微调录制区域"),L10n.T("坐标与尺寸均为实际屏幕像素，支持跨显示器选区。"),new[]{L10n.T("X 坐标"),L10n.T("Y 坐标"),L10n.T("宽度"),L10n.T("高度")},new[]{_region.X,_region.Y,_region.Width,_region.Height},v=>ValidateRegion(v,SystemInformation.VirtualScreen),true);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            var v=dialog.Values;var next=new Rectangle(v[0],v[1],v[2],v[3]);
            if(dialog.AdjustOnScreen)
            {
                _suspendOverlays=true;SyncRecordingUi();Hide();await Task.Delay(170);
                using var picker=new RegionPicker(next);
                if(picker.ShowDialog()!=DialogResult.OK)return;
                next=picker.SelectedBounds;
            }
            _region=next;_preview.ChangePicture(null);PlanChanged();
        }
        catch(Exception e){ShowError(L10n.T("调整选区失败：")+e.Message);}
        finally{_suspendOverlays=false;Show();if(windowState==FormWindowState.Maximized)WindowState=windowState;else RestoreToNormal();Activate();_busy=false;UpdateButtons();}
    }
    private void UpdateHotkeyLabels()
    {
        string[] actions={L10n.T("开始 / 停止录制"),L10n.T("暂停 / 继续录制"),L10n.T("保存截图")};
        for(int i=0;i<3;i++)_hotkeyLabels[i].Text=HotkeyBindings.Format(_settings.Hotkeys[i])+"     "+actions[i];
        _shortcutHint.Text=string.Join("  ·  ",Enumerable.Range(0,3).Select(i=>HotkeyBindings.Format(_settings.Hotkeys[i])+" "+new[]{L10n.T("开始/停止"),L10n.T("暂停/继续"),L10n.T("截图")}[i]));
        _hints.SetToolTip(_shortcutHint,_shortcutHint.Text);
    }
    private List<string> RegisterBindings(int[] bindings)
    {
        var failures=new List<string>();
        for(int i=0;i<3;i++)if(!HotkeyBindings.Register(Handle,HotkeyRecord+i,bindings[i]))failures.Add(HotkeyBindings.Format(bindings[i]));
        _hotkeyStatus.Text=failures.Count==0?L10n.T("全部快捷键可用"):L10n.T("被占用：")+string.Join(L10n.T("、"),failures);
        _hotkeyStatus.ForeColor=failures.Count==0?Ui.Mint:Ui.Warning;
        return failures;
    }
    private void UnregisterBindings(){for(int i=0;i<3;i++)WindowsInterop.UnregisterHotKey(Handle,HotkeyRecord+i);}
    private string? ApplyHotkeys(int[] bindings)
    {
        if(HotkeyBindings.Validate(bindings) is {} invalid)return invalid;
        var old=_settings.Hotkeys.ToArray();UnregisterBindings();
        var failures=RegisterBindings(bindings);
        if(failures.Count>0){UnregisterBindings();RegisterBindings(old);return L10n.T("组合键已被占用，请换一个组合。");}
        _settings.Hotkeys=bindings.ToArray();_settings.Save();UpdateHotkeyLabels();return null;
    }
    private void EditHotkeys()
    {
        if(_busy||_engine.State!=RecorderState.Idle||_dialogDepth>0)return;
        _dialogDepth++;UpdateButtons();
        try{using var dialog=new HotkeySettingsForm(_settings.Hotkeys,ApplyHotkeys);dialog.ShowDialog(this);}
        finally{_dialogDepth--;UpdateButtons();}
    }
    private void RefreshSelected()
    {
        for (int i=0;i<_scopeCards.Length;i++)
        {
            _scopeCards[i].Active=(int)_settings.Scope==i;
            _scopeCards[i].Invalidate();
        }
        for (int i=0;i<_purposeCards.Length;i++)
        {
            _purposeCards[i].Active=(int)_settings.Purpose==i;
            _purposeCards[i].Invalidate();
        }
        _selectionSummary.Text = _settings.Scope switch
        {
            RecordingScope.Region => _region.Width>0 ? L10n.F($"区域 · {_region.Width}×{_region.Height}  ( {_region.X}, {_region.Y} )") : L10n.T("区域 · 尚未选择"),
            RecordingScope.Window => _window is not null ? L10n.F($"窗口 · {_window.Title}") : L10n.T("窗口 · 尚未选择"),
            _ => L10n.T("全屏 · 保持屏幕原始分辨率")
        };
        SyncRecordingUi();
    }
    private void RefreshMonitors()
    {
        _monitors.Clear();_monitors.AddRange(WindowsInterop.GetMonitors());
        object? old = _monitorBox.SelectedItem;
        _monitorBox.BeginUpdate();_monitorBox.Items.Clear();
        _monitorBox.Items.Add(L10n.T("全部显示器（GDI 兼容模式）"));
        foreach (var screen in _monitors) _monitorBox.Items.Add(screen);
        string previous = (old as MonitorInfo)?.Name ?? _settings.MonitorName;
        int choose = _monitors.FindIndex(m => m.Name == previous);
        if (previous == "*") choose = -1;
        else if (choose < 0) choose = _monitors.FindIndex(m => m.Primary);
        _monitorBox.SelectedIndex=choose<0?0:choose+1;
        _monitorBox.EndUpdate();
        _gpuName.Text=WindowsInterop.DescribeGpu(_monitors);
        _gpuName.MaximumSize=new Size(700,0);
        PlanChanged();
    }
    private void HookEvents()
    {
        _languageBox.SelectedIndexChanged += (_,_) => ChangeLanguage();
        _monitorBox.SelectedIndexChanged += (_,_) => PlanChanged();
        _profileBox.SelectedIndexChanged += (_,_) => ApplyRecordingProfile();
        _fpsBox.SelectedIndexChanged += (_,_) => EncodingSettingChanged(true);
        _codecBox.SelectedIndexChanged += (_,_) => EncodingSettingChanged();
        _resolutionBox.SelectedIndexChanged += (_,_) => EncodingSettingChanged(true);
        _containerBox.SelectedIndexChanged += (_,_) => PlanChanged();
        _segmentMinutesBox.SelectedIndexChanged += (_,_) => PlanChanged();
        _segmentSizeBox.SelectedIndexChanged += (_,_) => PlanChanged();
        _autoPerformance.CheckedChanged += (_,_) => PlanChanged();
        _qualityBox.SelectedIndexChanged += (_,_) => EncodingSettingChanged(true);
        _systemDeviceBox.SelectedIndexChanged+=(_,_)=>{if(!_refreshingAudio)PlanChanged();};
        _microphoneDeviceBox.SelectedIndexChanged+=(_,_)=>{if(!_refreshingAudio)PlanChanged();};
        _folderBox.TextChanged += (_,_) => { _settings.OutputFolder=_folderBox.Text.Trim();_settings.Save(); };
        _systemSound.CheckedChanged += (_,_) => PlanChanged();
        _microphone.CheckedChanged += (_,_) => PlanChanged();
        _mixAudio.CheckedChanged += (_,_) => PlanChanged();
        _mouseCursor.CheckedChanged += (_,_) => PlanChanged();
        _countdown.CheckedChanged += (_,_) => PlanChanged();
        _limitBox.SelectedIndexChanged += (_,_) => PlanChanged();
        _minimize.CheckedChanged += (_,_) => PlanChanged();
        _dxgi.CheckedChanged += (_,_) => EncodingSettingChanged(true);_modernWindow.CheckedChanged+=(_,_)=>EncodingSettingChanged(true);
        _excludeHud.CheckedChanged += (_,_) => PlanChanged();
        _systemVolume.ValueChanged += (_,_) => PlanChanged();
        _micVolume.ValueChanged += (_,_) => PlanChanged();
        _engine.Message += msg => RunUi(() => ShowNotice(msg));
        _engine.StateUpdated += _ => RunUi(UpdateButtons);
        _engine.FinalizationProgress += progress => RunUi(()=>{_saveProgress=Math.Max(_saveProgress,progress);UpdateButtons();});
    }

    private void ChangeTheme(AppTheme theme)
    {
        if(_resourcesReleased||_languageSwitchPending||_dialogDepth>0||!Enum.IsDefined(theme))return;
        Ui.ChangeTheme(theme);_settings.Theme=Ui.Theme;_settings.Save();
    }
    private void UpdateThemeButton()
    {
        _themeButton.Glyph=Ui.IsDark?"sun":"moon";
        _themeButton.AccessibleName=Ui.IsDark?L10n.T("切换为白天模式"):L10n.T("切换为黑夜模式");
        _themeButton.AccessibleDescription=Ui.IsDark?L10n.T("黑夜模式"):L10n.T("白天模式");
        _hints.SetToolTip(_themeButton,_themeButton.AccessibleName);
        _themeButton.Enabled=!_languageSwitchPending;_themeButton.Invalidate();
    }
    private void OnThemeChanged(AppTheme previous)
    {
        if(_resourcesReleased)return;
        _settings.Theme=Ui.Theme;
        foreach(var page in _settingsPages)Ui.Retheme(page,previous);
        Ui.Retheme(_recordingHud,previous);Ui.Retheme(_regionIndicator,previous);
        if(_tray.ContextMenuStrip is {} menu)Ui.Retheme(menu,previous);
        UpdateThemeButton();SyncRecordingUi();
    }

    private void ChangeLanguage()
    {
        if(_languageSwitchPending||_languageBox.SelectedItem is not LanguageOption option||option.Language==_settings.Language)return;
        if(!CanChangeLanguage){_languageBox.SelectedIndex=(int)_settings.Language;return;}
        _settings.Language=option.Language;_settings.Save();
        if(LanguageChangeRequested is null)return;
        _languageSwitchPending=true;UpdateButtons();
        BeginInvoke((Action)(()=>{if(!_resourcesReleased)LanguageChangeRequested?.Invoke(this,EventArgs.Empty);}));
    }
    internal void CancelLanguageSwitch()
    {
        _settings.Language=L10n.Preference;_settings.Save();
        _languageSwitchPending=true;_languageBox.SelectedIndex=(int)_settings.Language;_languageSwitchPending=false;UpdateButtons();
    }
    internal MainFormUiState CaptureLanguageUiState()=>new(NormalWindowBounds,WindowState,
        Math.Max(0,_settingsPages.FindIndex(page=>page.Visible)),_lastSavedFile,
        _preview.Picture is null?null:(Bitmap)_preview.Picture.Clone(),_previewBounds);
    internal void RestoreLanguageUiState(MainFormUiState state)
    {
        StartPosition=FormStartPosition.Manual;Bounds=FitBounds(state.Bounds,Screen.FromRectangle(state.Bounds).WorkingArea);
        ShowSettingsPage(Math.Clamp(state.Page,0,_settingsPages.Count-1));
        if(state.LastFile is not null)RememberSavedFile(state.LastFile);
        if(state.Preview is not null){_preview.ChangePicture(state.Preview);_previewBounds=state.PreviewBounds;}
        if(state.WindowState==FormWindowState.Maximized)ToggleMaximize();
    }

    private void ApplyRecordingProfile()
    {
        if (_applyingProfile || _profileBox.SelectedIndex < 0) return;
        _applyingProfile = true;
        try
        {
            RecordingProfiles.Apply(_settings, (RecordingProfile)_profileBox.SelectedIndex);
            _fpsBox.SelectedIndex = Math.Max(0, Array.IndexOf(RecordingProfiles.FpsValues, _settings.Fps));
            SelectEncoder(_settings.Encoder);
            _qualityBox.SelectedIndex = (int)_settings.Quality;
            _resolutionBox.SelectedIndex = (int)_settings.Resolution;
            _dxgi.Checked = _settings.UseDxgi; _modernWindow.Checked = _settings.UseModernWindowCapture;
        }
        finally { _applyingProfile = false; }
        PlanChanged();
    }
    private void EncodingSettingChanged(bool preserveResolved = false)
    {
        if (_applyingProfile) return;
        // Manual edits own the current values instead of keeping a misleading preset label.
        _applyingProfile = true;
        try
        {
            if (preserveResolved && _settings.Profile != RecordingProfile.Custom)
                SelectEncoder(HardwareProbe.ResolveEncoder(_settings.Encoder,_settings.Purpose,_caps,_settings.Profile));
            _settings.Profile = RecordingProfile.Custom; _profileBox.SelectedIndex = 0;
        }
        finally { _applyingProfile = false; }
        PlanChanged();
    }
    private void PlanChanged()
    {
        if (_folderBox is null || _applyingProfile) return;
        _settings.Fps = _fpsBox.SelectedIndex >= 0 ? RecordingProfiles.FpsValues[_fpsBox.SelectedIndex] : 0;
        _settings.Encoder = (_codecBox.SelectedItem as EncoderOption)?.Encoder ?? EncoderChoice.Auto;
        _settings.Quality = _qualityBox.SelectedIndex >= 0 ? (CompressionQuality)_qualityBox.SelectedIndex : CompressionQuality.Balanced;
        _settings.Resolution = _resolutionBox.SelectedIndex >= 0 ? (OutputResolution)_resolutionBox.SelectedIndex : OutputResolution.Original;
        _settings.Container = _containerBox.SelectedIndex >= 0 ? (OutputContainer)_containerBox.SelectedIndex : OutputContainer.Mkv;
        if(_segmentMinutesBox.SelectedIndex>=0)_settings.SegmentMinutes=OutputOptions.SegmentMinutes[_segmentMinutesBox.SelectedIndex];
        if(_segmentSizeBox.SelectedIndex>=0)_settings.SegmentGigabytes=OutputOptions.SegmentGigabytes[_segmentSizeBox.SelectedIndex];
        _settings.AutoPerformance = _autoPerformance.Checked;
        _settings.RecordSystemAudio=_systemSound.Checked;
        _settings.RecordMicrophone=_microphone.Checked;
        _settings.MixAudio=_mixAudio.Checked;
        _settings.MonitorName=(_monitorBox.SelectedItem as MonitorInfo)?.Name ?? (_monitorBox.SelectedIndex == 0 ? "*" : _settings.MonitorName);
        _settings.RecordCursor=_mouseCursor.Checked;
        _settings.Countdown=_countdown.Checked;
        _settings.StopAfterMinutes=(_limitBox.SelectedItem as RecordingLimit)?.Minutes ?? 0;
        _settings.MinimizeWhenRecording=_minimize.Checked;
        _settings.UseDxgi=_dxgi.Checked;_settings.UseModernWindowCapture=_modernWindow.Checked;
        _settings.ExcludeHudFromCapture=_excludeHud.Checked;
        if(!_refreshingAudio)
        {
            _settings.SystemDeviceId=(_systemDeviceBox.SelectedItem as AudioDeviceChoice)?.Id??_settings.SystemDeviceId;
            _settings.MicrophoneDeviceId=(_microphoneDeviceBox.SelectedItem as AudioDeviceChoice)?.Id??_settings.MicrophoneDeviceId;
        }
        _settings.SystemVolume=_systemVolume.Value / 100f;
        _settings.MicrophoneVolume=_micVolume.Value / 100f;
        _settings.OutputFolder=_folderBox.Text.Trim();
        _settings.LastRegion=RegionSelection.FromRectangle(_region);
        if(_window is not null)_settings.LastWindow=WindowsInterop.DescribeWindow(_window)??_settings.LastWindow;
        _settings.Save();
        _profileHint.Text = RecordingProfiles.Hint(_settings.Profile);
        _hints.SetToolTip(_qualityBox, L10n.T("仅影响标为有损的压缩编码；无损编码不受此项影响。"));
        _hints.SetToolTip(_profileHint, _profileHint.Text + L10n.T(" 普通压缩为有损，奇数尺寸只在右侧 / 底部补 1 像素；帧率和编码效果取决于处理器与磁盘。"));
        RefreshSelected();
        _hints.SetToolTip(_selectionSummary,_selectionSummary.Text+(_settings.Scope==RecordingScope.Region?L10n.T("；拖动虚线或顶部箭头块移动选区，拖动边角与边中方块缩放。"):""));
        UpdateButtons();
        try
        {
            if (!_caps.Available) { _summary.Text=L10n.T("请配置 FFmpeg 后开始"); _details.Text=L10n.T("点右上角「FFmpeg 设置」选择程序"); return; }
            var plan=_engine.State != RecorderState.Idle && _engine.CurrentPlan is { } current ? current : GetPlan();
            if(_preview.Picture is not null&&_previewBounds!=plan.Bounds)_preview.ChangePicture(null);
            _summary.Text=plan.EncoderName;
            _encodingNote.Text = plan.Encoder == EncoderChoice.NvencHevcLossless
                ? L10n.T("10-bit 4:4:4 编码无损；严格 RGB 保真选 x264rgb / FFV1。")
                : plan.IsLossless ? L10n.T("无损编码；实际流畅度取决于设备性能。")
                : L10n.F($"有损压缩 · {VideoEncoding.QualityNames[(int)plan.Quality]}；合成不重复编码。");
            _details.Text=$"{plan.OutputSize.Width}×{plan.OutputSize.Height}  ·  {plan.Fps} FPS  ·  {(plan.Container==OutputContainer.Mp4?"MP4":"MKV")} · {plan.BackendName}";
            if(plan.Resolution!=OutputResolution.Original)_encodingNote.Text += L10n.T(" 缩放不保留原始像素。");
            _hints.SetToolTip(_details,_details.Text);_hints.SetToolTip(_summary,_summary.Text);
            _hints.SetToolTip(_encodingNote, plan.Encoder == EncoderChoice.NvencHevcLossless
                ? L10n.T("HEVC Rext / 10-bit YUV 4:4:4 / BT.709 全范围。编码前后 YUV 一致；RGB 转换不保证逐像素可逆，当前为 8-bit SDR 捕获，不是 HDR。DXGI 也会下载到 CPU 进行色彩转换。")
                : _encodingNote.Text);
            if (_settings.Encoder==EncoderChoice.Auto && VideoEncoding.IsHardware(plan.Encoder) && VideoEncoding.CpuFallback(_caps,plan.IsLossless)==EncoderChoice.Auto)
                _details.Text += L10n.T(" · 无 CPU 回退");
        }
        catch (Exception e) { _preview.ChangePicture(null);_summary.Text=L10n.T("待选择录制范围");_details.Text=e.Message; }
    }

    private RecordPlan GetPlan()
    {
        MonitorInfo? monitor = _monitorBox.SelectedItem as MonitorInfo;
        var rectangle = monitor?.Bounds ?? SystemInformation.VirtualScreen;
        if (_settings.Scope==RecordingScope.Region)
        {
            if (_region.Width < 64 || _region.Height < 64) throw new InvalidOperationException(L10n.T("请先拖动鼠标选择录制区域。"));
            rectangle=_region;
            monitor=_monitors.FirstOrDefault(m => m.Bounds.Contains(rectangle));
        }
        else if (_settings.Scope==RecordingScope.Window)
        {
            if(_window is null)RestoreRememberedWindow();
            if (_window is null) throw new InvalidOperationException(L10n.T("未找到唯一匹配的上次窗口，请点击「窗口录制」重新选择。"));
            rectangle=WindowsInterop.GetWindowClientBounds(_window.Handle);
            monitor=_monitors.FirstOrDefault(m => m.Bounds.Contains(rectangle));
        }
        if(_settings.Scope!=RecordingScope.Window)ScreenCapture.ValidateBounds(rectangle,SystemInformation.VirtualScreen);
        string adapter=monitor?.Adapter ?? WindowsInterop.DescribeGpu(_monitors);
        EncoderChoice encoder=HardwareProbe.ResolveEncoder(_settings.Encoder,_settings.Purpose,_caps,_settings.Profile);
        int fps=HardwareProbe.ResolveFps(_settings.Fps,monitor,_settings.Purpose,adapter);
        CaptureBackend backend=HardwareProbe.ResolveBackend(_settings.UseDxgi && !RecordingProfiles.RequiresCpu(_settings.Profile),_settings.Scope,encoder,monitor,rectangle,_caps,_settings.UseModernWindowCapture && !RecordingProfiles.RequiresCpu(_settings.Profile));
        return new RecordPlan(rectangle,monitor,_settings.Scope,_settings.Purpose,encoder,backend,fps,_mouseCursor.Checked,
            _systemSound.Checked,_microphone.Checked,_systemVolume.Value/100f,_micVolume.Value/100f,_mixAudio.Checked,_settings.SystemDeviceId,_settings.MicrophoneDeviceId,_settings.Scope==RecordingScope.Window?_window!.Handle.ToInt64():0,_settings.Profile,_settings.Quality,_settings.Resolution,_settings.Container,_settings.SegmentMinutes,_settings.SegmentGigabytes,_settings.AutoPerformance);
    }

    private async Task SelectScopeAsync(RecordingScope scope)
    {
        if (_engine.State!=RecorderState.Idle || _busy) return;
        _busy = true; UpdateButtons();
        try
        {
        if (scope==RecordingScope.Region)
        {
            _suspendOverlays = true; UpdateButtons();
            Hide();
            try
            {
                await Task.Delay(170);
                using var selector=new RegionPicker();
                if (selector.ShowDialog()==DialogResult.OK) _region=selector.SelectedBounds;
                else return;
            }
            catch (Exception e) { ShowError(L10n.T("无法选择区域：")+e.Message);return; }
            finally { _suspendOverlays = false; Show(); RestoreToNormal(); Activate(); SyncRecordingUi(); }
        }
        else if (scope==RecordingScope.Window)
        {
            using var chooser=new WindowPicker();
            if (chooser.ShowDialog(this)!=DialogResult.OK || chooser.SelectedWindow is null) return;
            _window=chooser.SelectedWindow;
            ShowNotice(L10n.T("窗口录制跟随内容区域；优先现代捕获，初始化失败自动尝试 GDI。目标关闭、最小化或改变尺寸时停止保存，可在视频页切换兼容模式。"));
        }
        _settings.Scope=scope;
        RefreshSelected();PlanChanged();
        }
        finally { _busy = false; UpdateButtons(); }
    }
    private void SelectEncoder(EncoderChoice encoder)
    {
        var option = _codecBox.Items.OfType<EncoderOption>().FirstOrDefault(item => item.Encoder == encoder);
        _codecBox.SelectedIndex = option is null ? 0 : _codecBox.Items.IndexOf(option);
    }
    private void UpdateEncoderChoices()
    {
        var selected = (_codecBox.SelectedItem as EncoderOption)?.Encoder ?? _settings.Encoder;
        bool wasApplying = _applyingProfile; _applyingProfile = true;
        _codecBox.BeginUpdate();
        try
        {
            _codecBox.Items.Clear();
            _codecBox.Items.AddRange(VideoEncoding.Choices(_caps));
            SelectEncoder(selected);
        }
        finally { _codecBox.EndUpdate(); _applyingProfile = wasApplying; }
    }
    private async Task RefreshCapabilitiesAsync()
    {
        if (_busy || _detecting || _engine.State != RecorderState.Idle) return;
        _detecting = true; _busy = true; UpdateButtons();
        try
        {
            _status.Text = L10n.T("正在检测 FFmpeg…");
            RefreshMonitors();
            RefreshAudioDevices();
            string path = HardwareProbe.LocateFfmpeg(_settings.FfmpegPath);
            _caps = await HardwareProbe.ProbeAsync(path);
            UpdateEncoderChoices();
            _gpuHint.Text = _caps.Nvenc || _caps.NvencH264 || _caps.NvencHevc || _caps.NvencHevcLossless || _caps.IntelH264 || _caps.AmdH264 ? L10n.T("硬件编码已验证 · 也可选择 CPU 编码")
                : _caps.H264Cpu || _caps.Mpeg4Cpu || _caps.X264Rgb || _caps.Ffv1 ? L10n.T("无需独立显卡 · 可用 CPU 编码") : L10n.T("未检测到可用编码器");
            _hints.SetToolTip(_gpuHint, L10n.T("点击设备诊断，查看各编码器试编码结果、声卡及显示器 DPI。试编码通过不等于所有分辨率均可用。"));
            RefreshStorage();
            _status.Text = _caps.Available && _caps.AnyEncoder ? L10n.T("✓ 已就绪") : L10n.T("需要配置 FFmpeg");
            ShowNotice(_caps.Available && _caps.AnyEncoder ? L10n.T("已就绪，选择录制范围后即可开始。") : _caps.Error.Length > 0 ? _caps.Error : L10n.T("未检测到可用编码器，请配置 FFmpeg。"));
            int recoverable = RecordingFinalizer.Discover(_folderBox.Text).Count;
            if (recoverable > 0) ShowNotice(L10n.F($"发现 {recoverable} 项未完成录制，点击右上角「恢复录制」处理。"));
        }
        catch (Exception e) { ShowNotice(L10n.T("检测失败：") + e.Message); }
        finally { _detecting = false; _busy = false; PlanChanged(); UpdateButtons(); }
    }

    private void ChooseFfmpeg()
    {
        if(_busy||_dialogDepth>0||_engine.State!=RecorderState.Idle)return;
        bool selected=false;_busy=true;UpdateButtons();
        try
        {
            using var chooser=new OpenFileDialog {Title=L10n.T("选择 Windows FFmpeg 程序"),Filter=L10n.T("FFmpeg 程序 (ffmpeg.exe)|ffmpeg.exe|可执行文件 (*.exe)|*.exe"),CheckFileExists=true};
            if(chooser.ShowDialog(this)==DialogResult.OK){_settings.FfmpegPath=chooser.FileName;_settings.Save();selected=true;}
        }
        finally{_busy=false;UpdateButtons();}
        if(selected)_=RefreshCapabilitiesAsync();
    }
    private void BrowseFolder()
    {
        if(_busy||_dialogDepth>0||_engine.State!=RecorderState.Idle)return;
        _busy=true;UpdateButtons();
        try
        {
            using var dialog=new FolderBrowserDialog {Description=L10n.T("选择录制文件的保存目录"),UseDescriptionForTitle=true};
            if(Directory.Exists(_folderBox.Text))dialog.SelectedPath=_folderBox.Text;
            if(dialog.ShowDialog(this)==DialogResult.OK)_folderBox.Text=dialog.SelectedPath;
        }
        finally{_busy=false;UpdateButtons();}
    }
    private void OpenFolder()
    {
        try
        {
            string directory=_folderBox.Text;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) {UseShellExecute=true});
        }
        catch (Exception e) { ShowError(L10n.T("无法打开目录：")+e.Message); }
    }
    private void ShowRecovery()
    {
        if (_busy || _engine.State != RecorderState.Idle) return;
        _busy = true; UpdateButtons();
        try { using var dialog = new RecoveryForm(_folderBox.Text, _caps); dialog.ShowDialog(this); }
        finally { _busy = false; UpdateButtons(); }
    }
    private void ShowHardwareDiagnostics()
    {
        if (_dialogDepth > 0) return;
        _dialogDepth++; UpdateButtons();
        try { using var dialog = new HardwareDiagnosticsForm(_caps); dialog.ShowDialog(this); }
        finally { _dialogDepth--; UpdateButtons(); }
    }
    private async Task ToggleRecordingAsync()
    {
        if (_countdownCancellation is not null) { _countdownCancellation.Cancel(); return; }
        if (_engine.State == RecorderState.Idle) await StartRecordingAsync();
        else await StopRecordingAsync();
    }
    private async Task<Bitmap> CaptureFrameAsync(RecordPlan plan)
    {
        if(plan.Backend==CaptureBackend.WindowsGraphics)
        {
            try
            {
                return await MediaProcess.ReadImageAsync(_caps.Ffmpeg,new[]{"-v","error","-f","lavfi","-i",FfmpegArguments.GraphicsWindowSource(plan),
                    "-frames:v","1","-f","image2pipe","-vcodec","png","pipe:1"},CancellationToken.None);
            }catch(Exception e){ShowNotice(L10n.T("现代窗口静态捕获失败，尝试 GDI：")+e.Message);}
        }
        return plan.WindowHandle!=0?ScreenCapture.TakeWindow(new IntPtr(plan.WindowHandle)):ScreenCapture.Take(plan.Bounds);
    }
    private async Task RefreshPreviewAsync()
    {
        if (_engine.State!=RecorderState.Idle || _busy) return;
        _busy=true;
        _suspendOverlays = true; UpdateButtons();
        bool wasVisible=Visible;
        try
        {
            var plan=GetPlan();var bounds=plan.Bounds;
            if(plan.WindowHandle==0)ScreenCapture.ValidateBounds(bounds,SystemInformation.VirtualScreen);ScreenCapture.ValidateBitmapSize(bounds.Size);
            Hide(); await Task.Delay(200);
            _preview.ChangePicture(await CaptureFrameAsync(plan)); _previewBounds=bounds;
            ShowNotice(L10n.T("已刷新所选区域的静态画面预览。"));
        }
        catch (Exception e) { ShowError(L10n.T("无法预览：")+e.Message); }
        finally { _suspendOverlays=false; _busy=false; if(wasVisible){Show();Activate();} UpdateButtons(); }
    }

    private async Task StartRecordingAsync()
    {
        if (_languageSwitchPending || _busy || _dialogDepth>0 || _engine.State!=RecorderState.Idle) return;
        if (!_caps.Available)
        {
            var response=MessageBox.Show(this,L10n.T("未检测到 FFmpeg，是否现在选择 ffmpeg.exe？"),L10n.T("缺少录制组件"),
                MessageBoxButtons.YesNo,MessageBoxIcon.Information);
            if(response==DialogResult.Yes) ChooseFfmpeg();
            return;
        }
        _regionIndicator.FinishDrag(true);
        RecordPlan plan;
        try
        {
            plan=GetPlan();
            if (plan.Encoder==EncoderChoice.Auto) throw new InvalidOperationException(L10n.T("没有适合此模式的编码器。请选择 H.264 CPU / MPEG-4，或使用随附 FFmpeg。"));
            bool encoded = VideoEncoding.Supported(plan.Encoder, _caps);
            if (!encoded && plan.Encoder == EncoderChoice.NvencHevcLossless)
                throw new InvalidOperationException(L10n.T("当前显卡、驱动或 FFmpeg 未通过 HEVC 10-bit 4:4:4 无损验证。请在设备诊断查看原因；需要 RGB 无损可选择 x264rgb / FFV1。请同时保留 ffprobe.exe。"));
            if (!encoded) throw new InvalidOperationException(VideoEncoding.IsHardware(plan.Encoder)
                ? L10n.T("当前硬件或驱动无法使用所选硬件编码。没有独立显卡也可以录制，请选择 H.264 CPU / MPEG-4，或「纯 CPU」预设。")
                : L10n.T("当前 FFmpeg 不支持所选 CPU 编码器，请使用「自动智能选择」或更换 FFmpeg。"));
            if (string.IsNullOrWhiteSpace(_folderBox.Text)) throw new InvalidOperationException(L10n.T("请设置有效的保存目录。"));
            Directory.CreateDirectory(_folderBox.Text);
            string root=Path.GetPathRoot(Path.GetFullPath(_folderBox.Text))??"";
            try
            {
                long bytes=new DriveInfo(root).AvailableFreeSpace;
                if (bytes<4L*1024*1024*1024)
                {
                    if (MessageBox.Show(this,L10n.F($"磁盘剩余不足 4GB（约 {bytes/1024.0/1024/1024:0.0}GB），录制和合成可能需要较大空间，仍要继续吗？"),
                        L10n.T("磁盘空间不足"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
                }
            }
            catch (Exception) { /* Network and virtual drives may lack free-space data. */ }
        }
        catch (Exception e) { ShowError(e.Message);return; }
        string outputFolder = Path.GetFullPath(_folderBox.Text);
        bool autoCodec = _settings.Encoder == EncoderChoice.Auto;
        _sessionLimit = new RecordingLimit(_settings.StopAfterMinutes);
        _adaptivePerformance.Reset(); _nextPerformanceWarning=DateTime.MinValue; _sessionDisplays=DisplayFingerprint();
        _lowSpaceWarned=false; _busy = true; _startingSession = true; _sessionStartedAt = null;
        _timerLabel.Text = "00:00:00"; _frameLabel.Text = L10n.T("0 帧");
        PlaceRecordingHud(plan.Bounds,plan.Scope==RecordingScope.Region);
        _countdownSeconds = _countdown.Checked ? 3 : 0;
        if (_countdown.Checked) _countdownCancellation = new CancellationTokenSource();
        UpdateButtons();
        // Show once before the final placement so the HUD has its actual monitor DPI size.
        PlaceRecordingHud(plan.Bounds,plan.Scope==RecordingScope.Region);
        bool hidden=false;
        try
        {
            if (_minimize.Checked && WindowNative.IsWindowVisible(_recordingHud.Handle))
            {
                Hide();hidden=true;
                SyncRecordingUi();
            }
            if (_countdownCancellation is not null)
            {
                for (int count=3;count>=1;count--)
                {
                    _tray.Text=L10n.F($"ScreenLossless · {count} 秒后开始录制");
                    ShowNotice(L10n.F($"{count} 秒后开始录制…")); _countdownSeconds = count; SyncRecordingUi();
                    await Task.Delay(1000, _countdownCancellation.Token);
                }
            }
            _countdownCancellation?.Token.ThrowIfCancellationRequested();
            _countdownCancellation?.Dispose(); _countdownCancellation = null; _countdownSeconds = 0;
            UpdateButtons();
            if(plan.WindowHandle!=0&&WindowsInterop.GetWindowClientBounds(new IntPtr(plan.WindowHandle)).Size!=plan.Bounds.Size)throw new InvalidOperationException(L10n.T("倒计时期间窗口尺寸改变，请重新开始录制。"));
            ShowNotice(L10n.T("开始新的录制会话。"));
            if (_sessionLimit.Minutes > 0) ShowNotice(L10n.F($"定时停止：累计有效录制 {_sessionLimit.Minutes} 分钟后自动保存；暂停不计时。"));
            await _engine.StartAsync(plan,_caps,outputFolder,autoCodec);
            _sessionStartedAt = DateTime.Now - _engine.Duration;
            if (!_recordingHud.CaptureExcluded) ShowNotice(L10n.T("浮窗使用兼容显示；请放在录制范围外。全屏录制可能录入浮窗，可在「辅助」中开启防录入。"));
            _status.Text=L10n.T("●  正在录制");_status.ForeColor=Ui.Recording;
            _tray.Text=L10n.T("ScreenLossless · 正在录制");
            _tray.ShowBalloonTip(1700,L10n.T("ScreenLossless 已开始"),_shortcutHint.Text,ToolTipIcon.Info);
            _restoreAfterRecord=hidden;
            PlanChanged();
        }
        catch (OperationCanceledException)
        {
            ShowNotice(L10n.T("已取消开始录制。"));
            _status.Text = L10n.T("已取消倒计时"); _tray.Text = L10n.T("ScreenLossless · 准备就绪");
        }
        catch (Exception e)
        {
            Show();RestoreToNormal();
            ShowError(L10n.T("启动录制失败：")+e.Message);
        }
        finally
        {
            if (_engine.State==RecorderState.Idle && hidden) { Show(); RestoreToNormal(); }
            _countdownCancellation?.Dispose(); _countdownCancellation = null; _countdownSeconds = 0;
            _startingSession=false; _busy=false;UpdateButtons();
            if (_closeAfterCountdown) { _reallyClosing = true; Close(); }
        }
    }

    private async Task TogglePauseAsync()
    {
        if (_busy || _engine.State is not (RecorderState.Recording or RecorderState.Paused)) return;
        _busy=true;UpdateButtons();
        try
        {
            if (_engine.State==RecorderState.Recording) await _engine.PauseAsync();
            else
            {
                if(_engine.CurrentPlan is {WindowHandle:not 0} plan&&WindowsInterop.GetWindowClientBounds(new IntPtr(plan.WindowHandle)).Size!=plan.Bounds.Size)throw new InvalidOperationException(L10n.T("窗口尺寸改变，请先停止保存已有片段。"));
                await _engine.ResumeAsync();
            }
        }
        catch (Exception e) { ShowError(L10n.T("暂停/继续失败：")+e.Message); }
        finally { _busy=false;UpdateButtons(); }
    }

    private async Task<bool> StopRecordingAsync()
    {
        if(_engine.State==RecorderState.Finalizing){_saveCancellation?.Cancel();UpdateButtons();return false;}
        if (_countdownCancellation is not null) { _countdownCancellation.Cancel(); return false; }
        if (_busy || _engine.State is not (RecorderState.Recording or RecorderState.Paused)) return false;
        bool saved = false;
        using var saveRequest=new CancellationTokenSource();_saveCancellation=saveRequest;_saveProgress=0;
        _busy=true;UpdateButtons();
        try
        {
            _status.Text=L10n.T("正在合成保存…");
            string file=await _engine.StopAsync(saveRequest.Token);
            saved = true;
            _status.Text=L10n.T("✓  保存完成");_status.ForeColor=Ui.Mint;
            _tray.Text=L10n.T("ScreenLossless · 保存完成");
            RememberSavedFile(file);ShowNotice((_engine.LastStopReason.Length > 0 ? _engine.LastStopReason + Environment.NewLine : "") + L10n.T("文件已保存：") + file);
            if (_restoreAfterRecord || !Visible || WindowState == FormWindowState.Minimized)
            {
                Show();RestoreToNormal();Activate();
            }
            _tray.ShowBalloonTip(2500,L10n.T("录制已完成"),Path.GetFileName(file),ToolTipIcon.Info);
        }
        catch(OperationCanceledException)
        {
            _status.Text=L10n.T("已取消保存 · 可恢复录制");_tray.Text=L10n.T("ScreenLossless · 原始素材已保留");
            ShowNotice(L10n.T("已取消保存，原始片段已保留，点击「恢复录制」可继续保存。"));Show();RestoreToNormal();
        }
        catch (Exception e)
        {
            _status.Text=L10n.T("完成失败，已保留临时文件");
            ShowError(e.Message);
            Show();RestoreToNormal();
        }
        finally
        {
            _saveCancellation=null;_restoreAfterRecord=false;_busy=false;UpdateButtons();
        }
        return saved;
    }

    private async Task SaveScreenshotAsync()
    {
        if (_busy||_dialogDepth>0) return;
        _busy=true;
        _suspendOverlays = true; UpdateButtons();
        bool showAgain=Visible;
        try
        {
            var plan=_engine.State!=RecorderState.Idle&&_engine.CurrentPlan is {} current?current:GetPlan();
            Rectangle bounds=plan.Bounds;
            if(plan.WindowHandle==0)ScreenCapture.ValidateBounds(bounds,SystemInformation.VirtualScreen);ScreenCapture.ValidateBitmapSize(bounds.Size);
            if(showAgain)Hide();await Task.Delay(150);
            using var bmp=await CaptureFrameAsync(plan);
            string dir=_folderBox.Text;
            Directory.CreateDirectory(dir);
            string file=Path.Combine(dir,"Screenshot_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")+".png");
            bmp.Save(file,System.Drawing.Imaging.ImageFormat.Png);
            RememberSavedFile(file);ShowNotice(L10n.T("截图已保存：")+file);
            _tray.ShowBalloonTip(1200,L10n.T("截图完成"),Path.GetFileName(file),ToolTipIcon.Info);
        }
        catch (Exception e) { ShowError(L10n.T("截图失败：")+e.Message); }
        finally { _suspendOverlays=false;_busy=false;if(showAgain){Show();Activate();} UpdateButtons(); }
    }

    private void RefreshRecordingCounters()
    {
        if(_startingSession||_countdownCancellation is not null||_engine.CurrentPlan is null)return;
        var duration=_engine.Duration;
        _timerLabel.Text=$"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        _frameLabel.Text=L10n.F($"累计 {_engine.FrameCount:N0} 帧 · {_engine.SegmentCount} 段");
    }
    private void UpdateButtons()
    {
        if(_resourcesReleased)return;
        RefreshRecordingCounters();
        bool recording = _engine.State is RecorderState.Recording or RecorderState.Paused;
        bool countdown = _countdownCancellation is not null;
        bool showFloating = _startingSession || countdown || _engine.State != RecorderState.Idle;
        _floatingButton.Enabled = showFloating;
        if (_floatingMenu is not null) _floatingMenu.Enabled = showFloating;
        bool editable = !_busy && !_languageSwitchPending && _dialogDepth==0 && _engine.State == RecorderState.Idle;
        _languageBox.Enabled=CanChangeLanguage&&!_languageSwitchPending;UpdateThemeButton();
        _recordButton.Text = countdown ? L10n.T("取消倒计时") : _engine.State==RecorderState.Finalizing ? L10n.F($"正在保存 {_saveProgress:P0}") : _startingSession||_engine.State==RecorderState.Starting ? L10n.T("正在处理…") : recording ? L10n.T("结束并保存") : L10n.T("开始录制");
        _recordButton.Glyph = countdown ? "close" : recording ? "stop" : "play";
        _recordButton.Enabled = !_languageSwitchPending && (countdown || (!_busy && ((_engine.State == RecorderState.Idle&&_dialogDepth==0) || recording)));
        _pauseButton.Enabled = !_busy && recording;
        _pauseButton.Text = _engine.State == RecorderState.Paused ? L10n.T("继续") : L10n.T("暂停");
        _pauseButton.Glyph = _engine.State == RecorderState.Paused ? "play" : "pause";
        bool saving=_engine.State==RecorderState.Finalizing;
        _stopButton.Text=saving?(_saveCancellation?.IsCancellationRequested==true?L10n.T("取消中…"):L10n.T("取消保存")):L10n.T("停止");
        float stopSize=saving?8.5f:9.5f;
        if(_stopButton.Font.Size!=stopSize)_stopButton.Font=Ui.Font(stopSize);
        _stopButton.Enabled = countdown || (!_busy && recording) || (saving&&_saveCancellation is { IsCancellationRequested:false });
        if(saving)_status.Text=_saveCancellation?.IsCancellationRequested==true?L10n.T("正在取消保存…"):L10n.F($"保存 {_saveProgress:P0}");
        _regionEditButton.Enabled=editable&&_settings.Scope==RecordingScope.Region&&_region.Width>=64;
        foreach (var button in _scopeCards) button.Enabled = editable;
        foreach (var button in _purposeCards) button.Enabled = editable;
        foreach (Control control in new Control[] { _monitorBox, _fpsBox, _codecBox, _profileBox, _qualityBox, _resolutionBox, _containerBox, _segmentMinutesBox, _segmentSizeBox, _autoPerformance, _dxgi, _modernWindow, _folderBox, _folderBrowse, _testButton, _refreshButton,
            _systemSound, _microphone, _mixAudio, _systemVolume, _micVolume, _mouseCursor, _countdown, _minimize, _limitBox, _limitCustomButton, _hotkeyEditButton }) control.Enabled = editable;
        _qualityBox.Enabled = editable && !VideoEncoding.IsLossless(HardwareProbe.ResolveEncoder(_settings.Encoder,_settings.Purpose,_caps,_settings.Profile));
        _systemDeviceBox.Enabled=editable&&_systemSound.Checked;_microphoneDeviceBox.Enabled=editable&&_microphone.Checked;
        _systemVolume.Enabled=editable&&_systemSound.Checked;_micVolume.Enabled=editable&&_microphone.Checked;
        _mixAudio.Enabled=editable&&_systemSound.Checked&&_microphone.Checked;_audioRefresh.Enabled=editable;
        if (_engine.State == RecorderState.Recording)
        {
            _status.ForeColor=Ui.Recording;_status.Text = L10n.T("●  正在录制"); _tray.Text = L10n.T("ScreenLossless · 正在录制");
        }
        else if (_engine.State == RecorderState.Paused)
        {
            _status.ForeColor=Ui.Warning;_status.Text = L10n.T("Ⅱ  已暂停"); _tray.Text = L10n.T("ScreenLossless · 已暂停");
        }
        SyncRecordingUi();UpdateAudioLevels();
    }

    private static string DisplayFingerprint() => string.Join(";", WindowsInterop.GetMonitors().OrderBy(m=>m.Name,StringComparer.Ordinal).Select(m=>$"{m.Name}:{m.Bounds}:{m.Primary}"));
    private void QueueDeviceRefresh(bool displays)
    {
        _displayPending|=displays;_devicesPending=true;_refreshDevicesAt=DateTime.UtcNow.AddSeconds(1);
        if(displays){_regionIndicator.FinishDrag(false);_countdownCancellation?.Cancel();}
    }
    private void UpdateTick()
    {
        if(_resourcesReleased)return;
        if(_devicesPending&&DateTime.UtcNow>=_refreshDevicesAt&&!_busy&&_dialogDepth==0)
        {
            if(_displayPending&&(_engine.State is RecorderState.Recording or RecorderState.Paused))
            {
                if (_sessionDisplays != DisplayFingerprint())
                {
                    _engine.ReportStopReason(L10n.T("显示器配置发生变化，正在停止并保存当前录制，防止捕获范围错位。"));_=StopRecordingAsync();return;
                }
                _displayPending=false;

            }
            if(_engine.State==RecorderState.Idle)
            {
                bool displays=_displayPending;_devicesPending=_displayPending=false;
                if(displays)
                {
                    RefreshMonitors();SetMinimumViewport();FitToCurrentScreen();_preview.ChangePicture(null);
                    if(_region.Width>0&&ValidateRegion(new[]{_region.X,_region.Y,_region.Width,_region.Height},SystemInformation.VirtualScreen)is not null)
                    {
                        _region=RegionSelection.FromRectangle(_region)?.Restore(SystemInformation.VirtualScreen)??Rectangle.Empty;
                        ShowNotice(_region.IsEmpty?L10n.T("显示器范围已变化，请重新选择录制区域。"):L10n.T("显示器范围已变化，已将选区调整到当前桌面内并保留虚线边框。"));
                    }
                    ShowNotice(L10n.T("已刷新显示器配置。"));
                }
                RefreshAudioDevices();PlanChanged();
            }
        }
        if(!_busy&&(_engine.State is RecorderState.Recording or RecorderState.Paused)&&_engine.CurrentPlan is {WindowHandle:not 0} windowPlan)
        {
            try
            {
                var current=WindowsInterop.GetWindowClientBounds(new IntPtr(windowPlan.WindowHandle));
                if(current.Size!=windowPlan.Bounds.Size)throw new InvalidOperationException(L10n.T("窗口尺寸发生变化。"));
            }
            catch(Exception e){_engine.ReportStopReason(e.Message+L10n.T(" 正在停止并保存已有内容。"));_=StopRecordingAsync();return;}
        }
        SyncRecordingUi();
        if(DateTime.UtcNow>=_nextSpaceCheck)RefreshStorage();
        if (_engine.State is RecorderState.Recording or RecorderState.Paused or RecorderState.Finalizing)
        {
            var duration = _engine.Duration;
            RefreshRecordingCounters();
            var performance = _engine.Performance;
            _hints.SetToolTip(_frameLabel,performance.Text+L10n.T("；FFmpeg 进度统计，不包含所有采集丢帧。"));
            if(_engine.State==RecorderState.Recording)
            {
                _status.Text = performance.Slow && _engine.SegmentDuration.TotalSeconds >= 10 ? L10n.T("● 编码偏慢") : L10n.T("● 正在录制");
                _hints.SetToolTip(_status,performance.Text);
                _details.Text = performance.Text;
                if(performance.Slow && _engine.SegmentDuration.TotalSeconds>=10 && DateTime.UtcNow>=_nextPerformanceWarning)
                {
                    _nextPerformanceWarning=DateTime.UtcNow.AddSeconds(30);
                    ShowNotice(L10n.T("编码速度低于实时：下次可选择 720p / 50% 缩放、15 FPS，或可用的硬件编码。")+performance.Text);
                }
            }
            if (_engine.State==RecorderState.Paused) _status.Text=L10n.T("Ⅱ  已暂停");
            if (_engine.State == RecorderState.Recording && !_busy && _sessionLimit.Reached(duration))
            {
                _engine.ReportStopReason(L10n.T("已达到定时录制时长，正在自动停止并保存。"));
                _ = StopRecordingAsync();
                return;
            }
            if (_engine.UnexpectedExit && !_busy)
            {
                _engine.ReportStopReason(L10n.T("发现 FFmpeg 意外退出，正在尝试保留已录内容。")+_engine.ExitDetails);
                _=StopRecordingAsync(); return;
            }
            if (_engine.State==RecorderState.Recording && !_busy && _engine.CurrentPlan is { } activePlan
                && _adaptivePerformance.NextFps(activePlan, performance, duration, _engine.SegmentDuration) is { } nextFps)
            { _=AdjustPerformanceAsync(nextFps); return; }
            if(_engine.State==RecorderState.Recording && !_busy && _engine.SegmentDue) _=RotateSegmentAsync();
        }
    }
    private async Task AdjustPerformanceAsync(int fps)
    {
        if (_busy || _engine.State!=RecorderState.Recording) return;
        _busy=true; UpdateButtons();
        try { await _engine.ChangeFpsAsync(fps); }
        catch (Exception e) { ShowNotice(L10n.T("性能调整失败，已保留素材：")+e.Message); }
        finally { _busy=false; PlanChanged(); UpdateButtons(); }
    }
    private async Task RotateSegmentAsync()
    {
        if(_busy || _engine.State!=RecorderState.Recording)return;
        _busy=true; UpdateButtons();
        try { await _engine.RotateSegmentAsync(); }
        catch(Exception e) { ShowNotice(L10n.T("自动分段失败，已有素材保留：")+e.Message); }
        finally { _busy=false; PlanChanged(); UpdateButtons(); }
    }
    private void SyncRecordingUi()
    {
        if (IsDisposed || _regionIndicator.IsDisposed || _recordingHud.IsDisposed) return;
        bool regionVisible = _hasShown && !_suspendOverlays && _settings.Scope == RecordingScope.Region && _region.Width >= 64 && _region.Height >= 64;
        if (regionVisible)
        {
            _regionIndicator.SetEditable(!_busy&&_dialogDepth==0&&!_startingSession&&_engine.State==RecorderState.Idle);
            _regionIndicator.SetSelection(_region, _engine.State);
            _regionIndicator.EnsureVisibleOnTop();
        }
        else {_regionIndicator.SetEditable(false);_regionIndicator.Hide();}
        bool capturing = _startingSession || _countdownCancellation is not null || _engine.State != RecorderState.Idle;
        if (capturing && !_suspendOverlays)
        {
            TimeSpan elapsed = _countdownCancellation is not null || _engine.State == RecorderState.Idle ? TimeSpan.Zero : _engine.Duration;
            _recordingHud.SetCaptureExclusion(_settings.ExcludeHudFromCapture);
            var performance=_engine.Performance;
            _recordingHud.UpdateState(_engine.State, _busy, _countdownSeconds, elapsed, _sessionStartedAt, _startingSession,_saveCancellation is {IsCancellationRequested:false},_saveProgress,
                _sessionLimit,performance,performance.Slow&&_engine.SegmentDuration.TotalSeconds>=10);
            _recordingHud.EnsureVisibleOnTop();
            _recordingHud.EnsureOnScreen();
        }
        else _recordingHud.Hide();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesReleased)
        {
            _resourcesReleased=true;Ui.ThemeChanged-=OnThemeChanged;
            _ticker.Stop();_ticker.Dispose();_audioTicker.Stop();_audioTicker.Dispose();
            _audioPreview?.Dispose();_audioPreview=null;
            _countdownCancellation?.Cancel();_saveCancellation?.Cancel();
            _engine.Dispose();
            _tray.Visible=false;_tray.Dispose();
            _recordingHud.Dispose();_regionIndicator.Dispose();_hints.Dispose();
            _brandImage?.Dispose();_brandImage=null;_ownedIcon?.Dispose();_ownedIcon=null;
        }
        base.Dispose(disposing);
    }
    private void ShowRecordingControls()
    {
        if (!_startingSession && _countdownCancellation is null && _engine.State == RecorderState.Idle) return;
        _excludeHud.Checked = false; // A recovery request must also work through remote/shared displays.
        Rectangle capture;
        try { capture = GetPlan().Bounds; } catch { capture = Screen.FromHandle(Handle).Bounds; }
        if (!_recordingHud.Visible) PlaceRecordingHud(capture,_settings.Scope==RecordingScope.Region);
        SyncRecordingUi();
        _recordingHud.PlaceNear(capture, _settings.Scope == RecordingScope.Region);
    }
    private void ConfigureTray()
    {
        _tray.Icon=Icon;
        _tray.Text=L10n.T("ScreenLossless · 准备就绪");
        _tray.Visible=true;
        _tray.DoubleClick+=(_,_)=>RestoreWindow();
        var menu=new ContextMenuStrip();
        menu.Items.Add(L10n.T("显示主界面"),null,(_,_)=>RestoreWindow());
        _floatingMenu=menu.Items.Add(L10n.T("显示录制浮窗"),null,(_,_)=>ShowRecordingControls()); _floatingMenu.Enabled=false;
        menu.Items.Add(L10n.T("开始 / 结束 / 取消倒计时"),null,async(_,_)=>await ToggleRecordingAsync());
        menu.Items.Add(L10n.T("暂停 / 继续"),null,async(_,_)=>await TogglePauseAsync());
        menu.Items.Add(L10n.T("保存截图"),null,async(_,_)=>await SaveScreenshotAsync());
        menu.Items.Add(L10n.T("打开保存目录"),null,(_,_)=>OpenFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L10n.T("退出"),null,(_,_)=>Close());
        Ui.StyleMenu(menu);_tray.ContextMenuStrip=menu;
    }
    internal void RequestActivation() => RunUi(() =>
    {
        if (_dialogDepth > 0 || OwnedForms.Any(form => form.Visible && form.Modal))
        {
            var dialog = OwnedForms.FirstOrDefault(form => form.Visible && form.Modal);
            dialog?.Activate();
            return;
        }
        RestoreWindow();
    });
    private void RestoreWindow()
    {
        Show();RestoreToNormal();Activate();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetMinimumViewport();
        var unavailable=RegisterBindings(_settings.Hotkeys);
        if(unavailable.Count>0)ShowNotice(_hotkeyStatus.Text+L10n.T("；可在辅助设置中更改。"));
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        WindowsInterop.UnregisterHotKey(Handle,HotkeyRecord);
        WindowsInterop.UnregisterHotKey(Handle,HotkeyPause);
        WindowsInterop.UnregisterHotKey(Handle,HotkeyScreenshot);
        base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x7E)QueueDeviceRefresh(true);
        else if(m.Msg==0x219)QueueDeviceRefresh(false);
        if (m.Msg==WindowsInterop.WmHotkey)
        {
            switch(m.WParam.ToInt32())
            {
                case HotkeyRecord:
                    _=ToggleRecordingAsync();
                    return;
                case HotkeyPause: _=TogglePauseAsync();return;
                case HotkeyScreenshot: _=SaveScreenshotAsync();return;
            }
        }
        base.WndProc(ref m);
    }
    private void OnClosing(object? sender,FormClosingEventArgs e)
    {
        if (_countdownCancellation is not null)
        {
            e.Cancel = true; _closeAfterCountdown = true; _countdownCancellation.Cancel(); return;
        }
        if (_busy) { e.Cancel = true; return; }

        if (_reallyClosing)
        {
            _settings.Save();return;
        }
        if (_engine.State!=RecorderState.Idle)
        {
            e.Cancel=true;
            if (_busy) { MessageBox.Show(this,L10n.T("正在处理录制文件，请稍候再退出。"),L10n.T("提示")); return; }
            if (MessageBox.Show(this,L10n.T("正在录制。停止录制并保存文件后退出吗？"),L10n.T("退出确认"),
                    MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
            _=StopThenCloseAsync();return;
        }
        _settings.Save();
    }
    private async Task StopThenCloseAsync()
    {
        bool saved = await StopRecordingAsync();
        if (saved && _engine.State==RecorderState.Idle) { _reallyClosing=true;Close(); }
    }
    private void RestoreRememberedWindow()
    {
        if(_settings.Scope!=RecordingScope.Window||_window is not null||_settings.LastWindow is null)return;
        _window=WindowsInterop.RestoreWindow(_settings.LastWindow);
        RefreshSelected();
    }
    private void PlaceRecordingHud(Rectangle capture,bool region)
    {
        if(_settings.HudPosition is {} saved){_recordingHud.RestorePosition(saved.Point);}
        else _recordingHud.PlaceNear(capture,region);
    }
    private void UpdateAudioLevels()
    {
        if(_resourcesReleased)return;
        bool visible=_hasShown&&Visible&&WindowState!=FormWindowState.Minimized&&_settingsPages.Count>1&&_settingsPages[1].Visible;
        bool updating=visible&&!_busy&&_dialogDepth==0;
        _audioTicker.Enabled=updating&&(_systemSound.Checked||_microphone.Checked)&&_engine.State is (RecorderState.Idle or RecorderState.Recording);
        bool preview=updating&&_engine.State==RecorderState.Idle;
        string key=preview?$"{_systemSound.Checked}:{_microphone.Checked}:{_settings.SystemDeviceId}:{_settings.MicrophoneDeviceId}":"";
        if (preview && _audioPreview?.NeedsReconnect == true && DateTime.UtcNow >= _nextPreviewRetry)
            _audioPreviewKey = "";
        if(key!=_audioPreviewKey)
        {
            _audioPreview?.Dispose();_audioPreview=null;_audioPreviewKey=key;
            _nextPreviewRetry = DateTime.UtcNow.AddSeconds(2);
            if(preview)_audioPreview=new(_systemSound.Checked,_microphone.Checked,_settings.SystemDeviceId,_settings.MicrophoneDeviceId,message =>
            {
                if(DateTime.UtcNow < _nextPreviewFailure) return;
                _nextPreviewFailure=DateTime.UtcNow.AddSeconds(30);ShowNotice(message);
            });
        }
        if(!visible)return;
        void ShowLevel(AudioLevelBar bar,bool enabled,float level,bool available,int volume)
        {
            string text=!enabled?L10n.T("未启用"):_engine.State==RecorderState.Paused?L10n.T("已暂停"):
                !available?(preview||_engine.State==RecorderState.Recording?L10n.T("设备不可用"):L10n.T("等待检测")):
                level>.0001f?L10n.F($"{20*Math.Log10(level):0.0} dB · 音量 {volume}%"):L10n.F($"静音 / 等待声音 · {volume}%");
            bar.UpdateLevel(level,text);
        }
        bool recording=_engine.State==RecorderState.Recording;
        ShowLevel(_systemLevelBar,_systemSound.Checked,recording?_engine.SystemLevel:(_audioPreview?.SystemLevel??0)*_systemVolume.Value/100f,
            recording?_engine.SystemAudioAvailable:_audioPreview?.SystemAvailable==true,_systemVolume.Value);
        ShowLevel(_micLevelBar,_microphone.Checked,recording?_engine.MicrophoneLevel:(_audioPreview?.MicrophoneLevel??0)*_micVolume.Value/100f,
            recording?_engine.MicrophoneAvailable:_audioPreview?.MicrophoneAvailable==true,_micVolume.Value);
    }
    private void RefreshStorage()
    {
        bool active=_engine.State is RecorderState.Recording or RecorderState.Paused;
        _nextSpaceCheck=DateTime.UtcNow.AddSeconds(active?5:15);
        try
        {
            long? available=_spaceReader(_folderBox.Text);
            if(available is not long free){_storage.Text=L10n.T("空间待检测");_storage.ForeColor=Ui.Subtle;return;}
            long sourceBytes=active?_workspaceSizer(_engine.WorkingDirectory):0;
            var decision=StorageGuard.Evaluate(free,sourceBytes,_engine.CurrentPlan?.Container==OutputContainer.Mp4?4:3);
            _storage.Text=L10n.F($"剩余 {free/1073741824d:0.0} GB");
            _storage.ForeColor=decision.Warn?Ui.Recording:Ui.Subtle;
            _hints.SetToolTip(_storage,active?L10n.F($"为合成预留约 {decision.Reserve/1073741824d:0.0} GB；空间不足将自动停录保存。"):L10n.T("录制中为素材合成预留空间并自动保护。"));
            if(active&&!_busy&&decision.Stop)
            {
                _engine.ReportStopReason(L10n.F($"磁盘空间保护：剩余 {free/1073741824d:0.00} GB，合成预留约 {decision.Reserve/1073741824d:0.00} GB，正在自动停止并保存。"));
                _=StopRecordingAsync();return;
            }
            if(active&&decision.Warn&&!_lowSpaceWarned)
            {
                _lowSpaceWarned=true;
                ShowNotice(L10n.T("磁盘空间偏低，已启用自动停止保护，请预留素材合成空间。"));
                _tray.ShowBalloonTip(3000,L10n.T("磁盘空间偏低"),L10n.T("达到合成预留空间时将自动停止并保存。"),ToolTipIcon.Warning);
            }
        }catch(Exception e){_storage.Text=L10n.T("空间待检测");_storage.ForeColor=Ui.Subtle;System.Diagnostics.Trace.WriteLine(e.Message);}
    }
    private void RememberSavedFile(string file)
    {
        _lastSavedFile=file;_lastFileButton.Enabled=File.Exists(file);_hints.SetToolTip(_lastFileButton,file);
    }
    private void OpenLastFile()
    {
        if(_lastSavedFile is null)return;
        if(!File.Exists(_lastSavedFile)){_lastFileButton.Enabled=false;ShowError(L10n.T("最近保存的文件已移动或删除，请打开保存文件夹查找。"));return;}
        try{Process.Start(new ProcessStartInfo(_lastSavedFile){UseShellExecute=true});}
        catch(Exception e){ShowError(L10n.T("无法打开最近文件：")+e.Message);}
    }
    private void RunUi(Action action)
    {
        if (_resourcesReleased || IsDisposed || Disposing || !IsHandleCreated) return;
        try { if (InvokeRequired) BeginInvoke(() => { if (!_resourcesReleased && !IsDisposed && !Disposing) action(); }); else action(); }
        catch (InvalidOperationException) { }
    }
    private void ShowNotice(string message)
    {
        if (_operationHint.IsDisposed) return;
        _operationHint.Text = message;
        _hints.SetToolTip(_operationHint, message);
    }
    private void ShowError(string message)
    {
        if(!Visible)Show();if(WindowState==FormWindowState.Minimized)RestoreToNormal();Activate();
        ShowNotice(L10n.T("错误：")+message);
        MessageBox.Show(this,message,"ScreenLossless",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }
}
