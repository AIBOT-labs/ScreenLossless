namespace ScreenLossless;

internal static class ScreenCapture
{
    internal static void ValidateBounds(Rectangle bounds,Rectangle desktop)
    {
        if(bounds.Width<64||bounds.Height<64)throw new InvalidOperationException(L10n.T("请先选择至少 64 × 64 像素的录制范围。"));
        if(!desktop.Contains(bounds))throw new InvalidOperationException(L10n.T("所选范围已不在当前桌面内，请重新选择区域或显示器。"));
    }
    internal static void ValidateBitmapSize(Size size)
    {
        if(size.Width<1||size.Height<1||(long)size.Width*size.Height>64_000_000)
            throw new InvalidOperationException(L10n.T("截图范围过大，请选择单个显示器或缩小区域。"));
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] private static extern bool BitBlt(IntPtr dest,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint operation);
    internal static Bitmap TakeWindow(IntPtr window)
    {
        var bounds=WindowsInterop.GetWindowClientBounds(window);ValidateBitmapSize(bounds.Size);
        var bitmap=new Bitmap(bounds.Width,bounds.Height);
        IntPtr source=GetDC(window);
        try
        {
            if(source==IntPtr.Zero)throw new InvalidOperationException(L10n.T("无法读取窗口画面。"));
            using var graphics=Graphics.FromImage(bitmap);IntPtr destination=graphics.GetHdc();
            try{if(!BitBlt(destination,0,0,bounds.Width,bounds.Height,source,0,0,0x40CC0020))throw new InvalidOperationException(L10n.T("窗口不支持 GDI 捕获，请使用区域录制。"));}
            finally{graphics.ReleaseHdc(destination);}
            return bitmap;
        }
        catch{bitmap.Dispose();throw;}
        finally{if(source!=IntPtr.Zero)ReleaseDC(window,source);}
    }
    internal static Bitmap Take(Rectangle bounds)
    {
        ValidateBounds(bounds,SystemInformation.VirtualScreen); ValidateBitmapSize(bounds.Size);
        var bitmap=new Bitmap(bounds.Width,bounds.Height);
        try { using var g=Graphics.FromImage(bitmap);g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);return bitmap; }
        catch {bitmap.Dispose();throw;}
    }
}