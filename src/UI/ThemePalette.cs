using System.Drawing.Drawing2D;

namespace ScreenLossless;

internal enum AppTheme { Light, Dark }

internal sealed class ThemePalette(bool dark)
{
    internal Color Background { get; } = ColorTranslator.FromHtml(dark ? "#151922" : "#F3F5F9");
    internal Color Surface { get; } = dark ? Color.FromArgb(32,38,49) : Color.White;
    internal Color Text { get; } = ColorTranslator.FromHtml(dark ? "#EDF1F7" : "#1C2537");
    internal Color Subtle { get; } = ColorTranslator.FromHtml(dark ? "#ADB8CA" : "#69768B");
    internal Color Accent { get; } = ColorTranslator.FromHtml(dark ? "#A3AFFF" : "#495BC9");
    internal Color ScrollThumb { get; } = ColorTranslator.FromHtml(dark ? "#6474B6" : "#9DA7E1");
    internal Color AccentLight { get; } = ColorTranslator.FromHtml(dark ? "#2B3553" : "#EFF2FF");
    internal Color Border { get; } = ColorTranslator.FromHtml(dark ? "#363F50" : "#E1E6EF");
    internal Color BorderStrong { get; } = ColorTranslator.FromHtml(dark ? "#53617A" : "#CCD5E6");
    internal Color Hover { get; } = ColorTranslator.FromHtml(dark ? "#293240" : "#F7F9FD");
    internal Color Disabled { get; } = ColorTranslator.FromHtml(dark ? "#252C38" : "#EFF2F7");
    internal Color DisabledText { get; } = ColorTranslator.FromHtml(dark ? "#8290A6" : "#97A2B4");
    internal Color Danger { get; } = ColorTranslator.FromHtml(dark ? "#F29BAD" : "#B83E4F");
    internal Color DangerLight { get; } = ColorTranslator.FromHtml(dark ? "#3A2731" : "#FDEFF2");
    internal Color DangerBorder { get; } = ColorTranslator.FromHtml(dark ? "#6B4252" : "#EFCDD5");
    internal Color Mint { get; } = ColorTranslator.FromHtml(dark ? "#72D6B5" : "#1E8769");
    internal Color PrimaryFill { get; } = ColorTranslator.FromHtml(dark ? "#5565CD" : "#495BC9");
    internal Color PrimaryHover { get; } = ColorTranslator.FromHtml(dark ? "#6172DE" : "#3D4FBB");
    internal Color PrimaryPressed { get; } = ColorTranslator.FromHtml(dark ? "#4858B8" : "#3646A9");
    internal Color Pressed { get; } = ColorTranslator.FromHtml(dark ? "#313D50" : "#E5EAF5");
    internal Color DangerHover { get; } = ColorTranslator.FromHtml(dark ? "#482F3C" : "#FBE7EC");
    internal Color DangerPressed { get; } = ColorTranslator.FromHtml(dark ? "#543746" : "#F9DFE6");
    internal Color SelectedBadge { get; } = ColorTranslator.FromHtml(dark ? "#3B486E" : "#E1E7FF");
    internal Color PreviewBackground { get; } = ColorTranslator.FromHtml(dark ? "#1B212C" : "#F7F9FC");
    internal Color PreviewGrid { get; } = ColorTranslator.FromHtml(dark ? "#2A3444" : "#ECF0F6");
    internal Color Warning { get; } = ColorTranslator.FromHtml(dark ? "#E7BB76" : "#AA761A");
    internal Color Recording { get; } = ColorTranslator.FromHtml(dark ? "#F18DA5" : "#C54156");
}

