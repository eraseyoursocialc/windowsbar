using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsBar.Services;

namespace WindowsBar.Widgets
{
    /// <summary>The Tray tab: files you dropped on the island, ready to drag out again.</summary>
    public partial class ShelfView : UserControl
    {
        private FileShelf? _shelf;
        private ShelfItem? _pressed;
        private Point _pressPoint;

        public ShelfView()
        {
            InitializeComponent();
        }

        /// <summary>True while a file is being dragged out of the tray.</summary>
        public bool IsDraggingOut { get; private set; }

        /// <summary>Raised when a drag out of the tray finishes.</summary>
        public event Action? DragOutFinished;

        public void Attach(FileShelf shelf)
        {
            _shelf = shelf;
            Items.ItemsSource = shelf.Items;
            shelf.Items.CollectionChanged += (_, _) => UpdateEmpty();
            UpdateEmpty();
        }

        private void UpdateEmpty()
        {
            bool empty = _shelf == null || _shelf.Items.Count == 0;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            Scroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not ShelfItem item)
                return;

            if (e.ClickCount == 2)
            {
                _pressed = null;
                try
                {
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not open {item.Path}", ex);
                }
                return;
            }

            _pressed = item;
            _pressPoint = e.GetPosition(this);
        }

        private void Item_MouseMove(object sender, MouseEventArgs e)
        {
            if (_pressed == null || _shelf == null || e.LeftButton != MouseButtonState.Pressed)
                return;
            var moved = e.GetPosition(this) - _pressPoint;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var item = _pressed;
            _pressed = null;
            if (!item.Exists)
            {
                _shelf.Remove(item);
                return;
            }

            IsDraggingOut = true;
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
                DragDrop.DoDragDrop((DependencyObject)sender, data,
                    DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            }
            finally
            {
                IsDraggingOut = false;
            }

            if (!item.Exists)
                _shelf.Remove(item); // it was moved somewhere else
            DragOutFinished?.Invoke();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is ShelfItem item)
                _shelf?.Remove(item);
            e.Handled = true;
        }

        private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        }
    }
}
