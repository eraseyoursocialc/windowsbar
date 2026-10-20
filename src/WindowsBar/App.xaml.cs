using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WindowsBar.Services;

namespace WindowsBar
{
    public partial class App : Application
    {
        private Mutex? _mutex;
        private bool _ownsMutex;
        private TrayIcon? _trayIcon;
        private NotchWindow? _window;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Only one island at a time.
            _mutex = new Mutex(true, "WindowsBar.SingleInstance", out _ownsMutex);
            if (!_ownsMutex)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnUnhandledException;
            base.OnStartup(e);

            var settings = Settings.Load();
            _window = new NotchWindow(settings);
            _window.Show();
            _trayIcon = new TrayIcon(settings);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            if (_ownsMutex)
                _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Keep the island alive; write the problem to the log for later.
            Log.Error("Unhandled exception", e.Exception);
            e.Handled = true;
        }
    }
}
