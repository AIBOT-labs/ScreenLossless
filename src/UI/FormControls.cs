using System.Collections.ObjectModel;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenLossless;

// Own the complete field surface and popup, so native ComboBox borders cannot leak through.
internal sealed class ChoiceBox : Control
{
    internal sealed class ItemCollection(ChoiceBox owner) : Collection<object>
    {
        public void AddRange(IEnumerable<object> values) { foreach (var value in values) Add(value); }
        protected override void InsertItem(int index,object item)
        {
            int selected=owner.SelectedIndex;base.InsertItem(index,item);owner.ItemsChanged(selected>=index?selected+1:selected);
        }
        protected override void SetItem(int index,object item)
        {base.SetItem(index,item);owner.ItemsChanged(owner.SelectedIndex,owner.SelectedIndex==index);}
        protected override void RemoveItem(int index)
        {
            int selected=owner.SelectedIndex;base.RemoveItem(index);owner.ItemsChanged(selected==index?-1:selected>index?selected-1:selected);
        }
        protected override void ClearItems(){base.ClearItems();owner.ItemsChanged(-1);}
    }
    private int _selectedIndex = -1, _updateDepth;
    private bool _hover;
    private ToolStripDropDown? _popup;
    private ChoiceList? _popupList;
    private readonly ToolTip _tooltip = new ThemeToolTip();
    private string _search = "";
    private DateTime _lastTyped;
    public ItemCollection Items { get; }
    public event EventHandler? SelectedIndexChanged;
    public int DropDownHeight { get; set; } = 280;
    internal bool CaptionStyle { get; set; }
    internal string Glyph { get; set; } = "";
    internal Rectangle DisplayTextBounds
    {
        get
        {
            int left = Ui.S(this, Glyph.Length == 0 ? 12 : 36);
            int right = Ui.S(this, CaptionStyle ? 26 : 34);
            return new Rectangle(left, 0, Math.Max(1, Width - left - right), Height);
        }
    }
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            _tooltip.SetToolTip(this, GetItemText(SelectedItem));
            Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
    public bool DroppedDown
    {
        get => _popup?.Visible == true;
        set { if (value) OpenPopup(); else ClosePopup(); }
    }
    public ChoiceBox()
    {
        Items = new ItemCollection(this);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Font = Ui.Font(); BackColor = Ui.Surface; ForeColor = Ui.Text;
        TabStop = true; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox;
        Size = new Size(220, 36);
    }
    public string GetItemText(object? item) => item?.ToString() ?? "";
    public void BeginUpdate() => _updateDepth++;
    public void EndUpdate() { _updateDepth = Math.Max(0, _updateDepth - 1); if (_updateDepth == 0) Invalidate(); }
    private void ItemsChanged(int nextIndex,bool selectedItemChanged=false)
    {
        ClosePopup();
        if(nextIndex!=_selectedIndex)SelectedIndex=nextIndex;
        else
        {
            _tooltip.SetToolTip(this,GetItemText(SelectedItem));
            if(selectedItemChanged)SelectedIndexChanged?.Invoke(this,EventArgs.Empty);
        }
        if(_updateDepth==0)Invalidate();
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) ClosePopup(); Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnVisibleChanged(EventArgs e) { if (!Visible) ClosePopup(); base.OnVisibleChanged(e); }
    protected override void OnSizeChanged(EventArgs e) { ClosePopup(); base.OnSizeChanged(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus(); DroppedDown = !DroppedDown;
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!Enabled) { base.OnKeyDown(e); return; }
        if (e.KeyCode is Keys.F4 or Keys.Space or Keys.Enter || (e.Alt && e.KeyCode == Keys.Down))
        { DroppedDown = !DroppedDown; e.Handled = e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Escape && DroppedDown) { ClosePopup(); e.Handled = e.SuppressKeyPress = true; }
        else if (Items.Count > 0 && e.KeyCode is Keys.Up or Keys.Down or Keys.Home or Keys.End)
        {
            SelectedIndex = e.KeyCode switch { Keys.Home => 0, Keys.End => Items.Count - 1,
                Keys.Up => Math.Max(0, _selectedIndex - 1), _ => Math.Min(Items.Count - 1, _selectedIndex + 1) };
            e.Handled = e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar))
        {
            if ((DateTime.UtcNow - _lastTyped).TotalSeconds > 1) _search = "";
            _lastTyped = DateTime.UtcNow; _search += e.KeyChar;
            int match = Enumerable.Range(0, Items.Count).FirstOrDefault(i => GetItemText(Items[i]).StartsWith(_search, StringComparison.CurrentCultureIgnoreCase), -1);
            if (match >= 0) SelectedIndex = match;
            e.Handled = true;
        }
        base.OnKeyPress(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Focused && Enabled && !DroppedDown && Items.Count > 0)
            SelectedIndex = Math.Clamp(_selectedIndex + (e.Delta > 0 ? -1 : 1), 0, Items.Count - 1);
        base.OnMouseWheel(e);
    }
    private void OpenPopup()
    {
        if (!Enabled || !Visible || Items.Count == 0 || DroppedDown) return;
        _popup?.Dispose();
        var list = new ChoiceList(this) { Font = Font, ItemHeight = Ui.S(this, 38), SelectedIndex = -1 };
        list.Items.AddRange(Items.ToArray()); list.SelectedIndex = _selectedIndex;
        int padding = Ui.S(this, 6);
        int height = Math.Min(Ui.S(this, DropDownHeight), list.ItemHeight * Items.Count + padding * 2);
        int textWidth = Items.Cast<object>().Max(item => TextRenderer.MeasureText(GetItemText(item), Font).Width) + Ui.S(this, 68);
        int popupWidth = Math.Min(Screen.FromControl(this).WorkingArea.Width - padding * 2, Math.Max(Width, textWidth));
        var host = new ToolStripControlHost(new ThemedScrollHost(list)) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty,
            Size = new Size(Math.Max(Ui.S(this, 160), popupWidth - padding * 2), Math.Max(list.ItemHeight, height - padding * 2)) };
        var popup = new ChoicePopup { AutoSize = true, Padding = new Padding(padding), BackColor = Ui.Surface, Font = Font,
            DropShadowEnabled = false, Renderer = new PopupRenderer() };
        popup.Items.Add(host); _popup = popup; _popupList = list;
        popup.Closed += (_, _) => { Invalidate(); };
        popup.Show(this, new Point(0, Height + Ui.S(this, 5)));
        list.Focus(); Invalidate();
    }
    private void ClosePopup() { if (_popup?.Visible == true) _popup.Close(); }
    private void Commit(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        SelectedIndex = index; ClosePopup(); Focus();
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.ParentColor(this));
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        var g = e.Graphics; g.Clear(Ui.ParentColor(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        bool active = Focused || DroppedDown;
        using var path = Ui.Round(new RectangleF(1, 1, Width - 2, Height - 2), Ui.S(this, CaptionStyle ? 8 : 9));
        Color surface = !Enabled ? CaptionStyle ? Ui.ParentColor(this) : Ui.Disabled
            : CaptionStyle ? DroppedDown ? Ui.AccentLight : _hover || Focused ? Ui.Surface : Ui.ParentColor(this)
            : _hover ? Ui.Hover : Ui.Surface;
        using var fill = new SolidBrush(surface); g.FillPath(fill, path);
        if (!CaptionStyle || active || (_hover && Enabled))
        {
            using var border = new Pen(active && Enabled ? Ui.Accent : _hover && Enabled ? Ui.BorderStrong : Ui.Border,
                Math.Max(1, (active && Enabled ? 1.3f : 1f) * DeviceDpi / 96f));
            g.DrawPath(border, path);
        }
        if (Glyph.Length > 0)
            Ui.Symbol(g, Glyph, new Rectangle(Ui.S(this, 11), (Height - Ui.S(this, 17)) / 2, Ui.S(this, 17), Ui.S(this, 17)),
                !Enabled ? Ui.DisabledText : active ? Ui.Accent : Ui.Subtle);
        string text = SelectedItem is null ? L10n.T("请选择") : GetItemText(SelectedItem);
        TextRenderer.DrawText(g, text, Font, DisplayTextBounds, Enabled ? ForeColor : Ui.DisabledText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        int x = Width - Ui.S(this, CaptionStyle ? 14 : 18), y = Height / 2, d = Ui.S(this, 4);
        using var arrow = new Pen(Enabled && active ? Ui.Accent : Ui.Subtle, Math.Max(1.5f, DeviceDpi / 64f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (DroppedDown) g.DrawLines(arrow, new[] { new Point(x - d, y + d / 2), new Point(x, y - d / 2), new Point(x + d, y + d / 2) });
        else g.DrawLines(arrow, new[] { new Point(x - d, y - d / 2), new Point(x, y + d / 2), new Point(x + d, y - d / 2) });
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ChoiceAccessibleObject(this);
    private sealed class ChoiceAccessibleObject(ChoiceBox owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.GetItemText(owner.SelectedItem); set { } }
        public override string DefaultAction => L10n.T("展开选项");
        public override void DoDefaultAction() => owner.DroppedDown = !owner.DroppedDown;
        public override AccessibleStates State => base.State | (owner.DroppedDown ? AccessibleStates.Expanded : AccessibleStates.Collapsed) | AccessibleStates.Focusable;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _popup?.Dispose(); _tooltip.Dispose(); }
        base.Dispose(disposing);
    }
    private sealed class ChoicePopup : ToolStripDropDown
    {
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0) return;
            using var path = Ui.Round(ClientRectangle, Ui.S(this, 10));
            var previous = Region; Region = new Region(path); previous?.Dispose();
        }
    }
    private sealed class PopupRenderer : ThemeMenuRenderer
    {
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Ui.Round(new RectangleF(.5f, .5f, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), Ui.S(e.ToolStrip, 10));
            using var pen = new Pen(Ui.BorderStrong); e.Graphics.DrawPath(pen, path);
        }
    }
    private sealed class ChoiceList : ListBox
    {
        private readonly ChoiceBox _owner;
        public ChoiceList(ChoiceBox owner)
        {
            _owner = owner; DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false;
            BorderStyle = BorderStyle.None; BackColor = Ui.Surface;
            MouseMove += (_, e) => { int index = IndexFromPoint(e.Location); if (index >= 0) SelectedIndex = index; };
            MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) _owner.Commit(IndexFromPoint(e.Location)); };
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { _owner.Commit(SelectedIndex); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode is Keys.Escape or Keys.F4) { _owner.ClosePopup(); _owner.Focus(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Tab) { _owner.ClosePopup(); _owner.Parent?.SelectNextControl(_owner, !e.Shift, true, true, true); e.Handled = e.SuppressKeyPress = true; }
            };
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using var background = new SolidBrush(Ui.Surface); e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index < 0) return;
            bool highlighted = (e.State & DrawItemState.Selected) != 0;
            bool current = e.Index == _owner.SelectedIndex;
            var rect = Rectangle.Inflate(e.Bounds, -Ui.S(_owner, 2), -Ui.S(_owner, 3));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (highlighted || current)
            {
                using var shape = Ui.Round(rect, Ui.S(_owner, 7)); using var fill = new SolidBrush(current ? Ui.AccentLight : Ui.Hover);
                e.Graphics.FillPath(fill, shape);
            }
            var text = new Rectangle(e.Bounds.X + Ui.S(_owner, 10), e.Bounds.Y, e.Bounds.Width - Ui.S(_owner, 42), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, _owner.GetItemText(Items[e.Index]), Font, text, current ? Ui.Accent : Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (e.Index == _owner.SelectedIndex)
            {
                int x = e.Bounds.Right - Ui.S(_owner, 20), y = e.Bounds.Top + e.Bounds.Height / 2;
                using var pen = new Pen(Ui.Accent, Ui.S(_owner, 2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                e.Graphics.DrawLines(pen, new[] { new Point(x - Ui.S(_owner, 4), y), new Point(x - Ui.S(_owner, 1), y + Ui.S(_owner, 3)), new Point(x + Ui.S(_owner, 5), y - Ui.S(_owner, 3)) });
            }
        }
    }
}

