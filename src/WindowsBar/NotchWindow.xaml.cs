using System;
using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WindowsBar.Services;
using WindowsBar.Widgets;

namespace WindowsBar
{
    public partial class NotchWindow : Window
    {
        /// <summary>
        /// Hidden: nothing on screen. Peek: a small tab slid down because the mouse touched the
        /// top-middle of the screen. Expanded: fully open. Drop: showing drop targets for a file drag.
        /// </summary>
        private enum Mode { Hidden, Peek, Expanded, Drop }
        private enum Tab { Nook, Tray }

        // Sizes in device-independent pixels.
        private const double PeekWidth = 180, PeekHeight = 22;
        private const double PeekActiveWidth = 240;
        private const double PeekGrow = 26;            // how much the tab swells while you hold the mouse there
        private const double ToastWidth = 300, ToastHeight = 30;
        private const double ExpandedHeight = 190;
        private const double DropWidth = 540, DropHeight = 152;

        // How the top-middle "hot corner" behaves.
        private const double HotZoneHalfWidth = 170;   // either side of the screen's center
        private static readonly TimeSpan PeekDwell = TimeSpan.FromSeconds(1);
        private const int PeekLeaveMs = 250, OpenLeaveMs = 450;

        private readonly Settings _settings;
        private readonly MediaService _media;
        private readonly CameraService _camera;
        private readonly TimerService _timers = new();
        private readonly FileShelf _shelf = new();
        private readonly BatteryWatcher _battery = new();

        private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly DispatcherTimer _toastTimer = new();
        private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

        private Mode _mode = Mode.Hidden;
        private Tab _tab = Tab.Nook;
        private bool _settingsOpen;
        private double _expandedWidth = 1200;
        private bool _overShareZone;
        private bool _blockedByFullscreen;
        private DateTime _peekStarted;
        private DateTime? _outsideSince;
        private int _tick;

        private readonly bool _pinOpen;
        private readonly bool _pinPeek;
        private readonly bool _pinSettings;

        private string? _toastText;
        private string? _toastGlyph;

        private readonly ScaleTransform[] _bars = new ScaleTransform[4];
        private bool _barsRunning;

        /// <param name="startState">"open", "settings" or "peek" pins the island in that state (used for screenshots).</param>
        public NotchWindow(Settings settings, string? startState = null)
        {
            InitializeComponent();
            _settings = settings;
            _pinOpen = startState is "open" or "settings";
            _pinPeek = startState == "peek";
            _pinSettings = startState == "settings";
            _media = new MediaService(Dispatcher);
            _camera = new CameraService(Dispatcher);

            _shelf.Load();
            Shelf.Attach(_shelf);
            _shelf.Items.CollectionChanged += (_, _) => RefreshCompact();

            Media.Attach(_media);
            Mirror.Attach(_camera);
            Timers.Attach(_timers);

            BuildEqualizer();

            _poll.Tick += (_, _) => OnPoll();
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
                ShowToast("", $"Timer done  ·  {CountdownTimer.Format(timer.Length)}", 6);
            };

            Loaded += OnLoaded;
            SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(PlaceAtTop);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Native.MakeToolWindow(this);
            Native.SetNoActivate(this, true);
            PlaceAtTop();

            ShowTab(Tab.Nook, animate: false);
            OnClockTick();
            RefreshCompact();
            _clock.Start();
            _poll.Start();

            if (_pinOpen)
                Expand();
            if (_pinSettings)
                Gear_Click(GearButton, new RoutedEventArgs());
            else if (_pinPeek)
                StartPeek();

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
        // Following the mouse
        // ------------------------------------------------------------------

        /// <summary>
        /// Runs ~30 times a second. The island is invisible while hidden, so it can't get
        /// mouse events; instead we watch where the cursor is.
        /// </summary>
        private void OnPoll()
        {
            if (!Native.GetCursorPos(out var cursor))
                return;
            var now = DateTime.Now;

            if (_blockedByFullscreen && !_pinOpen)
            {
                if (_mode != Mode.Hidden)
                    Retract();
                return;
            }

            bool inHotZone = IsInHotZone(cursor);
            bool overIsland = IsCursorOverIsland(cursor, 8);

            switch (_mode)
            {
                case Mode.Hidden:
                    if (inHotZone)
                        StartPeek();
                    break;

                case Mode.Peek:
                    if (inHotZone || overIsland || _pinPeek)
                    {
                        _outsideSince = null;
                        if (now - _peekStarted >= PeekDwell && !_pinPeek)
                            Expand();
                    }
                    else if (AwayFor(now, PeekLeaveMs))
                    {
                        Retract();
                    }
                    break;

                default:
                    if (overIsland || IsBusy())
                        _outsideSince = null;
                    else if (AwayFor(now, OpenLeaveMs))
                        Retract();
                    break;
            }
        }

