using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsBar.Services
{
    /// <summary>The handful of Win32 calls WPF doesn't expose.</summary>
    internal static class Native
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint MONITORINFOF_PRIMARY = 1;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
        private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        /// <summary>Keeps the window out of Alt+Tab and the taskbar.</summary>
        public static void MakeToolWindow(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// When on, clicking the window doesn't take focus from the app you were using.
        /// Turned off while typing in a note or task, which needs the keyboard.
        /// </summary>
        public static void SetNoActivate(Window window, bool on)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int style = GetWindowLong(hwnd, GWL_EXSTYLE);
            style = on ? style | WS_EX_NOACTIVATE : style & ~WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, style);
        }

        /// <summary>Puts the window back on top in case another always-on-top app pushed it down.</summary>
        public static void KeepOnTop(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>True when the active window covers the whole primary screen (games, videos, slideshows).</summary>
        public static bool IsForegroundFullscreen(Window self)
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == new WindowInteropHelper(self).Handle ||
                hwnd == GetShellWindow() || hwnd == GetDesktopWindow())
                return false;

            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is "WorkerW" or "Progman")
                return false; // the desktop itself

            if (!GetWindowRect(hwnd, out var r))
                return false;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
                return false;
            if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0)
                return false;

            var m = info.rcMonitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }

        /// <summary>The icon Explorer shows for a file or folder.</summary>
        public static ImageSource? GetShellIcon(string path)
        {
            try
            {
                var info = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                if (info.hIcon == IntPtr.Zero)
                    return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally
                {
                    DestroyIcon(info.hIcon);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"No icon for {path}", ex);
                return null;
            }
        }
    }
}
