using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenLossless;

internal sealed class RegionPicker : Form
{
    private readonly Bitmap _snapshot;
    private readonly Rectangle _virtual;
    private Point _anchor;
    private Rectangle _selection;
    private bool _selecting;
    private readonly bool _editing;
    private Rectangle _original;
    private int _edge; // 1 left, 2 right, 4 top, 8 bottom; 16 move
    private string _message = L10n.T("松开鼠标确认  ·  Esc 或右键取消");
    public Rectangle SelectedBounds { get; private set; }

    public RegionPicker(Rectangle? initial = null)
    {
        _virtual = SystemInformation.VirtualScreen;
        _editing = initial.HasValue;
        if (initial is { } selected) _selection = new Rectangle(selected.X-_virtual.X,selected.Y-_virtual.Y,selected.Width,selected.Height);
        if (_editing) _message = L10n.T("拖动内部或边角微调  ·  Enter 确认  ·  Esc 取消");
        _snapshot = new Bitmap(_virtual.Width, _virtual.Height);
        using (var g = Graphics.FromImage(_snapshot))
            g.CopyFromScreen(_virtual.X, _virtual.Y, 0, 0, _virtual.Size, CopyPixelOperation.SourceCopy);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (_editing && e.KeyCode == Keys.Enter && !_selecting) ConfirmSelection();
            else if (_editing && !_selecting && e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
            {
                int step=e.Shift?10:1;
                _selection=Adjust(_selection,new Point(e.KeyCode==Keys.Left?-step:e.KeyCode==Keys.Right?step:0,e.KeyCode==Keys.Up?-step:e.KeyCode==Keys.Down?step:0),16,ClientSize);
                Invalidate();e.Handled=e.SuppressKeyPress=true;
            }
        };
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // The picker spans physical desktop pixels. Windows' suggested DPI rectangle
        // belongs to one monitor and would crop/offset the other monitors.
        e.Cancel = true; base.OnDpiChanged(e); Bounds = _virtual;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
        if (e.Button != MouseButtons.Left) return;
        _selecting = true;
        _message = _editing ? L10n.T("拖动内部或边角微调  ·  Enter 确认  ·  Esc 取消") : L10n.T("松开鼠标确认  ·  Esc 或右键取消");
        _anchor = e.Location;
        _original = _selection;
        _edge = _editing ? HitEdge(e.Location) : 0;
        if (_edge == 0) _selection = Rectangle.Empty;
        Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting)
        {
            if (_editing) Cursor=HitEdge(e.Location) switch {16=>Cursors.SizeAll,1 or 2=>Cursors.SizeWE,4 or 8=>Cursors.SizeNS,5 or 10=>Cursors.SizeNWSE,6 or 9=>Cursors.SizeNESW,_=>Cursors.Cross};
            return;
        }
        UpdateSelection(e.Location);
        Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_selecting || e.Button != MouseButtons.Left) return;
        UpdateSelection(e.Location);
        _selecting = false;
        Capture = false;
        if (_selection.Width < 64 || _selection.Height < 64)
        {
            _message = L10n.T("区域至少需要 64 × 64 像素，请重新拖动选择。");
            Invalidate();
            return;
        }
        if (_editing) { Invalidate(); return; }
        ConfirmSelection();
    }
    private void ConfirmSelection()
    {
        if (_selection.Width < 64 || _selection.Height < 64) return;
        SelectedBounds = new Rectangle(_selection.X + _virtual.X, _selection.Y + _virtual.Y, _selection.Width, _selection.Height);
        DialogResult = DialogResult.OK;
        Close();
    }
    private void UpdateSelection(Point location)
    {
        if (_editing && _edge != 0)
        {
            _selection=Adjust(_original,new Point(location.X-_anchor.X,location.Y-_anchor.Y),_edge,ClientSize);return;
        }
        int x=Math.Clamp(location.X,0,ClientSize.Width),y=Math.Clamp(location.Y,0,ClientSize.Height);
        _selection=Rectangle.FromLTRB(Math.Min(_anchor.X,x),Math.Min(_anchor.Y,y),Math.Max(_anchor.X,x),Math.Max(_anchor.Y,y));
    }
    private int HitEdge(Point p)
    {
        if (_selection.Width<64||_selection.Height<64||!Rectangle.Inflate(_selection,9,9).Contains(p)) return 0;
        int edge=0;
        if(Math.Abs(p.X-_selection.Left)<=9)edge|=1;else if(Math.Abs(p.X-_selection.Right)<=9)edge|=2;
        if(Math.Abs(p.Y-_selection.Top)<=9)edge|=4;else if(Math.Abs(p.Y-_selection.Bottom)<=9)edge|=8;
        return edge!=0?edge:_selection.Contains(p)?16:0;
    }
    internal static Rectangle Adjust(Rectangle original,Point delta,int edge,Size desktop)
    {
        if(edge==16)return new Rectangle(Math.Clamp(original.X+delta.X,0,Math.Max(0,desktop.Width-original.Width)),Math.Clamp(original.Y+delta.Y,0,Math.Max(0,desktop.Height-original.Height)),original.Width,original.Height);
        int left=original.Left,right=original.Right,top=original.Top,bottom=original.Bottom;
        if((edge&1)!=0)left=Math.Clamp(left+delta.X,0,right-64);
        if((edge&2)!=0)right=Math.Clamp(right+delta.X,left+64,desktop.Width);
        if((edge&4)!=0)top=Math.Clamp(top+delta.Y,0,bottom-64);
        if((edge&8)!=0)bottom=Math.Clamp(bottom+delta.Y,top+64,desktop.Height);
        return Rectangle.FromLTRB(left,top,right,bottom);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if(_selecting&&!Capture){_selecting=false;_selection=_editing?_original:Rectangle.Empty;_message=L10n.T("选择已中断，请重新拖动。");Invalidate();}
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.DrawImageUnscaled(_snapshot, 0, 0);
        using (var fade = new SolidBrush(Color.FromArgb(115, 17, 22, 45)))
            g.FillRectangle(fade, ClientRectangle);
        if (_selection.Width > 0 && _selection.Height > 0)
        {
            g.DrawImage(_snapshot, _selection, _selection, GraphicsUnit.Pixel);
            using var outline = new Pen(Color.FromArgb(24, 31, 46), 3f) { DashPattern = new[] { 10f / 3, 8f / 3 } };
            using var core = new Pen(Color.FromArgb(250, 252, 255), 1f) { DashPattern = new[] { 10f, 8f } };
            g.DrawRectangle(outline, _selection);
            g.DrawRectangle(core, _selection);
            foreach (Point p in new Point[] {new(_selection.Left,_selection.Top),new(_selection.Right,_selection.Top),new(_selection.Left,_selection.Bottom),new(_selection.Right,_selection.Bottom)})
            {
                g.FillEllipse(Brushes.White, p.X - 5, p.Y - 5, 10, 10);
                g.DrawEllipse(Pens.RoyalBlue, p.X - 5, p.Y - 5, 10, 10);
            }
            string size = $"{_selection.Width} × {_selection.Height}";
            using var font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
            var measure = g.MeasureString(size, font);
            int boxY = _selection.Top > 44 ? _selection.Top - 38 : _selection.Bottom + 9;
            using var back = new SolidBrush(Color.FromArgb(229, 29, 35, 69));
            g.FillRectangle(back, _selection.Left, boxY, measure.Width + 18, 31);
            g.DrawString(size, font, Brushes.White, _selection.Left + 9, boxY + 4);
        }
        int inset = Ui.S(this, 24);
        var instructions = new Rectangle(inset, inset, Math.Min(Width - inset * 2, Ui.S(this, 420)), Ui.S(this, 86));
        using var instructionShape = Ui.Round(instructions, Ui.S(this, 12));
        using var instructionBackground = new SolidBrush(Color.FromArgb(236, 28, 37, 55));
        g.FillPath(instructionBackground, instructionShape);
        using var titleFont = Ui.Font(13, true); using var detailFont = Ui.Font(9);
        TextRenderer.DrawText(g, _editing ? L10n.T("微调录制区域") : L10n.T("拖动鼠标选择录制区域"), titleFont,
            new Rectangle(inset + Ui.S(this, 18), inset + Ui.S(this, 12), instructions.Width - Ui.S(this, 36), Ui.S(this, 28)), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _message, detailFont,
            new Rectangle(inset + Ui.S(this, 18), inset + Ui.S(this, 45), instructions.Width - Ui.S(this, 36), Ui.S(this, 26)), Color.FromArgb(211, 219, 238),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _snapshot.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class WindowPicker : ThemedForm
{
    private readonly DetailListBox _list = new() { Dock = DockStyle.Fill };
    private readonly TextBox _search = new() { PlaceholderText = L10n.T("搜索窗口名称…") };
    private readonly SoftButton _ok = new() { Text = L10n.T("选择窗口"), Glyph = "check", Primary = true, Enabled = false };
    private readonly SoftButton _cancel = new() { Text = L10n.T("取消"), DialogResult = DialogResult.Cancel };
    private readonly Label _count = DialogLayout.Text("", 8.5f, false, Ui.Subtle);
    private readonly Label _empty = DialogLayout.Text(L10n.T("没有可选择的窗口\n打开需要录制的应用，再点击「刷新」。"), 10, false, Ui.Subtle);
    private readonly Label _selection = DialogLayout.Text(L10n.T("请选择一个窗口"), 9, false, Ui.Subtle);
    private readonly List<WindowInfo>? _providedWindows;
    private List<WindowInfo> _all = new();
    public WindowInfo? SelectedWindow => _list.SelectedItem as WindowInfo;

    public WindowPicker(List<WindowInfo>? windows = null)
    {
        _providedWindows = windows;
        SuspendLayout(); DialogLayout.Style(this);
        Text = L10n.T("选择录制窗口 · ScreenLossless");
        ClientSize = new Size(680, 560); MinimumSize = new Size(560, 470);
        var root = DialogLayout.Grid(new[] { 100f }, 64, 52, -100, 34, 40);
        root.Padding = new Padding(24); SetContent(root);
        var header = DialogLayout.Grid(new[] { 100f }, 34, -100);
        header.Controls.Add(DialogLayout.Text(L10n.T("选择录制窗口"), 18, true), 0, 0);
        header.Controls.Add(DialogLayout.Text(L10n.T("跟随所选窗口的内容区域；请保持窗口可见且尺寸不变。"), 9, false, Ui.Subtle), 0, 1);
        root.Controls.Add(header, 0, 0);
        var search = DialogLayout.Grid(new[] { 100f, -92f }, -100);
        search.Margin = new Padding(0, 8, 0, 8);
        search.Controls.Add(new TextField(_search) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) }, 0, 0);
        var refresh = new SoftButton { Text = L10n.T("刷新"), Glyph = "refresh" }; refresh.Click += (_, _) => RefreshList();
        DialogLayout.Button(search, refresh, 1); root.Controls.Add(search, 0, 1);
        var card = DialogLayout.Card(); card.Padding = new Padding(10); root.Controls.Add(card, 0, 2);
        var listContent = DialogLayout.Grid(new[] { 100f }, 28, -100);
        var listHeader = DialogLayout.Grid(new[] { 100f, -100f }, -100);
        listHeader.Controls.Add(DialogLayout.Text(L10n.T("可用窗口"), 9, true), 0, 0);
        _count.TextAlign = ContentAlignment.MiddleRight; listHeader.Controls.Add(_count, 1, 0);
        listContent.Controls.Add(listHeader, 0, 0);
        var listHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Ui.Surface };
        _empty.TextAlign = ContentAlignment.MiddleCenter; _empty.AutoEllipsis = false;
        listHost.Controls.Add(new ThemedScrollHost(_list)); listHost.Controls.Add(_empty); listContent.Controls.Add(listHost, 0, 1); card.Controls.Add(listContent);
        _list.TitleText = item => ((WindowInfo)item).Title;
        _list.DetailText = item => { var window = (WindowInfo)item; return L10n.F($"{window.Bounds.Width} × {window.Bounds.Height}  ·  屏幕位置 {window.Bounds.X}, {window.Bounds.Y}"); };
        root.Controls.Add(_selection, 0, 3);
        var actions = DialogLayout.Grid(new[] { 100f, -96f, -132f }, -100);
        actions.Controls.Add(DialogLayout.Text(L10n.T("双击窗口也可确认"), 8.5f, false, Ui.Subtle), 0, 0);
        DialogLayout.Button(actions, _cancel, 1); DialogLayout.Button(actions, _ok, 2); root.Controls.Add(actions, 0, 4);
        _ok.Click += (_, _) => ConfirmSelection();
        _list.DoubleClick += (_, _) => ConfirmSelection();
        _list.SelectedIndexChanged += (_, _) => UpdateSelection();
        _search.TextChanged += (_, _) => Refilter();
        Shown += (_, _) => { RefreshList(); _search.Focus(); };
        AcceptButton = _ok; CancelButton = _cancel;
        ResumeLayout(true);
    }
    private void ConfirmSelection()
    {
        if (SelectedWindow is null) return;
        DialogResult = DialogResult.OK; Close();
    }
    private void RefreshList() { _all = _providedWindows ?? WindowsInterop.GetVisibleWindows(); Refilter(); }
    private void Refilter()
    {
        var previous = SelectedWindow?.Handle;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var window in _all.Where(w => w.Title.Contains(_search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))) _list.Items.Add(window);
            if (_list.Items.Count > 0)
            {
                int selected = Enumerable.Range(0, _list.Items.Count).FirstOrDefault(i => ((WindowInfo)_list.Items[i]).Handle == previous, 0);
                _list.SelectedIndex = selected;
            }
        }
        finally { _list.EndUpdate(); }
        _empty.Text = _all.Count == 0 ? L10n.T("没有可选择的窗口\n打开需要录制的应用，再点击「刷新」。") : L10n.T("没有匹配的窗口\n尝试其他名称，或清空搜索。");
        _empty.Visible = _list.Items.Count == 0; _list.Visible = !_empty.Visible;
        _count.Text = L10n.F($"{_list.Items.Count} 个窗口"); UpdateSelection();
    }
    private void UpdateSelection()
    {
        _ok.Enabled = SelectedWindow is not null;
        _selection.Text = SelectedWindow is { } selected ? L10n.F($"已选择：{selected.Title}") : L10n.T("请选择一个窗口");
    }
}