        private bool AwayFor(DateTime now, int ms)
        {
            _outsideSince ??= now;
            return (now - _outsideSince.Value).TotalMilliseconds >= ms;
        }

        /// <summary>The very top edge of the screen, around the middle.</summary>
        private bool IsInHotZone(Native.POINT cursor)
        {
            var topCenter = PointToScreen(new Point(ActualWidth / 2, 0));
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return cursor.Y <= topCenter.Y + 2 * scale &&
                   Math.Abs(cursor.X - topCenter.X) <= HotZoneHalfWidth * scale;
        }

        private bool IsCursorOverIsland(Native.POINT cursor, double margin)
        {
            if (Island.ActualHeight < 1)
                return false;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var topLeft = Island.PointToScreen(new Point(0, 0));
            var bottomRight = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
            double m = margin * scale;
            return cursor.X >= topLeft.X - m && cursor.X <= bottomRight.X + m &&
                   cursor.Y >= topLeft.Y - m && cursor.Y <= bottomRight.Y + m;
        }

        private bool IsBusy() =>
            _pinOpen ||
            Shelf.IsDraggingOut ||
            (IsActive && Island.IsKeyboardFocusWithin); // typing a note or task

        private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Clicking the tab opens it straight away.
            if (_mode == Mode.Peek)
                Expand();
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

        // ------------------------------------------------------------------
        // States
        // ------------------------------------------------------------------

        private void StartPeek()
        {
            _mode = Mode.Peek;
            _peekStarted = DateTime.Now;
            _outsideSince = null;
            RefreshCompact();

            // Slide down to a small tab, then keep swelling gently until it opens.
            double width = CompactWidth();
            Island.CornerRadius = new CornerRadius(0, 0, 13, 13);
            Island.BeginAnimation(WidthProperty, PeekFrames(width, width + PeekGrow));
            Island.BeginAnimation(HeightProperty, PeekFrames(PeekHeight, PeekHeight + 6));
            Island.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
            Reveal(CompactView, true, 160, 60);
        }

        private static DoubleAnimationUsingKeyFrames PeekFrames(double settle, double swell)
        {
            var frames = new DoubleAnimationUsingKeyFrames();
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(settle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),
                new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(swell, KeyTime.FromTimeSpan(PeekDwell),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
            return frames;
        }

        private void Expand()
        {
            if (_mode == Mode.Expanded)
                return;
            bool fromDrop = _mode == Mode.Drop;
            _mode = Mode.Expanded;
            _outsideSince = null;

            if (_settingsOpen)
                CloseSettings(animate: false);

            Island.CornerRadius = new CornerRadius(0, 0, 32, 32);
            AnimateIsland(_expandedWidth, ExpandedHeight, 560, new BackEase { Amplitude = 0.16, EasingMode = EasingMode.EaseOut });
            Island.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            Reveal(CompactView, false, 90, 0);
            Materialize(DropView, false, 0);
            Materialize(ExpandedView, true, fromDrop ? 60 : 130);
            if (_tab == Tab.Nook)
                StaggerIn(NookStrip, 170);

            Media.Refresh();
            Calendar.Refresh();
        }

        private void ShowDropTargets()
        {
            if (_mode == Mode.Drop)
                return;
            _mode = Mode.Drop;
            _outsideSince = null;

            TrayZoneCount.Text = _shelf.Items.Count switch
            {
                0 => "Keep files handy",
                1 => "1 file",
                var n => $"{n} files",
            };
            SetDropTarget(false);

            Island.CornerRadius = new CornerRadius(0, 0, 30, 30);
            AnimateIsland(DropWidth, DropHeight, 460, new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut });
            Island.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            Reveal(CompactView, false, 90, 0);
            Materialize(ExpandedView, false, 0);
            Materialize(DropView, true, 110);
        }

        /// <summary>Slides everything back up out of sight (or down to a notice, if one is showing).</summary>
        private void Retract()
        {
            if (_mode == Mode.Hidden)
                return;
            _mode = Mode.Hidden;
            _outsideSince = null;

            _ = Mirror.StopAsync();
            Notes.Save();
            Keyboard.ClearFocus();
            Native.SetNoActivate(this, true);

            Materialize(ExpandedView, false, 0);
            Materialize(DropView, false, 0);
            RefreshCompact();
        }

