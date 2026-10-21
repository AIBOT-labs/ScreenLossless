namespace ScreenLossless;

internal sealed class RegionSelection
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    internal static RegionSelection? FromRectangle(Rectangle bounds) => bounds.Width < 64 || bounds.Height < 64
        ? null : new() { X=bounds.X, Y=bounds.Y, Width=bounds.Width, Height=bounds.Height };
    internal Rectangle Restore(Rectangle desktop)
    {
        if(Width<64 || Height<64 || desktop.Width<64 || desktop.Height<64) return Rectangle.Empty;
        int width=Math.Min(Width,desktop.Width), height=Math.Min(Height,desktop.Height);
        return new Rectangle(Math.Clamp(X,desktop.Left,desktop.Right-width),Math.Clamp(Y,desktop.Top,desktop.Bottom-height),width,height);
    }
}