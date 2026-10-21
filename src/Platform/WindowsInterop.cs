using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ScreenLossless;

internal static class WindowsInterop
{
    internal const int WmHotkey = 0x0312;
    internal const uint ModControl = 0x0002;
    internal const uint ModAlt = 0x0001;
    internal const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out WinRect rect, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint { public int X,Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out WinRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window,ref WinPoint point);
    internal static Rectangle GetWindowClientBounds(IntPtr window)
    {
        if(!IsWindowVisible(window)||IsIconic(window)||!GetClientRect(window,out var rect))
            throw new InvalidOperationException(L10n.T("所选窗口已关闭、隐藏或最小化。"));
        var origin=new WinPoint();
        if(!ClientToScreen(window,ref origin))throw new InvalidOperationException(L10n.T("无法读取窗口位置。"));
        var bounds=new Rectangle(origin.X,origin.Y,rect.Right-rect.Left,rect.Bottom-rect.Top);
        if(bounds.Width<64||bounds.Height<64)throw new InvalidOperationException(L10n.T("窗口内容至少需要 64 × 64 像素。"));
        return bounds;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom); }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref WinRect rect, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    internal static (int X, int Y) MonitorDpi(Rectangle bounds)
    {
        try
        {
            var rect = new WinRect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
            if (GetDpiForMonitor(MonitorFromRect(ref rect, 2), 0, out uint x, out uint y) == 0 && x > 0 && y > 0)
                return ((int)x, (int)y);
        }
        catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        return (96, 96);
    }

    public static List<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        var screens = Screen.AllScreens;
        var adapters = new List<(string Name, string Gpu)>();
        for (uint i = 0; i < 32; i++)
        {
            var dd = new DisplayDevice
            {
                cb = Marshal.SizeOf<DisplayDevice>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceID = string.Empty,
                DeviceKey = string.Empty
            };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & 0x1) == 0) continue; // Attached to desktop
            adapters.Add((dd.DeviceName ?? "", dd.DeviceString ?? ""));
        }
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            string adapter = adapters.FirstOrDefault(a => a.Name.Equals(screen.DeviceName, StringComparison.OrdinalIgnoreCase)).Gpu;
            if (string.IsNullOrEmpty(adapter)) adapter = L10n.T("未知显卡");
            var dpi = MonitorDpi(screen.Bounds);
            result.Add(new MonitorInfo(i, screen.DeviceName, screen.Bounds, screen.Primary, adapter, dpi.X, dpi.Y));
        }
        return result;
    }

    public static string DescribeGpu(IEnumerable<MonitorInfo> monitors)
    {
        var names = monitors.Select(m => m.Adapter).Where(s => !string.IsNullOrWhiteSpace(s) && s != L10n.T("未知显卡"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            // nvidia-smi is supplementary only: a monitor can be driven by another adapter.
            foreach (string path in new [] { "nvidia-smi.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvidia-smi.exe") })
            {
                try
                {
                    var psi = new ProcessStartInfo(path, "--query-gpu=name --format=csv,noheader")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    using var process = Process.Start(psi);
                    if (process is null) continue;
                    string text = process.StandardOutput.ReadToEnd().Trim();
                    if (!process.WaitForExit(1500)) { try { process.Kill(); } catch { } continue; }
                    if (!string.IsNullOrWhiteSpace(text)) return text.Replace("\r", "").Replace("\n", "、");
                }
                catch { }
            }
            return L10n.T("未检测到显卡信息");
        }
        return string.Join("、", names);
    }

    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    internal static WindowSelection? DescribeWindow(WindowInfo window)
    {
        try
        {
            GetWindowThreadProcessId(window.Handle,out uint pid);
            using var process=Process.GetProcessById((int)pid);
            var name=new StringBuilder(256);GetClassName(window.Handle,name,name.Capacity);
            return new WindowSelection{ProcessPath=process.MainModule?.FileName??"",ClassName=name.ToString(),Title=window.Title};
        }catch{return null;}
    }
    internal static WindowInfo? RestoreWindow(WindowSelection selection)=>selection.Resolve(GetVisibleWindows().Select(w=>(w,DescribeWindow(w))));

    public static List<WindowInfo> GetVisibleWindows()
    {
        var result = new List<WindowInfo>();
        uint myPid = (uint)Environment.ProcessId;
        IntPtr shell = GetShellWindow();
        EnumWindows((hWnd, _) =>
        {
            if (hWnd == shell || !IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == myPid) return true;
            int n = GetWindowTextLength(hWnd);
            if (n < 2 || n > 300) return true;
            var text = new StringBuilder(n + 1);
            GetWindowText(hWnd, text, text.Capacity);
            if (!GetWindowRect(hWnd, out var winRect)) return true;
            try
            {
                if (DwmGetWindowAttribute(hWnd, 9, out var dwmRect, Marshal.SizeOf<WinRect>()) == 0)
                    winRect = dwmRect;
            }
            catch { }
            var bounds = winRect.ToRectangle();
            if (bounds.Width < 100 || bounds.Height < 70) return true;
            result.Add(new WindowInfo(hWnd, text.ToString().Trim(), bounds));
            return true;
        }, IntPtr.Zero);
        return result.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
