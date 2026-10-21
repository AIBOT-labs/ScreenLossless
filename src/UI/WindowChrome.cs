using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace ScreenLossless;

// Standard Win32 sizing and snapping with an entirely client-drawn title bar.
public class ThemedForm : Form
{
    private CaptionButton? _maximize;
    private Rectangle _normalBounds;

    private Label? _caption;
    internal const int ChromeHeight = 40;
    internal Rectangle NormalWindowBounds => WindowState==FormWindowState.Normal?Bounds:_normalBounds.Width>0?_normalBounds:RestoreBounds;
    public ThemedForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Ui.Background;
        DoubleBuffered = true;
    }
    protected override Size SizeFromClientSize(Size clientSize) => clientSize;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.Style |= 0x40000 | 0x20000 | 0x10000; return cp; }
    }
    protected void SetContent(Control content, bool caption = true, Control? captionAccessory = null, int captionAccessoryWidth = 184)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty, ColumnCount = 1, RowCount = 2, BackColor = Ui.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, ChromeHeight));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bar = new CaptionStrip { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0,0,8,1), ColumnCount = captionAccessory is null ? 4 : 5, RowCount = 1, BackColor = Ui.Background };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if(captionAccessory is not null)bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,captionAccessoryWidth));
        for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        _caption = new Label { Text = caption ? Text : "", Dock = DockStyle.Fill, Margin = new Padding(20, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Subtle, Font = Ui.Font(8.5f), AutoEllipsis = true };
        bar.Controls.Add(_caption, 0, 0);
        var minimize = new CaptionButton("minimize", L10n.T("最小化"));
        _maximize = new CaptionButton("maximize", L10n.T("最大化 / 还原"));
        var close = new CaptionButton("close", L10n.T("关闭"));
        minimize.Click += (_, _) => { RememberNormalBounds(); WindowState = FormWindowState.Minimized; };
        _maximize.Click += (_, _) => ToggleMaximize();
        close.Click += (_, _) => Close();
        int firstButton=captionAccessory is null?1:2;
        if(captionAccessory is not null)bar.Controls.Add(captionAccessory,1,0);
        bar.Controls.Add(minimize, firstButton, 0); bar.Controls.Add(_maximize, firstButton+1, 0); bar.Controls.Add(close, firstButton+2, 0);
        EnableWindowDrag(bar); EnableWindowDrag(_caption);
        root.Controls.Add(bar, 0, 0); root.Controls.Add(content, 0, 1); Controls.Add(root);
        ClientSize = new Size(ClientSize.Width, ClientSize.Height + ChromeHeight);
        MinimumSize = new Size(MinimumSize.Width, MinimumSize.Height + ChromeHeight);
    }
    protected void EnableWindowDrag(Control control)
    {
        control.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) WindowNative.Drag(Handle); };
        control.DoubleClick += (_, _) => ToggleMaximize();
    }
    private void RememberNormalBounds() { if (WindowState == FormWindowState.Normal) _normalBounds = Bounds; }
    internal void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized) RestoreToNormal();
        else { RememberNormalBounds(); WindowState = FormWindowState.Maximized; }
    }
    protected void RestoreToNormal()
    {
        bool restore = WindowState != FormWindowState.Normal;
        var bounds = _normalBounds;
        // WinForms still accounts for WS_THICKFRAME when restoring its client size.
        WindowState = FormWindowState.Normal;
        if (restore && bounds.Width > 0) Bounds = MainForm.FitBounds(bounds, Screen.FromHandle(Handle).WorkingArea);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_maximize is not null) { _maximize.Kind = WindowState == FormWindowState.Maximized ? "restore" : "maximize"; _maximize.Invalidate(); }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x112)
        {
            int command = m.WParam.ToInt32() & 0xFFF0;
            if (command is 0xF030 or 0xF020) RememberNormalBounds();
            if (command == 0xF120)
            {
                var bounds = _normalBounds;
                base.WndProc(ref m);
                if (WindowState == FormWindowState.Normal && bounds.Width > 0) Bounds = MainForm.FitBounds(bounds, Screen.FromHandle(Handle).WorkingArea);
                return;
            }
        }
        // Suppress both initial and subsequent non-client layout and painting.
        // DefWindowProc draws a classic caption on deactivation even without WS_CAPTION.
        if (m.Msg is 0x83 or 0x85) { m.Result = IntPtr.Zero; return; } // WM_NCCALCSIZE / WM_NCPAINT
        if (m.Msg == 0x86) { m.Result = new IntPtr(1); return; } // WM_NCACTIVATE
        if (m.Msg == 0x24 && m.LParam != IntPtr.Zero)
        {
            base.WndProc(ref m);
            var info = Marshal.PtrToStructure<WindowNative.MinMaxInfo>(m.LParam);
            var screen = Screen.FromHandle(Handle);
            info.MaxPosition = new Point(screen.WorkingArea.Left - screen.Bounds.Left, screen.WorkingArea.Top - screen.Bounds.Top);
            info.MaxSize = screen.WorkingArea.Size;
            info.MinTrackSize = MinimumSize;
            Marshal.StructureToPtr(info, m.LParam, false); return;
        }
        if (m.Msg == 0x84 && WindowState == FormWindowState.Normal)
        {
            long value = m.LParam.ToInt64();
            var local = PointToClient(new Point(unchecked((short)value), unchecked((short)(value >> 16))));
            int hit = ResizeHitTest(ClientSize, local, Ui.S(this, 6));
            if (hit != 1) { m.Result = new IntPtr(hit); return; }
        }
        base.WndProc(ref m);
    }
    internal static int ResizeHitTest(Size size, Point p, int edge)
    {
        bool left = p.X < edge, right = p.X >= size.Width - edge, top = p.Y < edge, bottom = p.Y >= size.Height - edge;
        return top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 1;
    }
}

