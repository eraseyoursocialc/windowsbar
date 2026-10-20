using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WindowsBar.Helper
{
    /// <summary>
    /// Reports when the active window fills the whole main screen (a game, a video, a slideshow),
    /// so the island can stay out of the way.
    /// </summary>
    internal sealed class FullscreenWatcher : IDisposable
    {
        private readonly Output _out;
        private readonly Timer _timer = new Timer { Interval = 1000 };
        private bool? _last;

        public FullscreenWatcher(Output output)
        {
            _out = output;
            _timer.Tick += (s, e) => Check();
            _timer.Start();
        }

        private void Check()
        {
            bool now = IsForegroundFullscreen();
            if (now == _last)
                return;
            _last = now;
            _out.Send(new FlagMessage { Type = "fullscreen", Value = now });
        }

        public void Dispose() => _timer.Dispose();

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static bool IsForegroundFullscreen()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || hwnd == GetDesktopWindow())
                return false;

            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "WorkerW" || cls.ToString() == "Progman")
                return false; // the desktop itself

            // Ignore the island's own window.
            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                if (Process.GetProcessById((int)pid).ProcessName.StartsWith("WindowsBar", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch
            {
                // Process already gone.
            }

            if (!GetWindowRect(hwnd, out var r))
                return false;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) || (info.dwFlags & 1) == 0)
                return false; // only the main screen matters

            var m = info.rcMonitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }
    }
}
