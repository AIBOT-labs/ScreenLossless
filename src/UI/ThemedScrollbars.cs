using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenLossless;

// Keep native selection, wheel, keyboard and accessibility behavior. Clip the system scrollbar
// gutter outside the viewport and expose only the narrow, theme-colored scrollbar.
internal sealed class ThemedScrollHost : Panel
{
    private readonly Control _content;
    private readonly Panel _viewport = new() { TabStop = false };
    private readonly ThemedScrollBar _vertical = new(true), _horizontal = new(false);
    private readonly ScrollObserver _observer;
    private bool _syncing, _queued;
    internal ThemedScrollBar VerticalBar => _vertical;
    internal ThemedScrollBar HorizontalBar => _horizontal;
    internal Rectangle ViewportBounds => _viewport.Bounds;

    public ThemedScrollHost(Control content)
    {
        _content = content; _observer = new ScrollObserver(this);
        Dock = DockStyle.Fill; Margin = Padding.Empty; TabStop = false;
        BackColor = _viewport.BackColor = content.BackColor;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        content.Dock = DockStyle.None; content.Margin = Padding.Empty;
        _viewport.Controls.Add(content); Controls.Add(_viewport); Controls.Add(_vertical); Controls.Add(_horizontal);
        _vertical.BackColor = _horizontal.BackColor = content.BackColor;
        _vertical.ValueChanged += (_, _) => ApplyScroll(true, _vertical.Value);
        _horizontal.ValueChanged += (_, _) => ApplyScroll(false, _horizontal.Value);
        content.HandleCreated += (_, _) => { _observer.AssignHandle(content.Handle); QueueSync(); };
        content.HandleDestroyed += (_, _) => _observer.ReleaseHandle();
        content.Layout += (_, _) => QueueSync();
        content.VisibleChanged += (_, _) => QueueSync();
        content.EnabledChanged += (_, _) => { _vertical.Enabled = _horizontal.Enabled = content.Enabled; };
        content.FontChanged += (_, _) => QueueSync();
        content.Invalidated += (_, _) => QueueSync();
        if (content is ScrollableControl scrollable)
        {
            scrollable.Scroll += (_, _) => QueueSync();
            // AutoScrollPosition can move children without raising the public Scroll event.
            foreach (Control child in content.Controls) child.LocationChanged += ChildMoved;
            content.ControlAdded += (_, e) => { if (e.Control is { } child) child.LocationChanged += ChildMoved; QueueSync(); };
            content.ControlRemoved += (_, e) => { if (e.Control is { } child) child.LocationChanged -= ChildMoved; QueueSync(); };
        }
        if (content.IsHandleCreated) _observer.AssignHandle(content.Handle);
    }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); RefreshScrollbars(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); RefreshScrollbars(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); QueueSync(); }
    protected override void OnHandleDestroyed(EventArgs e) { _queued = false; base.OnHandleDestroyed(e); }
    private void ChildMoved(object? sender, EventArgs e) => QueueSync();
    private void QueueSync()
    {
        if (_syncing || _queued || IsDisposed || !IsHandleCreated) return;
        _queued = true;
        BeginInvoke((Action)(() => { _queued = false; if (!IsDisposed) RefreshScrollbars(); }));
    }
    internal void RefreshScrollbars()
    {
        if (_syncing || _content is null || IsDisposed) return;
        _syncing = true;
        try
        {
            // Resolve both axes together: changing viewport width can change text wrapping.
            for (int pass = 0; pass < 4; pass++)
            {
                var v = ReadAxis(true); var h = ReadAxis(false);
                int strip = Ui.S(this, 12);
                bool showV = _content.Visible && v.Maximum > v.Minimum;
                bool showH = _content.Visible && h.Maximum > h.Minimum;
                int width = Math.Max(0, ClientSize.Width - (showV ? strip : 0));
                int height = Math.Max(0, ClientSize.Height - (showH ? strip : 0));
                var bounds = new Rectangle(0, 0, width, height);
                var size = new Size(width + v.NativeSize, height + h.NativeSize);
                bool stable = _viewport.Bounds == bounds && _content.Size == size;
                _viewport.Bounds = bounds; _content.Bounds = new Rectangle(Point.Empty, size);
                _vertical.Bounds = new Rectangle(width, 0, strip, height);
                _horizontal.Bounds = new Rectangle(0, height, width, strip);
                _vertical.Visible = showV; _horizontal.Visible = showH;
                if (stable) break;
            }
            var vertical = ReadAxis(true); var horizontal = ReadAxis(false);
            _vertical.SetMetrics(vertical.Minimum, vertical.Maximum, vertical.Page, vertical.Position);
            _horizontal.SetMetrics(horizontal.Minimum, horizontal.Maximum, horizontal.Page, horizontal.Position);
            _vertical.SmallChange = _content is ScrollableControl ? Ui.S(this, 20) : 1;
            _horizontal.SmallChange = Ui.S(this, 20);
        }
        finally { _syncing = false; }
    }
    private readonly record struct Axis(int Minimum, int Maximum, int Page, int Position, int NativeSize);
    private Axis ReadAxis(bool vertical)
    {
        if (!_content.IsHandleCreated) return default;
        var info = new ScrollNative.Info { Size = Marshal.SizeOf<ScrollNative.Info>(), Mask = 0x17 };
        if (!ScrollNative.GetScrollInfo(_content.Handle, vertical ? 1 : 0, ref info)) return default;
        var geometry = new ScrollNative.BarInfo { Size = Marshal.SizeOf<ScrollNative.BarInfo>() };
        bool present = ScrollNative.GetScrollBarInfo(_content.Handle, vertical ? -5 : -6, ref geometry)
            && (geometry.State & 0x8000) == 0;
        if (!present) return default;
        int nativeSize = vertical ? geometry.Rect.Right - geometry.Rect.Left : geometry.Rect.Bottom - geometry.Rect.Top;
        int maximum = Math.Max(info.Minimum, info.Maximum - Math.Max(1, (int)info.Page) + 1);
        return new(info.Minimum, maximum, Math.Max(1, (int)info.Page), info.Position, nativeSize);
    }
    private void ApplyScroll(bool vertical, int value)
    {
        if (_syncing || !_content.IsHandleCreated) return;
        if (_content is ListBox list && vertical) list.TopIndex = value;
        else if (_content is ScrollableControl panel)
        {
            Point position = panel.AutoScrollPosition;
            panel.AutoScrollPosition = new Point(vertical ? -position.X : value, vertical ? value : -position.Y);
        }
        else if (_content is TextBoxBase)
        {
            int previous = ReadAxis(vertical).Position;
            ScrollNative.SendMessage(_content.Handle, 0xB6, new IntPtr(vertical ? 0 : value - previous), new IntPtr(vertical ? value - previous : 0));
        }
        RefreshScrollbars();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _observer.ReleaseHandle();
        base.Dispose(disposing);
    }
    private sealed class ScrollObserver(ThemedScrollHost host) : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // List mutations, scrolling, keyboard selection, text/wrapping and sizing.
            if (m.Msg is 0x5 or 0xC or 0xF or 0x47 or 0x85 or 0x100 or 0x101 or 0x114 or 0x115 or 0x20A or 0x20E
                || m.Msg is >= 0x180 and <= 0x1B2 || m.Msg is >= 0xB0 and <= 0xD3) host.QueueSync();
        }
    }
}

