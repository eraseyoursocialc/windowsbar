using System;
using System.Linq;
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
        private bool _started;

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
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log.Error("Fatal error", args.ExceptionObject as Exception);
            base.OnStartup(e);

            try
            {
                var settings = Settings.Load();
                _window = new NotchWindow(settings, openOnStart: e.Args.Contains("--open"));
                _window.Show();
                _trayIcon = new TrayIcon(settings);
                _started = true;
            }
            catch (Exception ex)
            {
                FailStartup(ex);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            if (_ownsMutex)
                _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            if (!_started)
            {
                FailStartup(e.Exception);
                return;
            }
            // Once running, keep the island alive and note the problem in the log.
            Log.Error("Unhandled exception", e.Exception);
        }

        /// <summary>Never sit invisibly in the background: say what went wrong and quit.</summary>
        private void FailStartup(Exception ex)
        {
            Log.Error("Could not start", ex);
            MessageBox.Show(
                $"WindowsBar couldn't start.\n\n{ex.GetBaseException().Message}\n\n" +
                $"Details were saved to {AppPaths.Dir}\\log.txt",
                "WindowsBar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
