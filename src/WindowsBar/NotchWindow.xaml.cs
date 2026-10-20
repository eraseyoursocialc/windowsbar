using System;
using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WindowsBar.Services;
using WindowsBar.Widgets;

namespace WindowsBar
{
    public partial class NotchWindow : Window
    {
        private enum Mode { Collapsed, Expanded, Drop }
        private enum Tab { Nook, Tray }

        // Island sizes in device-independent pixels.
        private const double IdleWidth = 180, IdleHeight = 30;
        private const double CountWidth = 210;
        private const double ActiveWidth = 250, ActiveHeight = 32;
        private const double ToastWidth = 310;
        private const double ExpandedHeight = 186;
        private const double DropWidth = 540, DropHeight = 150;

        private readonly Settings _settings;
        private readonly MediaService _media;
        private readonly CameraService _camera;
        private readonly TimerService _timers = new();
        private readonly FileShelf _shelf = new();
        private readonly BatteryWatcher _battery = new();

        private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
        private readonly DispatcherTimer _toastTimer = new();
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

        private Mode _mode = Mode.Collapsed;
        private Tab _tab = Tab.Nook;
        private double _expandedWidth = 1200;
        private bool _overShareZone;
        private DateTime _lastInside = DateTime.Now;
        private int _tick;
        private readonly bool _pinnedOpen;

        private string? _toastText;
        private string? _toastGlyph;

        private readonly ScaleTransform[] _bars = new ScaleTransform[4];
        private bool _barsRunning;