internal sealed class TextField : UserControl
{
    private readonly TextBox _editor;
    public TextField(TextBox editor)
    {
        _editor = editor; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Surface; Size = new Size(200, 36); TabStop = false;
        editor.BorderStyle = BorderStyle.None; editor.Dock = DockStyle.None; editor.Margin = Padding.Empty;
        editor.BackColor = Ui.Surface; editor.ForeColor = Ui.Text;
        Controls.Add(editor);
        editor.GotFocus += (_, _) => Invalidate(); editor.LostFocus += (_, _) => Invalidate();
        editor.EnabledChanged += (_, _) => Invalidate();
        editor.FontChanged += (_, _) => PerformLayout();
        Click += (_, _) => editor.Focus();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_editor is null) return;
        int inset = Ui.S(this, 12);
        _editor.Bounds = new Rectangle(inset, Math.Max(Ui.S(this, 4), (Height - _editor.PreferredHeight) / 2), Math.Max(1, Width - inset * 2), _editor.PreferredHeight);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.ParentColor(this));
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Round(new RectangleF(1, 1, Width - 2, Height - 2), Ui.S(this, 9));
        using var fill = new SolidBrush(Ui.Surface); using var border = new Pen(_editor.Focused ? Ui.Accent : Ui.Border, Math.Max(1, (_editor.Focused ? 1.3f : 1f) * DeviceDpi / 96f));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
}

