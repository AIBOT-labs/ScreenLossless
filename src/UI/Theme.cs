using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenLossless;

internal static partial class Ui
{
    public static Color Background => _colors.Background;
    public static Color Surface => _colors.Surface;
    public static Color Text => _colors.Text;
    public static Color Subtle => _colors.Subtle;
    public static Color Accent => _colors.Accent;
    public static Color ScrollThumb => _colors.ScrollThumb;
    public static Color AccentLight => _colors.AccentLight;
    public static Color Border => _colors.Border;
    public static Color BorderStrong => _colors.BorderStrong;
    public static Color Hover => _colors.Hover;
    public static Color Disabled => _colors.Disabled;
    public static Color DisabledText => _colors.DisabledText;
    public static Color Danger => _colors.Danger;
    public static Color DangerLight => _colors.DangerLight;
    public static Color DangerBorder => _colors.DangerBorder;
    public static Color Mint => _colors.Mint;
    public static Color PrimaryFill => _colors.PrimaryFill;
    public static Color PrimaryHover => _colors.PrimaryHover;
    public static Color PrimaryPressed => _colors.PrimaryPressed;
    public static Color Pressed => _colors.Pressed;
    public static Color DangerHover => _colors.DangerHover;
    public static Color DangerPressed => _colors.DangerPressed;
    public static Color SelectedBadge => _colors.SelectedBadge;
    public static Color PreviewBackground => _colors.PreviewBackground;
    public static Color PreviewGrid => _colors.PreviewGrid;
    public static Color Warning => _colors.Warning;
    public static Color Recording => _colors.Recording;
    public static int S(Control c, float value) => (int)Math.Round(value * c.DeviceDpi / 96f);
    public static Font Font(float size = 9.5f, bool bold = false) => new(L10n.IsEnglish?"Segoe UI":"Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
    public static Color ParentColor(Control c)
    {
        for (Control? parent = c.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is CardPanel card) return card.CardColor;
            if (parent.BackColor.A == 255) return parent.BackColor;
        }
        return Background;
    }
    public static GraphicsPath Round(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0) return path;
        float diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (diameter <= 0) { path.AddRectangle(rect); return path; }
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
    public static void Symbol(Graphics g, string kind, Rectangle bounds, Color color)
    {
        var state = g.Save();
        g.TranslateTransform(bounds.X, bounds.Y);
        g.ScaleTransform(bounds.Width / 32f, bounds.Height / 32f);
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (kind)
        {
            case "sun":
                g.DrawEllipse(pen,10,10,12,12);
                for(int i=0;i<8;i++){double angle=i*Math.PI/4;g.DrawLine(pen,16+(float)Math.Cos(angle)*10,16+(float)Math.Sin(angle)*10,16+(float)Math.Cos(angle)*13,16+(float)Math.Sin(angle)*13);}break;
            case "moon":
                using(var moon=new GraphicsPath())
                {
                    moon.AddBezier(27,17,26,24,19,28,12,25);moon.AddBezier(12,25,5,22,3,14,7,8);
                    moon.AddBezier(7,8,9,5,12,4,15,4);moon.AddBezier(15,4,12,7,11,11,14,15);
                    moon.AddBezier(14,15,17,19,23,20,27,17);moon.CloseFigure();g.DrawPath(pen,moon);
                }break;
            case "language":
                g.DrawEllipse(pen, 4, 4, 24, 24); g.DrawEllipse(pen, 10, 4, 12, 24);
                g.DrawLine(pen, 4, 16, 28, 16); break;
            case "play":
                g.DrawPolygon(pen, new[] { new PointF(10, 6), new PointF(26, 16), new PointF(10, 26) }); break;
            case "pause":
                g.DrawLine(pen, 11, 7, 11, 25); g.DrawLine(pen, 21, 7, 21, 25); break;
            case "stop":
                using (var p = Round(new RectangleF(7, 7, 18, 18), 2)) g.DrawPath(pen, p); break;
            case "close":
                g.DrawLine(pen, 8, 8, 24, 24); g.DrawLine(pen, 24, 8, 8, 24); break;
            case "refresh":
                g.DrawArc(pen, 6, 6, 20, 20, 45, 285);
                g.DrawLines(pen, new[] { new PointF(26, 5), new PointF(26, 12), new PointF(19, 12) }); break;
            case "folder":
                g.DrawLines(pen, new[] { new PointF(3, 25), new PointF(3, 8), new PointF(13, 8), new PointF(16, 11), new PointF(29, 11), new PointF(29, 25), new PointF(3, 25) }); break;
            case "recover":
                g.DrawArc(pen, 7, 7, 18, 18, 215, 290);
                g.DrawLines(pen, new[] { new PointF(5, 5), new PointF(5, 13), new PointF(13, 13) }); break;
            case "settings":
                g.DrawLine(pen, 5, 9, 27, 9); g.DrawLine(pen, 5, 23, 27, 23);
                g.DrawEllipse(pen, 10, 5, 8, 8); g.DrawEllipse(pen, 18, 19, 8, 8); break;
            case "check":
                g.DrawLines(pen, new[] { new PointF(6, 17), new PointF(13, 24), new PointF(27, 8) }); break;
            case "region":
                g.DrawLines(pen, new PointF[] { new(4, 11), new(4, 4), new(11, 4) });
                g.DrawLines(pen, new PointF[] { new(21, 4), new(28, 4), new(28, 11) });
                g.DrawLines(pen, new PointF[] { new(4, 21), new(4, 28), new(11, 28) });
                g.DrawLines(pen, new PointF[] { new(21, 28), new(28, 28), new(28, 21) });
                break;
            case "window":
                using (var p = Round(new RectangleF(3, 5, 26, 22), 3)) g.DrawPath(pen, p);
                g.DrawLine(pen, 3, 11, 29, 11); g.DrawEllipse(pen, 6, 7, 1, 1); break;
            case "game":
                using (var p = Round(new RectangleF(2, 8, 28, 18), 6)) g.DrawPath(pen, p);
                g.DrawLine(pen, 8, 17, 14, 17); g.DrawLine(pen, 11, 14, 11, 20);
                g.DrawEllipse(pen, 21, 13, 2, 2); g.DrawEllipse(pen, 24, 18, 2, 2); break;
            default:
                using (var p = Round(new RectangleF(3, 4, 26, 19), 3)) g.DrawPath(pen, p);
                g.DrawLine(pen, 16, 23, 16, 28); g.DrawLine(pen, 10, 28, 22, 28); break;
        }
        g.Restore(state);
    }
}

