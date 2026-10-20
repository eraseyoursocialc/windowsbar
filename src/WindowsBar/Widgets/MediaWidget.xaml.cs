using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsBar.Services;

namespace WindowsBar.Widgets
{
    /// <summary>Now playing: artwork, title, controls and a seek bar.</summary>
    public partial class MediaWidget : UserControl
    {
        private MediaService? _media;
        private bool _seeking;

        public MediaWidget()
        {
            InitializeComponent();
        }

        public void Attach(MediaService media)
        {
            _media = media;
            Refresh();
        }

        public void Refresh()
        {
            if (_media == null)
                return;

            if (!_media.HasSession || string.IsNullOrEmpty(_media.Title))
            {
                TitleText.Text = "Nothing playing";
                ArtistText.Text = "Play music in any app";
                ArtImage.Source = null;
                Buttons.IsEnabled = _media.HasSession;
            }
            else
            {
                TitleText.Text = _media.Title;
                ArtistText.Text = _media.Artist;
                ArtImage.Source = _media.Artwork;
                Buttons.IsEnabled = true;
            }
            PlayPauseButton.Content = _media.IsPlaying ? "" : "";
            UpdateProgress();
        }

        /// <summary>Called every second while the island is open.</summary>
        public void UpdateProgress()
        {
            if (_media == null || _seeking)
                return;

            var (position, duration) = _media.GetProgress();
            if (duration <= TimeSpan.Zero || string.IsNullOrEmpty(_media.Title))
            {
                ProgressArea.Visibility = Visibility.Hidden;
                return;
            }

            ProgressArea.Visibility = Visibility.Visible;
            SeekBar.Maximum = duration.TotalSeconds;
            SeekBar.Value = position.TotalSeconds;
            PositionText.Text = Helpers.Clock(position);
            DurationText.Text = Helpers.Clock(duration);
        }

        private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _seeking = true;

        private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_seeking)
                return;
            _seeking = false;
            _media?.Seek(TimeSpan.FromSeconds(SeekBar.Value));
        }

        private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_seeking)
                PositionText.Text = Helpers.Clock(TimeSpan.FromSeconds(e.NewValue));
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) => _media?.TogglePlayPause();
        private void Next_Click(object sender, RoutedEventArgs e) => _media?.Next();
        private void Previous_Click(object sender, RoutedEventArgs e) => _media?.Previous();
    }
}