internal sealed class ToggleCheck : CheckBox
{
    private bool _hover;
    public ToggleCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Font = Ui.Font(); Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.ParentColor(this));
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Ui.ParentColor(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        int side = Ui.S(this, 17), top = (Height - side) / 2;
        var rect = new Rectangle(Ui.S(this, 2), top, side, side);
        using var path = Ui.Round(rect, Ui.S(this, 4));
        using var fill = new SolidBrush(Checked && Enabled ? Ui.PrimaryFill : !Enabled ? Ui.Background : Ui.Surface);
        using var border = new Pen(Enabled && (Checked || _hover || Focused) ? Ui.Accent : Ui.Border, Ui.S(this, 1));
        g.FillPath(fill, path); g.DrawPath(border, path);
        if (Checked)
        {
            using var tick = new Pen(Enabled ? Color.White : Ui.Subtle, Math.Max(1.5f, Ui.S(this, 2))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(tick, new[] { new Point(rect.X + Ui.S(this, 4), top + Ui.S(this, 8)), new Point(rect.X + Ui.S(this, 7), top + Ui.S(this, 11)), new Point(rect.X + Ui.S(this, 13), top + Ui.S(this, 5)) });
        }
        var text = new Rectangle(Ui.S(this, 28), 0, Math.Max(1, Width - Ui.S(this, 31)), Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? Ui.Text : Ui.Subtle,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(text, 0, -Ui.S(this, 5)), Ui.Accent, Ui.ParentColor(this));
    }
}