internal sealed class CardPanel : Panel
{
    public int Radius { get; set; } = 14;
    public Color CardColor { get; set; } = Ui.Surface;
    public Color BorderColor { get; set; } = Ui.Border;
    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Background; Padding = new Padding(18);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.ParentColor(this));
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Ui.S(this, Radius));
        using var fill = new SolidBrush(CardColor); using var border = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
}

internal enum SoftButtonStyle { Standard, Quiet, Tab }

internal sealed class SoftButton : Button
{
    private bool _hover, _pressed, _active;
    public bool Active { get => _active; set { _active = value; Invalidate(); } }
    internal SoftButtonStyle Appearance { get; set; }
    public string Glyph { get; set; } = "";
    public bool Primary { get; set; }
    public bool Destructive { get; set; }
    public int Radius { get; set; } = 9;
    public SoftButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
        Font = Ui.Font(); ForeColor = Ui.Text; BackColor = Ui.Surface; Cursor = Cursors.Hand;
        MinimumSize = new Size(32, 30);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.ParentColor(this));
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        var g = e.Graphics;
        // Paint every pixel. Native themed Button backgrounds must never leak through rounded corners.
        g.Clear(Ui.ParentColor(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        bool tab = Appearance == SoftButtonStyle.Tab, quiet = Appearance == SoftButtonStyle.Quiet;
        Color fill = !Enabled ? (tab || quiet ? Ui.ParentColor(this) : Ui.Disabled)
            : Primary ? (_pressed ? Ui.PrimaryPressed : _hover ? Ui.PrimaryHover : Ui.PrimaryFill)
            : Destructive ? (_pressed ? Ui.DangerPressed : _hover ? Ui.DangerHover : Ui.DangerLight)
            : tab ? (Active ? Ui.Surface : _pressed || _hover ? Ui.Pressed : Ui.ParentColor(this))
            : quiet ? (_pressed ? Ui.AccentLight : _hover ? Ui.Surface : Ui.ParentColor(this))
            : Active ? Ui.AccentLight : _pressed ? Ui.Pressed : _hover ? Ui.Hover : BackColor;
        using var shape = Ui.Round(new RectangleF(1, 1, Width - 2, Height - 2), Ui.S(this, tab ? 8 : Radius));
        using var brush = new SolidBrush(fill); g.FillPath(brush, shape);
        if (!Primary && ((!tab && !quiet) || (tab && Active) || (quiet && _hover)))
        {
            Color outline = !Enabled ? Ui.Border : Destructive ? Ui.DangerBorder : Active && !tab ? Ui.Accent : _hover && !tab ? Ui.BorderStrong : Ui.Border;
            using var pen = new Pen(outline); g.DrawPath(pen, shape);
        }
        Color foreground = !Enabled ? Ui.DisabledText : Primary ? Color.White : Destructive ? Ui.Danger : Active ? Ui.Accent : ForeColor;
        if (tab && Active && Enabled)
        {
            int marker = Math.Min(Ui.S(this, 26), Width / 3);
            using var mark = new Pen(Ui.Accent, Math.Max(1.5f, Ui.S(this, 2))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(mark, (Width - marker) / 2, Height - Ui.S(this, 4), (Width + marker) / 2, Height - Ui.S(this, 4));
        }
        var textBounds = Rectangle.Inflate(ClientRectangle, -Ui.S(this, 7), -Ui.S(this, 3));
        if (_pressed) textBounds.Offset(0, Ui.S(this, 1));
        if (Glyph.Length > 0)
        {
            int iconSize = Ui.S(this, 17), gap = Text.Length==0?0:Ui.S(this, 7);
            int textWidth = Text.Length==0?0:Math.Min(TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width, Math.Max(1, textBounds.Width - iconSize - gap));
            int left = Math.Max(textBounds.Left, (Width - textWidth - iconSize - gap) / 2);
            Ui.Symbol(g, Glyph, new Rectangle(left, (Height - iconSize) / 2 + (_pressed ? Ui.S(this, 1) : 0), iconSize, iconSize), foreground);
            textBounds = new Rectangle(left + iconSize + gap, textBounds.Top, textWidth, textBounds.Height);
        }
        using var selectedFont = tab && Active ? new Font(Font, FontStyle.Bold) : null;
        TextRenderer.DrawText(g, Text, selectedFont ?? Font, textBounds, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
        {
            using var focus = Ui.Round(Rectangle.Inflate(ClientRectangle, -Ui.S(this, 4), -Ui.S(this, 4)), Ui.S(this, Math.Max(3, Radius - 3)));
            using var pen = new Pen(Primary ? Color.White : Ui.Accent, Ui.S(this, 1)) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, focus);
        }
    }
}

internal sealed class SelectCard : Control
{
    private bool _hover, _active;
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Glyph { get; set; } = "screen";
    public bool Active { get => _active; set { _active = value; Invalidate(); } }
    public SelectCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Font = Ui.Font(); BackColor = Ui.Surface; Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.RadioButton;
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Focus(); base.OnMouseDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Ui.ParentColor(this));
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 3 || Height < 3) return;
        var g = e.Graphics; g.Clear(Ui.ParentColor(this)); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Ui.Round(new RectangleF(1, 1, Width - 2, Height - 2), Ui.S(this, 10));
        using var fill = new SolidBrush(!Enabled ? Ui.Disabled : Active ? Ui.AccentLight : _hover ? Ui.Hover : Ui.Surface);
        using var border = new Pen(Active || Focused ? Ui.Accent : _hover && Enabled ? Ui.BorderStrong : Ui.Border, 1);
        g.FillPath(fill, path); g.DrawPath(border, path);
        Color foreground = Enabled ? Ui.Text : Ui.Subtle;
        var badge = new Rectangle(Ui.S(this, 10), (Height - Ui.S(this, 36)) / 2, Ui.S(this, 36), Ui.S(this, 36));
        using (var badgePath = Ui.Round(badge, Ui.S(this, 9)))
        using (var badgeFill = new SolidBrush(Active ? Ui.SelectedBadge : Ui.Background)) g.FillPath(badgeFill, badgePath);
        Ui.Symbol(g, Glyph, new Rectangle(badge.X + Ui.S(this, 7), badge.Y + Ui.S(this, 7), Ui.S(this, 22), Ui.S(this, 22)), Enabled && Active ? Ui.Accent : Ui.Subtle);
        int left = Ui.S(this, 54), width = Math.Max(1, Width - left - Ui.S(this, 12));
        using var titleFont = new Font(Font, FontStyle.Bold);
        using var subtitleFont = new Font(Font.FontFamily, Math.Max(8, Font.Size - 1));
        int center = Height / 2;
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, Title, titleFont, new Rectangle(left, center - Ui.S(this, 23), width, Ui.S(this, 24)), foreground, flags);
        TextRenderer.DrawText(g, Subtitle, subtitleFont, new Rectangle(left, center + Ui.S(this, 2), width, Ui.S(this, 22)), Ui.Subtle, flags);
    }
}