        /// <param name="openOnStart">Open the island and keep it open (used for screenshots).</param>
        public NotchWindow(Settings settings, bool openOnStart = false)
        {
            InitializeComponent();
            _settings = settings;
            _pinnedOpen = openOnStart;
            _media = new MediaService(Dispatcher);
            _camera = new CameraService(Dispatcher);

            _shelf.Load();
            Shelf.Attach(_shelf);
            Shelf.DragOutFinished += () => _leaveTimer.Start();
            _shelf.Items.CollectionChanged += (_, _) => RefreshCompact();

            Media.Attach(_media);
            Mirror.Attach(_camera);
            Timers.Attach(_timers);

            BuildEqualizer();
            GearButton.ContextMenu = BuildSettingsMenu();

            _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); Expand(); };
            _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); CollapseIfOutside(); };
            _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toastText = null; RefreshCompact(); };
            _clock.Tick += (_, _) => OnClockTick();

            _media.Changed += () =>
            {
                Media.Refresh();
                RefreshCompact();
            };
            _timers.Ticked += OnTimersTicked;
            _timers.Finished += timer =>
            {
                SystemSounds.Asterisk.Play();
                ShowToast("", $"Timer done  ·  {CountdownTimer.Format(timer.Length)}", 8);
            };
            _battery.PowerChanged += (percent, pluggedIn) =>
            {
                if (pluggedIn)
                    ShowToast("", $"Charging  ·  {percent}%", 4);
                else
                    ShowToast("", $"On battery  ·  {percent}%", 3);
            };

            Loaded += OnLoaded;
            Deactivated += (_, _) => _leaveTimer.Start();
            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(PlaceAtTop);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Native.MakeToolWindow(this);
            Native.SetNoActivate(this, true);
            PlaceAtTop();

            ShowTab(Tab.Nook);
            OnClockTick();
            RefreshCompact();
            _clock.Start();
            if (_pinnedOpen)
                Expand();

            await _media.InitAsync();
        }

        /// <summary>Sizes the window to the screen and centers it at the very top.</summary>
        private void PlaceAtTop()
        {
            _expandedWidth = Math.Min(1200, SystemParameters.PrimaryScreenWidth - 40);
            ExpandedView.Width = _expandedWidth;
            Width = _expandedWidth + 40;
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = 0;
        }

        // ------------------------------------------------------------------
        // Opening and closing
        // ------------------------------------------------------------------

        private void Island_MouseEnter(object sender, MouseEventArgs e)
        {
            _leaveTimer.Stop();
            if (_mode == Mode.Collapsed)
                _hoverTimer.Start();
        }

        private void Island_MouseLeave(object sender, MouseEventArgs e)
        {
            _hoverTimer.Stop();
            _leaveTimer.Start();
        }

        private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mode == Mode.Collapsed)
            {
                _hoverTimer.Stop();
                Expand();
            }
        }

        private void Island_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Typing needs keyboard focus, so let the window activate when a text box is clicked.
            if (Helpers.FindAncestor<TextBoxBase>(e.OriginalSource as DependencyObject, Island) != null)
            {
                Native.SetNoActivate(this, false);
                Activate();
            }
        }

        /// <summary>Closes the island unless the mouse is still on it or the user is busy with it.</summary>
        private void CollapseIfOutside()
        {
            if (_mode == Mode.Collapsed || IsBusy() || IsCursorOverIsland())
                return;
            Collapse();
        }

        private bool IsBusy() =>
            _pinnedOpen ||
            Shelf.IsDraggingOut ||
            GearButton.ContextMenu.IsOpen ||
            (IsActive && Island.IsKeyboardFocusWithin); // typing a note or task

        /// <summary>
        /// Checks the real cursor position. WPF's own IsMouseOver goes stale during
        /// file drags, so this is what decides whether to close.
        /// </summary>
        private bool IsCursorOverIsland()
        {
            if (!Island.IsVisible || !Native.GetCursorPos(out var p))
                return false;
            var topLeft = Island.PointToScreen(new Point(0, 0));
            var bottomRight = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
            return p.X >= topLeft.X && p.X <= bottomRight.X && p.Y >= topLeft.Y - 2 && p.Y <= bottomRight.Y;
        }

        private void Expand()
        {
            if (Island.Visibility != Visibility.Visible)
                return;
            var from = _mode;
            if (from == Mode.Expanded)
                return;
            _mode = Mode.Expanded;
            _lastInside = DateTime.Now;

            Island.CornerRadius = new CornerRadius(0, 0, 30, 30);
            AnimateIsland(_expandedWidth, ExpandedHeight, 450, new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut });
            Show(CompactView, false, 90, 0);
            Show(DropView, false, 90, 0);
            Show(ExpandedView, true, 220, from == Mode.Drop ? 60 : 120);

            Media.Refresh();
            Calendar.Refresh();
        }

        private void ShowDropTargets()
        {
            if (_mode == Mode.Drop || Island.Visibility != Visibility.Visible)
                return;
            _mode = Mode.Drop;
            _lastInside = DateTime.Now;

            TrayZoneCount.Text = _shelf.Items.Count switch
            {
                0 => "Keep files handy",
                1 => "1 file",
                var n => $"{n} files",
            };
            SetDropTarget(false);

            Island.CornerRadius = new CornerRadius(0, 0, 28, 28);
            AnimateIsland(DropWidth, DropHeight, 380, new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut });
            Show(CompactView, false, 90, 0);
            Show(ExpandedView, false, 90, 0);
            Show(DropView, true, 200, 100);
        }

        private void Collapse()
        {
            if (_mode == Mode.Collapsed)
                return;
            _mode = Mode.Collapsed;

            _ = Mirror.StopAsync();
            Notes.Save();
            Keyboard.ClearFocus();
            Native.SetNoActivate(this, true);

            Show(ExpandedView, false, 110, 0);
            Show(DropView, false, 110, 0);
            Show(CompactView, true, 200, 160);
            RefreshCompact();
        }

        private void AnimateIsland(double width, double height, int ms, IEasingFunction ease)
        {
            var duration = TimeSpan.FromMilliseconds(ms);
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
        }

        private static void Show(UIElement element, bool visible, int ms, int delayMs)
        {
            element.IsHitTestVisible = visible;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
            });
        }

        // ------------------------------------------------------------------
        // The collapsed pill ("live activities")
        // ------------------------------------------------------------------

        /// <summary>
        /// Decides what the small pill shows, most important first:
        /// a short notice, a running timer, music, then how many files are in the tray.
        /// </summary>
        private void RefreshCompact()
        {
            CompactArt.Visibility = Visibility.Collapsed;
            CompactGlyph.Visibility = Visibility.Collapsed;
            CompactCount.Visibility = Visibility.Collapsed;
            CompactText.Text = "";
            bool playing = _media.HasSession && _media.IsPlaying;
            var timer = _timers.Soonest;
            SetEqualizer(false);

            double width = IdleWidth, height = IdleHeight;

            if (_toastText != null)
            {
                CompactGlyph.Text = _toastGlyph ?? "";
                CompactGlyph.Visibility = Visibility.Visible;
                CompactText.Text = _toastText;
                width = ToastWidth;
                height = ActiveHeight;
            }
            else if (timer != null)
            {
                CompactGlyph.Text = "";
                CompactGlyph.Visibility = Visibility.Visible;
                CompactText.Text = timer.RemainingText;
                SetEqualizer(playing);
                width = ActiveWidth;
                height = ActiveHeight;
            }
            else if (playing)
            {
                CompactArtImage.Source = _media.Artwork;
                CompactArt.Visibility = Visibility.Visible;
                SetEqualizer(true);
                width = ActiveWidth;
                height = ActiveHeight;
            }
            else if (_shelf.Items.Count > 0)
            {
                CompactGlyph.Text = "";
                CompactGlyph.Visibility = Visibility.Visible;
                CompactCountText.Text = _shelf.Items.Count.ToString();
                CompactCount.Visibility = Visibility.Visible;
                width = CountWidth;
            }

            if (_mode == Mode.Collapsed)
            {
                Island.CornerRadius = new CornerRadius(0, 0, height / 2, height / 2);
                AnimateIsland(width, height, 300, new CubicEase { EasingMode = EasingMode.EaseOut });
            }
        }

        private void ShowToast(string glyph, string text, int seconds)
        {
            _toastGlyph = glyph;
            _toastText = text;
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
            _toastTimer.Start();
            RefreshCompact();
        }

        private void OnTimersTicked()
        {
            // Only re-layout the pill when the shown text actually changes.
            if (_mode != Mode.Collapsed || _toastText != null)
                return;
            string expected = _timers.Soonest?.RemainingText ?? "";
            if (CompactText.Text != expected)
                RefreshCompact();
        }

        private void BuildEqualizer()
        {
            for (int i = 0; i < _bars.Length; i++)
            {
                _bars[i] = new ScaleTransform(1, 0.3);
                Equalizer.Children.Add(new Rectangle
                {
                    Width = 3,
                    Height = 14,
                    Margin = new Thickness(1, 0, 1, 0),
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = (Brush)FindResource("AccentBrush"),
                    RenderTransformOrigin = new Point(0.5, 1),
                    RenderTransform = _bars[i],
                });
            }
        }

        private void SetEqualizer(bool on)
        {
            Equalizer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on == _barsRunning)
                return;
            _barsRunning = on;

            int[] speeds = { 420, 300, 520, 360 };
            for (int i = 0; i < _bars.Length; i++)
            {
                if (on)
                {
                    _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty,
                        new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(speeds[i]))
                        {
                            AutoReverse = true,
                            RepeatBehavior = RepeatBehavior.Forever,
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                        });
                }
                else
                {
                    _bars[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    _bars[i].ScaleY = 0.3;
                }
            }
        }

        // ------------------------------------------------------------------
        // Once a second: progress bar, calendar, battery, fullscreen check
        // ------------------------------------------------------------------

        private void OnClockTick()
        {
            _tick++;
            var now = DateTime.Now;

            if (_mode == Mode.Expanded)
            {
                Media.UpdateProgress();
                Calendar.Refresh();
            }

            if (_mode != Mode.Collapsed)
            {
                if (IsCursorOverIsland() || IsBusy())
                    _lastInside = now;
                else if ((now - _lastInside).TotalSeconds > 1.5)
                    Collapse(); // safety net in case a MouseLeave was missed
            }

            if (_tick % 5 == 1)
            {
                _battery.Poll();
                BatteryText.Text = _battery.HasBattery
                    ? $"{(_battery.PluggedIn ? "⚡ " : "")}{_battery.Percent}%"
                    : "";
            }

            bool hide = _settings.HideInFullscreen && Native.IsForegroundFullscreen(this);
            if (hide && Island.Visibility == Visibility.Visible)
            {
                Collapse();
                Island.Visibility = Visibility.Hidden;
            }
            else if (!hide && Island.Visibility != Visibility.Visible)
            {
                Island.Visibility = Visibility.Visible;
            }

            if (!hide)
                Native.KeepOnTop(this);
        }

        // ------------------------------------------------------------------
        // Tabs and settings
        // ------------------------------------------------------------------

        private void NookTab_Click(object sender, RoutedEventArgs e) => ShowTab(Tab.Nook);
        private void TrayTab_Click(object sender, RoutedEventArgs e) => ShowTab(Tab.Tray);

        private void ShowTab(Tab tab)
        {
            _tab = tab;
            NookPanel.Visibility = tab == Tab.Nook ? Visibility.Visible : Visibility.Collapsed;
            Shelf.Visibility = tab == Tab.Tray ? Visibility.Visible : Visibility.Collapsed;

            var active = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            NookTabButton.Background = tab == Tab.Nook ? active : Brushes.Transparent;
            TrayTabButton.Background = tab == Tab.Tray ? active : Brushes.Transparent;
            NookTabButton.Opacity = tab == Tab.Nook ? 1 : 0.55;
            TrayTabButton.Opacity = tab == Tab.Tray ? 1 : 0.55;

            if (tab != Tab.Nook)
                _ = Mirror.StopAsync();
        }

        /// <summary>Mouse wheel scrolls the widget strip sideways, except over parts that scroll themselves.</summary>
        private void NookPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            for (var el = e.OriginalSource as DependencyObject; el != null && el != NookPanel;
                 el = el is Visual ? VisualTreeHelper.GetParent(el) : LogicalTreeHelper.GetParent(el))
            {
                if (el is FrameworkElement { Tag: "OwnWheel" })
                    return;
                if (el is ScrollViewer inner && (inner.ScrollableHeight > 0 || inner.ScrollableWidth > 0))
                    return;
            }
            NookPanel.ScrollToHorizontalOffset(NookPanel.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void Gear_Click(object sender, RoutedEventArgs e)
        {
            var menu = GearButton.ContextMenu;
            menu.PlacementTarget = GearButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private ContextMenu BuildSettingsMenu()
        {
            var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true };
            startup.Click += (_, _) => StartupManager.SetEnabled(startup.IsChecked);

            var fullscreen = new MenuItem { Header = "Hide when an app is fullscreen", IsCheckable = true };
            fullscreen.Click += (_, _) =>
            {
                _settings.HideInFullscreen = fullscreen.IsChecked;
                _settings.Save();
            };

            var folder = new MenuItem { Header = "Open data folder" };
            folder.Click += (_, _) => Process.Start(new ProcessStartInfo(AppPaths.Dir) { UseShellExecute = true });

            var quit = new MenuItem { Header = "Quit WindowsBar" };
            quit.Click += (_, _) => Application.Current.Shutdown();

            var menu = new ContextMenu();
            menu.Items.Add(startup);
            menu.Items.Add(fullscreen);
            menu.Items.Add(folder);
            menu.Items.Add(new Separator());
            menu.Items.Add(quit);

            // The tray icon can change these too, so read them fresh each time.
            menu.Opened += (_, _) =>
            {
                startup.IsChecked = StartupManager.IsEnabled;
                fullscreen.IsChecked = _settings.HideInFullscreen;
            };
            menu.Closed += (_, _) => _leaveTimer.Start();
            return menu;
        }

        // ------------------------------------------------------------------
        // Dragging files onto the island
        // ------------------------------------------------------------------

        private bool IsIncomingFileDrag(DragEventArgs e) =>
            !Shelf.IsDraggingOut && e.Data.GetDataPresent(DataFormats.FileDrop);

        private void Island_DragEnter(object sender, DragEventArgs e)
        {
            if (IsIncomingFileDrag(e))
                ShowDropTargets();
            Island_DragOver(sender, e);
        }

        private void Island_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!IsIncomingFileDrag(e))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            _leaveTimer.Stop();
            _lastInside = DateTime.Now;
            if (_mode != Mode.Drop)
                ShowDropTargets();

            var p = e.GetPosition(ShareZone);
            SetDropTarget(p.X >= 0 && p.Y >= 0 && p.X <= ShareZone.ActualWidth && p.Y <= ShareZone.ActualHeight);
            e.Effects = DragDropEffects.Copy;
        }

        private void SetDropTarget(bool share)
        {
            _overShareZone = share;
            var accent = (Brush)FindResource("AccentBrush");
            ShareZone.BorderBrush = share ? accent : Brushes.Transparent;
            TrayZoneOutline.Stroke = share ? (Brush)FindResource("FaintBrush") : accent;
        }

        private void Island_DragLeave(object sender, DragEventArgs e)
        {
            // Also fires when moving between child elements; the timer re-checks the real cursor.
            _leaveTimer.Stop();
            _leaveTimer.Start();
        }

        private void Island_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!IsIncomingFileDrag(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
                return;

            if (_overShareZone)
            {
                Collapse();
                ShareService.Share(this, paths);
                return;
            }

            foreach (var path in paths)
                _shelf.Add(path);
            ShowTab(Tab.Tray);
            Expand();
        }
    }
}
