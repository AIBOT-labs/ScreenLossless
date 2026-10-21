using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenLossless;

internal static class DialogLayout
{
    public static void Style(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96); form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = Ui.Font(); form.BackColor = Ui.Background;
        form.StartPosition = FormStartPosition.CenterParent;
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ScreenLossless.AppIcon");
        if (stream is not null) form.Icon = new Icon(stream);
    }
    public static TableLayoutPanel Grid(float[] columns, params float[] rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent,
            ColumnCount = columns.Length, RowCount = rows.Length };
        foreach (float value in columns) grid.ColumnStyles.Add(new ColumnStyle(value < 0 ? SizeType.Absolute : SizeType.Percent, Math.Abs(value)));
        foreach (float value in rows) grid.RowStyles.Add(new RowStyle(value < 0 ? SizeType.Percent : SizeType.Absolute, Math.Abs(value)));
        return grid;
    }
    public static Label Text(string text, float size = 9.5f, bool bold = false, Color? color = null) => new()
    {
        Text = text, Font = Ui.Font(size, bold), ForeColor = color ?? Ui.Text, BackColor = Color.Transparent,
        AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft
    };
    public static CardPanel Card() => new() { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = Padding.Empty };
    public static void Button(TableLayoutPanel parent, SoftButton button, int column)
    {
        button.Dock = DockStyle.Fill; button.Margin = new Padding(column == 0 ? 0 : 8, 0, 0, 0);
        if(L10n.IsEnglish&&parent.ColumnStyles[column].SizeType==SizeType.Absolute)
        {
            // Column widths are still design units here. Measuring screen pixels before
            // AutoScale would multiply the system DPI a second time on high-DPI displays.
            using var sample = new Bitmap(1, 1); sample.SetResolution(96, 96);
            using var measure = Graphics.FromImage(sample);
            int textWidth = (int)Math.Ceiling(measure.MeasureString(button.Text, button.Font,
                int.MaxValue, StringFormat.GenericTypographic).Width);
            int required = textWidth + (button.Glyph.Length > 0 ? 38 : 14) + button.Margin.Horizontal + 14;
            parent.ColumnStyles[column].Width = Math.Max(parent.ColumnStyles[column].Width, required);
        }
        parent.Controls.Add(button, column, 0);
    }
}

internal sealed class DetailListBox : ListBox
{
    public Func<object, string> TitleText { get; set; } = item => item.ToString() ?? "";
    public Func<object, string> DetailText { get; set; } = _ => "";
    public DetailListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false; BorderStyle = BorderStyle.None;
        BackColor = Ui.Surface; ForeColor = Ui.Text; Font = Ui.Font(); ItemHeight = 58;
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height * 2 + Ui.S(this, 22); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        using var background = new SolidBrush(Ui.Surface); e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var row = Rectangle.Inflate(e.Bounds, -Ui.S(this, 2), -Ui.S(this, 3));
        if (selected)
        {
            using var shape = Ui.Round(row, Ui.S(this, 8)); using var fill = new SolidBrush(Ui.AccentLight);
            g.FillPath(fill, shape); using var border = new Pen(Ui.BorderStrong); g.DrawPath(border, shape);
        }
        Ui.Symbol(g, "window", new Rectangle(row.X + Ui.S(this, 12), row.Y + (row.Height - Ui.S(this, 26)) / 2, Ui.S(this, 26), Ui.S(this, 26)), selected ? Ui.Accent : Ui.Subtle);
        int x = row.X + Ui.S(this, 50), center = row.Y + row.Height / 2;
        using var titleFont = new Font(Font, FontStyle.Bold); using var detailFont = Ui.Font(8.5f);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, TitleText(Items[e.Index]), titleFont, new Rectangle(x, center - Ui.S(this, 23), Math.Max(1, row.Width - Ui.S(this, 62)), Ui.S(this, 24)), Ui.Text, flags);
        TextRenderer.DrawText(g, DetailText(Items[e.Index]), detailFont, new Rectangle(x, center + Ui.S(this, 2), Math.Max(1, row.Width - Ui.S(this, 62)), Ui.S(this, 21)), Ui.Subtle, flags);
        if (selected && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(row, -Ui.S(this, 4), -Ui.S(this, 4)), Ui.Accent, Ui.AccentLight);
    }
}