internal sealed class VolumeSlider : Control
{
    private int _minimum, _maximum = 100, _value = 100;
    private bool _dragging;
    public int Minimum { get => _minimum; set { if (value > _maximum) throw new ArgumentOutOfRangeException(nameof(value)); _minimum = value; Value = _value; Invalidate(); } }
    public int Maximum { get => _maximum; set { if (value < _minimum) throw new ArgumentOutOfRangeException(nameof(value)); _maximum = value; Value = _value; Invalidate(); } }
    public int SmallChange { get; set; } = 5;
    public int LargeChange { get; set; } = 10;
    public int Value
    {
        get => _value;
        set { int next = Math.Clamp(value, _minimum, _maximum); if (next == _value) return; _value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? ValueChanged;
    public VolumeSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.Slider; TabStop = true; Cursor = Cursors.Hand;
        Size = new Size(200, 36); BackColor = Ui.Surface;
    }
    private void SetFromX(int x)
    {
        if (!Enabled) return;
        int inset = Ui.S(this, 12), width = Math.Max(1, Width - inset * 2);
        Value = _minimum + (int)Math.Round(Math.Clamp((x - inset) / (double)width, 0, 1) * (_maximum - _minimum));
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus(); _dragging = true; Capture = true; SetFromX(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging && e.Button == MouseButtons.Left) { SetFromX(e.X); _dragging = false; Capture = false; }
        base.OnMouseUp(e);
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) _dragging = false; base.OnMouseCaptureChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { _dragging = false; Capture = false; } Invalidate(); base.OnEnabledChanged(e); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
        {
            Value = e.KeyCode switch { Keys.Home => Minimum, Keys.End => Maximum, Keys.Left or Keys.Down => Value - SmallChange,
                Keys.PageDown => Value - LargeChange, Keys.PageUp => Value + LargeChange, _ => Value + SmallChange };
            e.Handled = e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e) { if (Enabled && Focused) Value += e.Delta > 0 ? SmallChange : -SmallChange; base.OnMouseWheel(e); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.ParentColor(this));
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Ui.ParentColor(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        int inset = Ui.S(this, 12), y = Height / 2;
        int x = inset + (int)Math.Round((Width - inset * 2) * (Value - Minimum) / (double)Math.Max(1, Maximum - Minimum));
        using var track = new Pen(Ui.Border, Ui.S(this, 4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var progress = new Pen(Enabled ? Ui.Accent : Ui.DisabledText, Ui.S(this, 4)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(track, inset, y, Math.Max(inset, Width - inset), y);
        if (x > inset) g.DrawLine(progress, inset, y, x, y);
        int radius = Ui.S(this, 7);
        if (Focused && ShowFocusCues) { using var ring = new SolidBrush(Ui.AccentLight); g.FillEllipse(ring, x - radius - Ui.S(this, 4), y - radius - Ui.S(this, 4), (radius + Ui.S(this, 4)) * 2, (radius + Ui.S(this, 4)) * 2); }
        using var fill = new SolidBrush(Enabled ? Ui.Accent : Ui.Subtle); g.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
        using var outline = new Pen(Ui.Surface, Ui.S(this, 2)); g.DrawEllipse(outline, x - radius, y - radius, radius * 2, radius * 2);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibleObject(this);
    private sealed class SliderAccessibleObject(VolumeSlider owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.Value.ToString(); set { if (int.TryParse(value, out int level) && owner.Enabled) owner.Value = level; } }
    }
}