internal static partial class Ui
{
    private static readonly ThemePalette LightPalette=new(false),DarkPalette=new(true);
    private static ThemePalette _colors=LightPalette;
    internal static AppTheme Theme { get; private set; }
    internal static bool IsDark=>Theme==AppTheme.Dark;
    internal static event Action<AppTheme>? ThemeChanged;
    internal static void Configure(AppTheme theme)
    {
        Theme=Enum.IsDefined(theme)?theme:AppTheme.Light;
        _colors=IsDark?DarkPalette:LightPalette;
    }
    internal static void ChangeTheme(AppTheme theme)
    {
        if(!Enum.IsDefined(theme)||Theme==theme)return;
        var previous=Theme;Configure(theme);
        var windows=Application.OpenForms.Cast<Form>().ToArray();
        foreach(var form in windows)Retheme(form,previous);
        ThemeChanged?.Invoke(previous);
        // Flush transparent and double-buffered children after every surface has
        // changed, so nested status panels cannot retain pixels from the old theme.
        foreach(var form in windows)if(!form.IsDisposed&&form.Visible)form.Refresh();
    }
    internal static void Retheme(Control control,AppTheme previous)
    {
        if(control.IsDisposed)return;
        var old=previous==AppTheme.Dark?DarkPalette:LightPalette;
        Color Map(Color value)
        {
            if(value.A==0)return value;
            // Only semantic surface/text colors are remapped. Absolute white
            // glyphs, captured images and the high-contrast selection outline stay intact.
            if(value.ToArgb()==old.Background.ToArgb())return _colors.Background;
            if(value.ToArgb()==old.Surface.ToArgb())return _colors.Surface;
            if(value.ToArgb()==old.Text.ToArgb())return _colors.Text;
            if(value.ToArgb()==old.Subtle.ToArgb())return _colors.Subtle;
            if(value.ToArgb()==old.Accent.ToArgb())return _colors.Accent;
            if(value.ToArgb()==old.ScrollThumb.ToArgb())return _colors.ScrollThumb;
            if(value.ToArgb()==old.AccentLight.ToArgb())return _colors.AccentLight;
            if(value.ToArgb()==old.Border.ToArgb())return _colors.Border;
            if(value.ToArgb()==old.BorderStrong.ToArgb())return _colors.BorderStrong;
            if(value.ToArgb()==old.Hover.ToArgb())return _colors.Hover;
            if(value.ToArgb()==old.Disabled.ToArgb())return _colors.Disabled;
            if(value.ToArgb()==old.DisabledText.ToArgb())return _colors.DisabledText;
            if(value.ToArgb()==old.Danger.ToArgb())return _colors.Danger;
            if(value.ToArgb()==old.DangerLight.ToArgb())return _colors.DangerLight;
            if(value.ToArgb()==old.DangerBorder.ToArgb())return _colors.DangerBorder;
            if(value.ToArgb()==old.Mint.ToArgb())return _colors.Mint;
            if(value.ToArgb()==old.SelectedBadge.ToArgb())return _colors.SelectedBadge;
            if(value.ToArgb()==old.PreviewBackground.ToArgb())return _colors.PreviewBackground;
            if(value.ToArgb()==old.PreviewGrid.ToArgb())return _colors.PreviewGrid;
            if(value.ToArgb()==old.Warning.ToArgb())return _colors.Warning;
            if(value.ToArgb()==old.Recording.ToArgb())return _colors.Recording;
            return value;
        }
        control.SuspendLayout();
        try
        {
            if(control is ChoiceBox choice)choice.DroppedDown=false;
            if(control is CardPanel card){card.CardColor=Map(card.CardColor);card.BorderColor=Map(card.BorderColor);}
            if(control is CaptionButton caption)caption.BaseColor=Map(caption.BaseColor);
            control.BackColor=Map(control.BackColor);control.ForeColor=Map(control.ForeColor);
            if(control is ContextMenuStrip menu){menu.Close();StyleMenu(menu);}
            foreach(Control child in control.Controls)Retheme(child,previous);
            control.Invalidate(true);
        }
        finally{control.ResumeLayout(false);}
    }
    internal static void StyleMenu(ContextMenuStrip menu)
    {
        menu.BackColor=Surface;menu.ForeColor=Text;menu.Renderer=new ThemeMenuRenderer();
        foreach(ToolStripItem item in menu.Items){item.BackColor=Surface;item.ForeColor=Text;}
    }
}

internal class ThemeMenuRenderer : ToolStripProfessionalRenderer
{
    internal ThemeMenuRenderer():base(new ThemeMenuColors()) { }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)=>e.Graphics.Clear(Ui.Surface);
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {e.TextColor=e.Item.Enabled?Ui.Text:Ui.DisabledText;base.OnRenderItemText(e);}
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {e.ArrowColor=e.Item?.Enabled==true?Ui.Subtle:Ui.DisabledText;base.OnRenderArrow(e);}
    private sealed class ThemeMenuColors:ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>Ui.Surface;
        public override Color MenuBorder=>Ui.BorderStrong;
        public override Color MenuItemBorder=>Ui.BorderStrong;
        public override Color MenuItemSelected=>Ui.AccentLight;
        public override Color MenuItemSelectedGradientBegin=>Ui.AccentLight;
        public override Color MenuItemSelectedGradientEnd=>Ui.AccentLight;
        public override Color MenuItemPressedGradientBegin=>Ui.Pressed;
        public override Color MenuItemPressedGradientMiddle=>Ui.Pressed;
        public override Color MenuItemPressedGradientEnd=>Ui.Pressed;
        public override Color ImageMarginGradientBegin=>Ui.Surface;
        public override Color ImageMarginGradientMiddle=>Ui.Surface;
        public override Color ImageMarginGradientEnd=>Ui.Surface;
        public override Color SeparatorDark=>Ui.Border;
        public override Color SeparatorLight=>Ui.Surface;
        public override Color CheckBackground=>Ui.AccentLight;
        public override Color CheckSelectedBackground=>Ui.AccentLight;
        public override Color CheckPressedBackground=>Ui.AccentLight;
    }
}

internal sealed class ThemeToolTip:ToolTip
{
    internal ThemeToolTip()
    {
        OwnerDraw=true;
        Draw+=(_,e)=>
        {
            using var background=new SolidBrush(Ui.Surface);e.Graphics.FillRectangle(background,e.Bounds);
            using var edge=new Pen(Ui.BorderStrong);e.Graphics.DrawRectangle(edge,e.Bounds.X,e.Bounds.Y,e.Bounds.Width-1,e.Bounds.Height-1);
            var bounds=Rectangle.Inflate(e.Bounds,-4,-2);
            TextRenderer.DrawText(e.Graphics,e.ToolTipText,e.Font,bounds,Ui.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        };
    }
}
