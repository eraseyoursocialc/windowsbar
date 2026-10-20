using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace WindowsBar.Services
{
    /// <summary>The icon next to the clock, with the app's settings and Quit.</summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;

        public TrayIcon(Settings settings)
        {
            var startup = new Forms.ToolStripMenuItem("Start with Windows")
            {
                CheckOnClick = true,
                Checked = StartupManager.IsEnabled,
            };
            startup.CheckedChanged += (_, _) => StartupManager.SetEnabled(startup.Checked);

            var fullscreen = new Forms.ToolStripMenuItem("Hide when an app is fullscreen")
            {
                CheckOnClick = true,
                Checked = settings.HideInFullscreen,
            };
            fullscreen.CheckedChanged += (_, _) =>
            {
                settings.HideInFullscreen = fullscreen.Checked;
                settings.Save();
            };

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(startup);
            menu.Items.Add(fullscreen);
            menu.Items.Add("Open data folder", null, (_, _) =>
                Process.Start(new ProcessStartInfo(AppPaths.Dir) { UseShellExecute = true }));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Quit WindowsBar", null, (_, _) => System.Windows.Application.Current.Shutdown());

            _icon = new Forms.NotifyIcon
            {
                Icon = CreateIcon(),
                Text = "WindowsBar",
                ContextMenuStrip = menu,
                Visible = true,
            };
        }

        /// <summary>Draws a small white pill so the app needs no icon file.</summary>
        private static Icon CreateIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.White, 2, 10, 12, 12);
                g.FillEllipse(Brushes.White, 18, 10, 12, 12);
                g.FillRectangle(Brushes.White, 8, 10, 16, 12);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