internal sealed class PreviewCanvas : Control
{
    public Bitmap? Picture { get; private set; }
    public string Helper { get; set; } = L10n.T("刷新预览，查看所选画面");
    public PreviewCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.PreviewBackground; Font = Ui.Font();
    }
    public void ChangePicture(Bitmap? next) { Picture?.Dispose(); Picture = next; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor);
        if (Width <= 0 || Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Picture is { } picture)
        {
            double ratio = Math.Min((double)Width / picture.Width, (double)Height / picture.Height);
            int width = Math.Max(1, (int)(picture.Width * ratio)), height = Math.Max(1, (int)(picture.Height * ratio));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(picture, new Rectangle((Width - width) / 2, (Height - height) / 2, width, height));
            return;
        }
        using var line = new Pen(Ui.PreviewGrid);
        int step = Math.Max(16, Ui.S(this, 24));
        for (int x = step; x < Width; x += step) g.DrawLine(line, x, 0, x, Height);
        for (int y = step; y < Height; y += step) g.DrawLine(line, 0, y, Width, y);
        int center = Height / 2;
        if (Height > Ui.S(this, 120)) Ui.Symbol(g, "screen", new Rectangle((Width - Ui.S(this, 32)) / 2, center - Ui.S(this, 48), Ui.S(this, 32), Ui.S(this, 32)), Ui.Subtle);
        using var title = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(g, L10n.T("预览尚未刷新"), title, new Rectangle(8, center - Ui.S(this, Height < Ui.S(this, 90) ? 13 : 7), Width - 16, Ui.S(this, 26)), Ui.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Height >= Ui.S(this, 90)) TextRenderer.DrawText(g, Helper, Font, new Rectangle(Ui.S(this, 12), center + Ui.S(this, 23), Width - Ui.S(this, 24), Ui.S(this, 36)), Ui.Subtle,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
    }
    protected override void Dispose(bool disposing) { if (disposing) Picture?.Dispose(); base.Dispose(disposing); }
}

internal sealed class SegmentedTabsPanel : TableLayoutPanel
{
    internal SegmentedTabsPanel(int columns=4)
    {
        Dock = DockStyle.Fill; Margin = Padding.Empty; Padding = new Padding(3);
        BackColor = Ui.Background; RowCount = 1; ColumnCount = columns;
        for (int i = 0; i < columns; i++) ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f/columns));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Ui.ParentColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Ui.Round(ClientRectangle, Ui.S(this, 11)); using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, shape);
    }
}
