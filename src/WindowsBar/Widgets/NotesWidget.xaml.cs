using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsBar.Services;

namespace WindowsBar.Widgets
{
    /// <summary>Three small pages of rich-text notes, saved as you type.</summary>
    public partial class NotesWidget : UserControl
    {
        private static readonly string[] PageShapes = { "◆", "▲", "●" }; // ◆ ▲ ●
        private static readonly Color[] TextColors =
        {
            Color.FromRgb(0x0A, 0x84, 0xFF), Colors.White, Color.FromRgb(0xFF, 0xD6, 0x0A),
            Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0x30, 0xD1, 0x58),
        };
        private static readonly double[] TextSizes = { 12, 15, 19 };

        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
        private readonly Button[] _pageButtons = new Button[PageShapes.Length];
        private int _page;
        private int _colorIndex;
        private int _sizeIndex;
        private bool _loading;

        public NotesWidget()
        {
            InitializeComponent();
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };

            for (int i = 0; i < PageShapes.Length; i++)
            {
                int page = i;
                var button = new Button
                {
                    Style = (Style)FindResource("IconButton"),
                    Content = PageShapes[i],
                    FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                    FontSize = 13,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(0, 2, 0, 2),
                    ToolTip = $"Note {i + 1}",
                };
                button.Click += (_, _) => SwitchTo(page);
                _pageButtons[i] = button;
                PageButtons.Children.Add(button);
            }

            Load(0);
        }

        private static string PagePath(int page) => Path.Combine(AppPaths.Dir, $"note-{page + 1}.rtf");

        private void SwitchTo(int page)
        {
            if (page == _page)
                return;
            Save();
            Load(page);
        }

        private void Load(int page)
        {
            _page = page;
            _loading = true;
            try
            {
                Editor.Document.Blocks.Clear();
                var path = PagePath(page);
                if (File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Load(stream, DataFormats.Rtf);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Could not load note {page + 1}", ex);
            }
            finally
            {
                _loading = false;
            }

            for (int i = 0; i < _pageButtons.Length; i++)
                _pageButtons[i].Opacity = i == page ? 1 : 0.35;
            UpdatePlaceholder();
        }

        public void Save()
        {
            _saveTimer.Stop();
            try
            {
                using var stream = File.Create(PagePath(_page));
                new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Save(stream, DataFormats.Rtf);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not save note {_page + 1}", ex);
            }
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaceholder();
            if (_loading)
                return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void UpdatePlaceholder()
        {
            var text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
            Placeholder.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
        }

        // Formatting applies to the selection, or to what you type next if nothing is selected.

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            _colorIndex = (_colorIndex + 1) % TextColors.Length;
            var brush = new SolidColorBrush(TextColors[_colorIndex]);
            ColorSwatch.Background = brush;
            Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            Editor.Focus();
        }

        private void Size_Click(object sender, RoutedEventArgs e)
        {
            _sizeIndex = (_sizeIndex + 1) % TextSizes.Length;
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, TextSizes[_sizeIndex]);
            Editor.Focus();
        }

        private void Bold_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Editor);
        private void Italic_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Editor);
        private void Underline_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleUnderline.Execute(null, Editor);
    }
}
