using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WindowsBar.Services
{
    /// <summary>
    /// Follows whatever Windows shows in its own media flyout (Spotify, browsers, Media Player…)
    /// and lets the island control it.
    /// </summary>
    public sealed class MediaService
    {
        private readonly Dispatcher _ui;
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;
        private GlobalSystemMediaTransportControlsSessionTimelineProperties? _timeline;

        public MediaService(Dispatcher ui) => _ui = ui;

        /// <summary>Raised on the UI thread whenever the song, artwork or play state changes.</summary>
        public event Action? Changed;

        public bool HasSession => _session != null;
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public ImageSource? Artwork { get; private set; }
        public bool IsPlaying { get; private set; }

        public async Task InitAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += (_, _) => _ui.InvokeAsync(AttachCurrentSession);
                AttachCurrentSession();
            }
            catch (Exception ex)
            {
                Log.Error("Media controls unavailable", ex);
            }
        }

        private void AttachCurrentSession()
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }

            _session = _manager?.GetCurrentSession();

            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged += OnTimelineChanged;
            }

            _ = RefreshAsync();
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
            _ui.InvokeAsync(() => _ = RefreshAsync());

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
            _ui.InvokeAsync(() =>
            {
                ReadPlayback();
                Changed?.Invoke();
            });

        private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
            _ui.InvokeAsync(ReadTimeline);

        private async Task RefreshAsync()
        {
            var session = _session;
            if (session == null)
            {
                Title = Artist = "";
                Artwork = null;
                IsPlaying = false;
                _timeline = null;
                Changed?.Invoke();
                return;
            }

            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (session != _session)
                    return; // a newer session took over while we waited

                Title = props?.Title ?? "";
                Artist = props?.Artist ?? "";
                Artwork = props?.Thumbnail != null ? await LoadArtworkAsync(props.Thumbnail) : null;
                ReadPlayback();
                ReadTimeline();
            }
            catch (Exception ex)
            {
                Log.Error("Could not read media info", ex);
            }

            Changed?.Invoke();
        }

        private void ReadPlayback()
        {
            try
            {
                IsPlaying = _session?.GetPlaybackInfo()?.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch (Exception ex)
            {
                Log.Error("Could not read playback state", ex);
            }
        }

        private void ReadTimeline()
        {
            try
            {
                _timeline = _session?.GetTimelineProperties();
            }
            catch (Exception ex)
            {
                Log.Error("Could not read timeline", ex);
            }
        }

        /// <summary>
        /// Current position and length. Apps only report the position now and then,
        /// so while playing we add the time that passed since their last report.
        /// </summary>
        public (TimeSpan Position, TimeSpan Duration) GetProgress()
        {
            var t = _timeline;
            if (t == null)
                return (TimeSpan.Zero, TimeSpan.Zero);

            var duration = t.EndTime - t.StartTime;
            var position = t.Position - t.StartTime;
            if (IsPlaying && t.LastUpdatedTime > DateTimeOffset.MinValue)
                position += DateTimeOffset.Now - t.LastUpdatedTime;

            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration) position = duration;
            return (position, duration);
        }

        /// <summary>Jumps to a point in the song, if the playing app allows it.</summary>
        public void Seek(TimeSpan position)
        {
            var start = _timeline?.StartTime ?? TimeSpan.Zero;
            Run(s => s.TryChangePlaybackPositionAsync((start + position).Ticks).AsTask());
        }

        public void TogglePlayPause() => Run(s => s.TryTogglePlayPauseAsync().AsTask());
        public void Next() => Run(s => s.TrySkipNextAsync().AsTask());
        public void Previous() => Run(s => s.TrySkipPreviousAsync().AsTask());

        private void Run(Func<GlobalSystemMediaTransportControlsSession, Task> action)
        {
            if (_session == null)
                return;
            action(_session).ContinueWith(t => Log.Error("Media command failed", t.Exception),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private static async Task<ImageSource?> LoadArtworkAsync(IRandomAccessStreamReference reference)
        {
            try
            {
                using var winStream = await reference.OpenReadAsync();
                using var stream = winStream.AsStreamForRead();
                var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                memory.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 200;
                bmp.StreamSource = memory;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Error("Could not load artwork", ex);
                return null;
            }
        }
    }
}
