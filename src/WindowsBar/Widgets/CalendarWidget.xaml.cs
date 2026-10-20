using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowsBar.Services;

namespace WindowsBar.Widgets
{
    /// <summary>Month name, a strip of days to pick from, and that day's events.</summary>
    public partial class CalendarWidget : UserControl
    {
        private const double DayWidth = 26;

        private readonly CalendarService _calendar = new();
        private DateTime _builtFor;
        private DateTime _selected = DateTime.Today;

        public CalendarWidget()
        {
            InitializeComponent();
        }

        /// <summary>Called when the island opens and once a second; rebuilds only when the day changes.</summary>
        public void Refresh()
        {
            var today = DateTime.Today;
            if (_builtFor == today)
                return;
            _builtFor = today;
            _selected = today;
            BuildStrip();
            _ = LoadEventsAsync();
        }

        private void BuildStrip()
        {
            var culture = CultureInfo.CurrentCulture;
            var today = DateTime.Today;
            MonthText.Text = culture.DateTimeFormat.GetAbbreviatedMonthName(_selected.Month).TrimEnd('.');
            DayStrip.Children.Clear();

            int days = DateTime.DaysInMonth(_selected.Year, _selected.Month);
            for (int d = 1; d <= days; d++)
            {
                var date = new DateTime(_selected.Year, _selected.Month, d);
                bool isToday = date == today;
                bool isSelected = date == _selected;
                bool weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

                var accent = (Brush)FindResource("AccentBrush");
                var cell = new StackPanel { Width = DayWidth, Cursor = Cursors.Hand, Background = Brushes.Transparent };
                cell.Children.Add(new TextBlock
                {
                    Text = isSelected
                        ? culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek).ToUpper(culture)
                        : culture.DateTimeFormat.GetShortestDayName(date.DayOfWeek).Substring(0, 1).ToUpper(culture),
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = isSelected ? Brushes.White : (Brush)FindResource("DimBrush"),
                });
                cell.Children.Add(new TextBlock
                {
                    Text = d.ToString("00", culture),
                    FontSize = isSelected ? 16 : 12,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = isSelected || isToday ? accent
                        : weekend ? new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x6B, 0x6B))
                        : (Brush)FindResource("SubtleBrush"),
                });
                cell.MouseLeftButtonUp += (_, _) => Select(date);
                DayStrip.Children.Add(cell);
            }

            // Keep the chosen day roughly in the middle.
            Dispatcher.InvokeAsync(() =>
                DayScroller.ScrollToHorizontalOffset((_selected.Day - 1) * DayWidth - DayScroller.ViewportWidth / 2 + DayWidth / 2),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Select(DateTime date)
        {
            _selected = date;
            BuildStrip();
            _ = LoadEventsAsync();
        }

        private async System.Threading.Tasks.Task LoadEventsAsync()
        {
            var day = _selected;
            var events = await _calendar.GetEventsAsync(day);
            if (day != _selected)
                return;

            EventList.Children.Clear();
            foreach (var ev in events.Take(3))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new Border { CornerRadius = new CornerRadius(1.5), Background = (Brush)FindResource("AccentBrush") });
                var text = new StackPanel { Margin = new Thickness(7, 0, 0, 0) };
                text.Children.Add(new TextBlock { Text = ev.Title, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock
                {
                    Text = ev.AllDay ? "All day" : ev.Start.LocalDateTime.ToString("t", CultureInfo.CurrentCulture),
                    FontSize = 10,
                    Foreground = (Brush)FindResource("SubtleBrush"),
                });
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                EventList.Children.Add(row);
            }

            EmptyState.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = day == DateTime.Today
                ? "Nothing for today"
                : $"Nothing on {day.ToString("ddd d", CultureInfo.CurrentCulture)}";
        }

        private void DayScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            DayScroller.ScrollToHorizontalOffset(DayScroller.HorizontalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
    }
}
