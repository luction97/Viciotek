using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Viciotek.Infrastructure.Shell;

/// <summary>
/// Small, best-effort DWM integration. It asks Windows 11 for dark window
/// chrome and rounded corners; older builds simply ignore the request. It also
/// keeps a maximized borderless window inside the monitor work area so that
/// bottom-anchored content (such as the toast) is never hidden by the taskbar.
/// </summary>
public static class WindowAppearance
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = 1;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        var corners = DwmwcpRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corners, sizeof(int));
    }

    public static void RestrictMaximizedToWorkArea(Window window)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(OnWindowMessage);
    }

    public static Size WorkAreaSize(Window reference)
    {
        var handle = new WindowInteropHelper(reference).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info))
            return SystemParameters.WorkArea.Size;
        return WorkAreaSizeInDips(info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top, GetDpiForWindow(handle) / 96d);
    }

    internal static Size WorkAreaSizeInDips(double width, double height, double scale) =>
        new(width / (scale > 0 ? scale : 1), height / (scale > 0 ? scale : 1));

    public static void KeepDialogOnScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info) || !GetWindowRect(handle, out var bounds)) return;
        var x = Math.Clamp(bounds.Left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - (bounds.Right - bounds.Left)));
        var y = Math.Clamp(bounds.Top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - (bounds.Bottom - bounds.Top)));
        if (x != bounds.Left || y != bounds.Top)
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x0015); // No size, z-order or activation changes.
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private static IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                minMax.MaxSize.X = info.Work.Right - info.Work.Left;
                minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                Marshal.StructureToPtr(minMax, lParam, true);
            }
        }

        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }
}