        private void AnimateIsland(double width, double height, int ms, IEasingFunction ease)
        {
            var duration = TimeSpan.FromMilliseconds(ms);
            Island.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
            Island.BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
        }

        // ------------------------------------------------------------------
        // Content animations
        // ------------------------------------------------------------------

        private static readonly IEasingFunction SoftOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        private static void Reveal(UIElement element, bool visible, int ms, int delayMs)
        {
            element.IsHitTestVisible = visible;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = SoftOut,
            });
        }

        /// <summary>Content fades in out of a soft blur while drifting into place.</summary>
        private static void Materialize(FrameworkElement element, bool show, int delayMs)
        {
            element.IsHitTestVisible = show;
            var slide = Translate(element);

            if (show)
            {
                var begin = TimeSpan.FromMilliseconds(delayMs);
                var duration = TimeSpan.FromMilliseconds(380);
                var blur = new BlurEffect { Radius = 14, RenderingBias = RenderingBias.Performance };
                element.Effect = blur;

                var unblur = new DoubleAnimation(14, 0, duration) { BeginTime = begin, EasingFunction = SoftOut };
                // Drop the effect once sharp again so text renders crisply.
                unblur.Completed += (_, _) => { if (element.Effect == blur) element.Effect = null; };
                blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { BeginTime = begin, EasingFunction = SoftOut });
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, duration) { BeginTime = begin, EasingFunction = SoftOut });
            }
            else
            {
                element.Effect = null;
                element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)));
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, TimeSpan.FromMilliseconds(160)));
            }
        }

        /// <summary>Each widget slides in a beat after the one before it.</summary>
        private static void StaggerIn(Panel panel, int delayMs)
        {
            for (int i = 0; i < panel.Children.Count; i++)
            {
                var child = panel.Children[i];
                var begin = TimeSpan.FromMilliseconds(delayMs + i * 28);
                var duration = TimeSpan.FromMilliseconds(420);
                child.Opacity = 0;
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { BeginTime = begin, EasingFunction = SoftOut });
                Translate(child).BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(18, 0, duration) { BeginTime = begin, EasingFunction = SoftOut });
            }
        }

        /// <summary>A panel switched in from a tab or the gear: quick fade and lift.</summary>
        private static void SwitchIn(UIElement element)
        {
            var duration = TimeSpan.FromMilliseconds(260);
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = SoftOut });
            Translate(element).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = SoftOut });
        }

        private static TranslateTransform Translate(UIElement element)
        {
            if (element.RenderTransform is not TranslateTransform t)
            {
                t = new TranslateTransform();
                element.RenderTransform = t;
            }
            return t;
        }

        // ------------------------------------------------------------------
        // The small tab: peeking and notices
        // ------------------------------------------------------------------

        /// <summary>
        /// Fills the small tab, most important first: a notice, a running timer,
        /// music, how many files are in the tray, or just a grab bar.
        /// While hidden it only shows itself for a notice.
        /// </summary>
        private void RefreshCompact()
        {
            CompactArt.Visibility = Visibility.Collapsed;
            CompactGlyph.Visibility = Visibility.Collapsed;
            CompactCount.Visibility = Visibility.Collapsed;
            PeekHandle.Visibility = Visibility.Collapsed;
            CompactText.Text = "";
            bool playing = _media.HasSession && _media.IsPlaying;
            var timer = _timers.Soonest;
            SetEqualizer(false);

            if (_toastText != null)
            {
                CompactGlyph.Text = _toastGlyph ?? "";
                CompactGlyph.Visibility = Visibility.Visible;
                CompactText.Text = _toastText;
            }
            else if (timer != null)
            {
                CompactGlyph.Text = "";
                CompactGlyph.Visibility = Visibility.Visible;
                CompactText.Text = timer.RemainingText;
                SetEqualizer(playing);
            }
            else if (playing)
            {
                CompactArtImage.Source = _media.Artwork;
                CompactArt.Visibility = Visibility.Visible;
                SetEqualizer(true);
            }
            else if (_shelf.Items.Count > 0)
            {
                CompactGlyph.Text = "";
                CompactGlyph.Visibility = Visibility.Visible;
                CompactCountText.Text = _shelf.Items.Count.ToString();
                CompactCount.Visibility = Visibility.Visible;
            }
            else
            {
                PeekHandle.Visibility = Visibility.Visible;
            }

            if (_mode != Mode.Hidden)
                return;

            if (_toastText != null)
            {
                // A notice drops down on its own, then goes away again.
                Island.CornerRadius = new CornerRadius(0, 0, ToastHeight / 2, ToastHeight / 2);
                AnimateIsland(ToastWidth, ToastHeight, 420, new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut });
                Island.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
                Reveal(CompactView, true, 200, 120);
            }
            else
            {
                Reveal(CompactView, false, 100, 0);
                AnimateIsland(PeekWidth, 0, 320, new CubicEase { EasingMode = EasingMode.EaseInOut });
                Island.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
                {
                    BeginTime = TimeSpan.FromMilliseconds(200),
                });
            }
        }

        private double CompactWidth() =>
            PeekHandle.Visibility == Visibility.Visible ? PeekWidth : PeekActiveWidth;

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
            // Only touch the tab when the shown time actually changes.
            if (_mode is Mode.Expanded or Mode.Drop || _toastText != null)
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
                    Height = 12,
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

            if (_mode == Mode.Expanded)
            {
                Media.UpdateProgress();
                Calendar.Refresh();
            }

            if (_tick % 5 == 1)
            {
                _battery.Poll();
                BatteryText.Text = _battery.HasBattery
                    ? $"{(_battery.PluggedIn ? "⚡ " : "")}{_battery.Percent}%"
                    : "";
            }

            _blockedByFullscreen = _settings.HideInFullscreen && Native.IsForegroundFullscreen(this);
            if (!_blockedByFullscreen)
                Native.KeepOnTop(this);
        }

        // ------------------------------------------------------------------
        // Tabs and settings
        // ------------------------------------------------------------------

        private void NookTab_Click(object sender, RoutedEventArgs e) => ShowTab(Tab.Nook);
        private void TrayTab_Click(object sender, RoutedEventArgs e) => ShowTab(Tab.Tray);

        private void ShowTab(Tab tab, bool animate = true)
        {
            bool changed = tab != _tab || _settingsOpen;
            _tab = tab;
            _settingsOpen = false;
            GearButton.Content = "";
            GearButton.ToolTip = "Settings";

            NookPanel.Visibility = tab == Tab.Nook ? Visibility.Visible : Visibility.Collapsed;
            Shelf.Visibility = tab == Tab.Tray ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            HighlightTabs(tab);

            if (tab != Tab.Nook)
                _ = Mirror.StopAsync();
            if (animate && changed)
            {
                if (tab == Tab.Nook)
                    StaggerIn(NookStrip, 0);
                else
                    SwitchIn(Shelf);
            }
        }

        private void HighlightTabs(Tab? active)
        {
            var on = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            NookTabButton.Background = active == Tab.Nook ? on : Brushes.Transparent;
            TrayTabButton.Background = active == Tab.Tray ? on : Brushes.Transparent;
            NookTabButton.Opacity = active == Tab.Nook ? 1 : 0.55;
            TrayTabButton.Opacity = active == Tab.Tray ? 1 : 0.55;
        }

        private void Gear_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsOpen)
            {
                CloseSettings(animate: true);
                return;
            }

            _settingsOpen = true;
            _ = Mirror.StopAsync();
            StartupToggle.IsChecked = StartupManager.IsEnabled;
            FullscreenToggle.IsChecked = _settings.HideInFullscreen;

            NookPanel.Visibility = Visibility.Collapsed;
            Shelf.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
            HighlightTabs(null);
            GearButton.Content = "";
            GearButton.ToolTip = "Close settings";
            SwitchIn(SettingsPanel);
        }

        private void CloseSettings(bool animate)
        {
            _settingsOpen = false;
            ShowTab(_tab, animate);
        }

        private void StartupToggle_Click(object sender, RoutedEventArgs e) =>
            StartupManager.SetEnabled(StartupToggle.IsChecked == true);

        private void FullscreenToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.HideInFullscreen = FullscreenToggle.IsChecked == true;
            _settings.Save();
        }

        private void DataFolder_Click(object sender, RoutedEventArgs e) =>
            Process.Start(new ProcessStartInfo(AppPaths.Dir) { UseShellExecute = true });

        private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

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

            _outsideSince = null;
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

        private void Island_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (!IsIncomingFileDrag(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
                return;

            if (_overShareZone)
            {
                Retract();
                ShareService.Share(this, paths);
                return;
            }

            foreach (var path in paths)
                _shelf.Add(path);
            ShowTab(Tab.Tray, animate: false);
            Expand();
        }
    }
}