internal sealed class ThemedScrollBar : Control
{
    private readonly bool _vertical;
    private int _minimum, _maximum, _page = 1, _value, _dragOffset, _wheelRemainder;
    private bool _hover, _dragging;
    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 400 };
    private Point _pagePoint;
    internal int SmallChange { get; set; } = 1;
    internal int Maximum => _maximum;
    internal int PageSize => _page;
    internal event EventHandler? ValueChanged;
    internal int Value
    {
        get => _value;
        set { int next = Math.Clamp(value, _minimum, _maximum); if (next == _value) return; _value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    internal ThemedScrollBar(bool vertical)
    {
        _vertical = vertical; TabStop = false; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = L10n.T(vertical ? "垂直滚动条" : "水平滚动条"); BackColor = Ui.Surface;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        _repeat.Tick += (_, _) => { _repeat.Interval = 60; PageToward(_pagePoint); };
    }
    internal void SetMetrics(int minimum, int maximum, int page, int position)
    {
        maximum=Math.Max(minimum,maximum);page=Math.Max(1,page);position=Math.Clamp(position,minimum,maximum);
        if(_minimum==minimum&&_maximum==maximum&&_page==page&&_value==position)return;
        _minimum=minimum;_maximum=maximum;_page=page;_value=position;Invalidate();
    }
    internal Rectangle ThumbBounds
    {
        get
        {
            int inset = Ui.S(this, 2), length = Math.Max(0, (_vertical ? Height : Width) - inset * 2);
            int size = Math.Min(length, Math.Max(Ui.S(this, 24), (int)Math.Round(length * (double)_page / ((long)_maximum - _minimum + _page))));
            int start = inset + (_maximum == _minimum ? 0 : (int)Math.Round((length - size) * (double)(_value - _minimum) / (_maximum - _minimum)));
            return _vertical ? new(0, start, Width, size) : new(start, 0, size, Height);
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); if (_maximum <= _minimum) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int thick = Ui.S(this, _hover || _dragging ? 8 : 6);
        var thumb = ThumbBounds;
        thumb = _vertical ? new Rectangle((Width - thick) / 2, thumb.Y, thick, thumb.Height)
            : new Rectangle(thumb.X, (Height - thick) / 2, thumb.Width, thick);
        using var path = Ui.Round(thumb, thick / 2f);
        using var brush = new SolidBrush(!Enabled ? Ui.Border : _hover || _dragging ? Ui.Accent : Ui.ScrollThumb);
        e.Graphics.FillPath(brush, path);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (!Enabled || e.Button != MouseButtons.Left || _maximum <= _minimum) return;
        Capture = true; _pagePoint = e.Location;
        if (ThumbBounds.Contains(e.Location))
        {
            _dragging = true; _dragOffset = (_vertical ? e.Y - ThumbBounds.Y : e.X - ThumbBounds.X);
        }
        else { PageToward(e.Location); _repeat.Interval = 400; _repeat.Start(); }
        Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); _pagePoint = e.Location; if (!_dragging) return;
        int inset = Ui.S(this, 2), travel = (_vertical ? Height - ThumbBounds.Height : Width - ThumbBounds.Width) - inset * 2;
        if (travel > 0) Value = _minimum + (int)Math.Round(Math.Clamp((_vertical ? e.Y : e.X) - _dragOffset - inset, 0, travel) * (double)(_maximum - _minimum) / travel);
    }
    private void PageToward(Point point)
    {
        int position = _vertical ? point.Y : point.X, start = _vertical ? ThumbBounds.Top : ThumbBounds.Left, end = _vertical ? ThumbBounds.Bottom : ThumbBounds.Right;
        if (position < start) Value -= _page; else if (position >= end) Value += _page;
    }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; _repeat.Stop(); Capture = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { _dragging = false; _repeat.Stop(); Invalidate(); } base.OnMouseCaptureChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { _repeat.Stop(); _dragging = false; Capture = false; } Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnVisibleChanged(EventArgs e) { if (!Visible) { _repeat.Stop(); _dragging = false; Capture = false; } base.OnVisibleChanged(e); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Enabled)
        {
            _wheelRemainder += e.Delta; int steps = _wheelRemainder / 120; _wheelRemainder %= 120;
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines != 0) Value -= steps * (lines < 0 ? _page : Math.Max(1, lines) * SmallChange);
        }
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled)
        {
            int? next = e.KeyCode switch { Keys.Home => _minimum, Keys.End => _maximum, Keys.PageUp => _value - _page,
                Keys.PageDown => _value + _page, Keys.Up or Keys.Left => _value - SmallChange, Keys.Down or Keys.Right => _value + SmallChange, _ => null };
            if (next.HasValue) { Value = next.Value; e.Handled = e.SuppressKeyPress = true; }
        }
        base.OnKeyDown(e);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ScrollAccessibleObject(this);
    private sealed class ScrollAccessibleObject(ThemedScrollBar owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.Value.ToString(); set { if (owner.Enabled && int.TryParse(value, out int position)) owner.Value = position; } }
    }
    protected override void Dispose(bool disposing) { if (disposing) _repeat.Dispose(); base.Dispose(disposing); }
}

internal static class ScrollNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Info
    { internal int Size, Mask, Minimum, Maximum; internal uint Page; internal int Position, TrackPosition; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct BarInfo
    { internal int Size; internal Rect Rect; internal int LineButton, ThumbTop, ThumbBottom, Reserved; internal uint State, State1, State2, State3, State4, State5; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetScrollInfo(IntPtr handle, int bar, ref Info info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetScrollBarInfo(IntPtr handle, int obj, ref BarInfo info);
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
}