internal sealed class CaptionStrip : TableLayoutPanel
{
    internal CaptionStrip() => DoubleBuffered = true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var edge = new Pen(Ui.Border);
        e.Graphics.DrawLine(edge, 0, Height - 1, Width, Height - 1);
    }
}

internal sealed class CaptionButton : Button
{
    internal string Kind;
    internal Color BaseColor { get; set; } = Ui.Background;
    private bool _hover, _pressed;
    private readonly ToolTip _tip = new ThemeToolTip();
    public CaptionButton(string kind, string name)
    {
        Kind = kind; AccessibleName = name; _tip.SetToolTip(this, name);
        Dock = DockStyle.Fill; Margin = Padding.Empty; TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { _pressed = false; Invalidate(); } base.OnMouseCaptureChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BaseColor);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        bool highlighted = _hover || Focused;
        Color fill = highlighted ? Kind == "close" ? Ui.DangerLight : Ui.AccentLight : BaseColor;
        using (var shape = Ui.Round(Rectangle.Inflate(ClientRectangle, -Ui.S(this, 4), -Ui.S(this, 4)), Ui.S(this, 8)))
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, shape);
        using var pen = new Pen(highlighted ? Kind == "close" ? Ui.Danger : Ui.Accent : Ui.Subtle, Ui.S(this, 1.4f));
        float s = DeviceDpi / 96f, x = Width / 2f, y = Height / 2f + (_pressed ? s : 0);
        if (Kind == "minimize") g.DrawLine(pen, x - 5*s, y + 3*s, x + 5*s, y + 3*s);
        else if (Kind == "close") { g.DrawLine(pen, x-5*s, y-5*s, x+5*s, y+5*s); g.DrawLine(pen, x+5*s, y-5*s, x-5*s, y+5*s); }
        else if (Kind == "restore") { g.DrawRectangle(pen, x-3*s,y-5*s,8*s,8*s); using var background = new SolidBrush(fill); g.FillRectangle(background,x-5*s,y-3*s,8*s,8*s); g.DrawRectangle(pen,x-5*s,y-3*s,8*s,8*s); }
        else g.DrawRectangle(pen,x-5*s,y-5*s,10*s,10*s);
    }
    protected override void Dispose(bool disposing) { if (disposing) _tip.Dispose(); base.Dispose(disposing); }
}

internal static class WindowNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct MinMaxInfo
    {
        public Point Reserved; public Size MaxSize; public Point MaxPosition; public Size MinTrackSize; public Size MaxTrackSize;
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    internal static bool RaiseWithoutActivation(IntPtr handle) => SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002 | 0x0040);
    internal static void Drag(IntPtr handle) { ReleaseCapture(); SendMessage(handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr handle, out uint affinity);
    internal static bool SetCaptureExclusion(IntPtr handle, bool exclude) => (!exclude || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) && SetWindowDisplayAffinity(handle, exclude ? 0x11u : 0u);
    internal static bool IsCaptureExcluded(IntPtr handle) => GetWindowDisplayAffinity(handle, out uint affinity) && affinity == 0x11;
}
