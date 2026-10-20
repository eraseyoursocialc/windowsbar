using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsBar.Services;

namespace WindowsBar.Widgets
{
    /// <summary>A small to-do list: type and press Enter, tick the circle when done.</summary>
    public partial class TasksWidget : UserControl
    {
        private readonly TaskStore _tasks = new();

        public TasksWidget()
        {
            InitializeComponent();
            _tasks.Load();
            TaskList.ItemsSource = _tasks.Items;
        }

        private void NewTask_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;
            _tasks.Add(NewTask.Text);
            NewTask.Clear();
            e.Handled = true;
        }

        private void NewTask_TextChanged(object sender, TextChangedEventArgs e) =>
            Placeholder.Visibility = NewTask.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void Check_Click(object sender, MouseButtonEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is TaskItem item)
                _tasks.Toggle(item);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is TaskItem item)
                _tasks.Remove(item);
        }
    }
}
