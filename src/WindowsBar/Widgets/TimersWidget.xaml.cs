using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowsBar.Services;

namespace WindowsBar.Widgets
{
    /// <summary>Pick a time on three little wheels, then run as many timers as you like.</summary>
    public partial class TimersWidget : UserControl
    {
        private static readonly int[] Max = { 23, 59, 59 };
        private static readonly string[] Units = { "h", "m", "s" };

        private readonly int[] _values = { 0, 5, 0 };
        private readonly (TextBlock Prev, TextBlock Current, TextBlock Next)[] _wheelText =
            new (TextBlock, TextBlock, TextBlock)[3];
        private TimerService? _timers;

        public TimersWidget()
        {
            InitializeComponent();
            BuildWheels();
        }

        public void Attach(TimerService timers)
        {
            _timers = timers;
            TimerList.ItemsSource = timers.Timers;
            timers.Timers.CollectionChanged += (_, _) => ShowList(timers.Timers.Count > 0);
            ShowList(timers.Timers.Count > 0);
        }

        private void BuildWheels()
        {
            for (int i = 0; i < 3; i++)
            {
                if (i > 0)
                {
                    Wheels.Children.Add(new TextBlock
                    {
                        Text = ":",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 2),
                        Foreground = (Brush)FindResource("SubtleBrush"),
                    });
                }

                int index = i;
                var prev = Faded();
                var next = Faded();
                var current = new TextBlock
                {
                    FontSize = 19,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var unit = new TextBlock
                {
                    Text = Units[i],
                    FontSize = 9,
                    Margin = new Thickness(1, 0, 0, 3),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Foreground = (Brush)FindResource("AccentBrush"),
                };
                var currentRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                currentRow.Children.Add(current);
                currentRow.Children.Add(unit);

                var column = new StackPanel
                {
                    Width = 50,
                    Background = Brushes.Transparent,
                    Cursor = Cursors.SizeNS,
                    ToolTip = "Scroll to change",
                    Tag = "OwnWheel", // tells the island not to scroll sideways here
                };
                column.Children.Add(prev);
                column.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = (Brush)FindResource("CardBrush"),
                    Padding = new Thickness(0, 2, 0, 2),
                    Child = currentRow,
                });
                column.Children.Add(next);

                prev.MouseLeftButtonUp += (_, _) => Step(index, -1);
                next.MouseLeftButtonUp += (_, _) => Step(index, +1);
                column.MouseWheel += (_, e) =>
                {
                    Step(index, e.Delta > 0 ? -1 : +1);
                    e.Handled = true;
                };

                _wheelText[i] = (prev, current, next);
                Wheels.Children.Add(column);
            }
            UpdateWheels();
        }

        private TextBlock Faded() => new()
        {
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("DimBrush"),
            Cursor = Cursors.Hand,
        };

        private void Step(int index, int by)
        {
            int range = Max[index] + 1;
            _values[index] = ((_values[index] + by) % range + range) % range;
            UpdateWheels();
        }

        private void UpdateWheels()
        {
            for (int i = 0; i < 3; i++)
            {
                int range = Max[i] + 1;
                _wheelText[i].Prev.Text = $"{(_values[i] - 1 + range) % range:00}{Units[i]}";
                _wheelText[i].Current.Text = $"{_values[i]:00}";
                _wheelText[i].Next.Text = $"{(_values[i] + 1) % range:00}{Units[i]}";
            }
            StartButton.IsEnabled = Picked > TimeSpan.Zero;
        }

        private TimeSpan Picked => new(_values[0], _values[1], _values[2]);

        private void ShowList(bool list)
        {
            PickerView.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
            RunningView.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            BackButton.Visibility = _timers?.Timers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Start_Click(object sender, RoutedEventArgs e) => _timers?.Add(Picked);
        private void Preset1_Click(object sender, RoutedEventArgs e) => _timers?.Add(TimeSpan.FromMinutes(1));
        private void Preset5_Click(object sender, RoutedEventArgs e) => _timers?.Add(TimeSpan.FromMinutes(5));

        private void AddTimer_Click(object sender, RoutedEventArgs e) => ShowList(false);
        private void Back_Click(object sender, RoutedEventArgs e) => ShowList(true);

        private void ToggleTimer_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is CountdownTimer timer)
                _timers?.Toggle(timer);
        }

        private void RemoveTimer_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is CountdownTimer timer)
                _timers?.Remove(timer);
        }
    }
}
