using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WindowsBar.Helper
{
    /// <summary>
    /// Follows whatever Windows shows in its own media flyout (Spotify, browsers, Media Player…)
    /// and passes play/pause/skip/seek back to it.
    /// </summary>
    internal sealed class MediaWatcher
    {
        private readonly Control _ui;
        private readonly Output _out;
        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private GlobalSystemMediaTransportControlsSession _session;
        private string _artKey;

        public MediaWatcher(Control ui, Output output)
        {
            _ui = ui;
            _out = output;
        }

        public async void Start()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += (s, e) => OnUi(AttachCurrentSession);
                AttachCurrentSession();
            }
            catch (Exception ex)
            {
                _out.Error("Media controls unavailable: " + ex.Message);
            }
        }

        private void OnUi(Action action) => _ui.BeginInvoke(action);

        private void AttachCurrentSession()
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }

            _session = _manager?.GetCurrentSession();

            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackChanged;
                _session.TimelinePropertiesChanged += OnTimelineChanged;
            }

            Refresh(withArtwork: true);
        }

        private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
            OnUi(() => Refresh(withArtwork: true));

        private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
            OnUi(() => Refresh(withArtwork: false));

        private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
            OnUi(() => Refresh(withArtwork: false));

        private async void Refresh(bool withArtwork)
        {
            var session = _session;
            var message = new MediaMessage();

            if (session == null)
            {
                _out.Send(message);
                SendArt(null, "");
                return;
            }

            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (session != _session)
                    return; // a newer session took over while we waited

                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();

                message.HasSession = true;
                message.Title = props?.Title ?? "";
                message.Artist = props?.Artist ?? "";
                message.Playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                message.CanSeek = playback?.Controls?.IsPlaybackPositionEnabled ?? false;
                if (timeline != null)
                {
                    message.Position = (timeline.Position - timeline.StartTime).TotalSeconds;
                    message.Duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
                    message.Updated = timeline.LastUpdatedTime.ToUnixTimeMilliseconds();
                }
                _out.Send(message);

                var key = message.Title + "\u0001" + message.Artist;
                if (withArtwork && key != _artKey)
                    SendArt(key, await ReadArtworkAsync(props?.Thumbnail));
            }
            catch (Exception ex)
            {
                _out.Error("Could not read media info: " + ex.Message);
            }
        }

        private void SendArt(string key, string dataUrl)
        {
            _artKey = key;
            _out.Send(new ArtMessage { Data = dataUrl ?? "" });
        }

        private static async Task<string> ReadArtworkAsync(IRandomAccessStreamReference reference)
        {
            if (reference == null)
                return "";
            try
            {
                using (var winStream = await reference.OpenReadAsync())
                using (var stream = winStream.AsStreamForRead())
                using (var memory = new MemoryStream())
                {
                    await stream.CopyToAsync(memory);
                    var type = string.IsNullOrEmpty(winStream.ContentType) ? "image/png" : winStream.ContentType;
                    return $"data:{type};base64,{Convert.ToBase64String(memory.ToArray())}";
                }
            }
            catch
            {
                return "";
            }
        }

        public void TogglePlayPause() => Run(s => s.TryTogglePlayPauseAsync().AsTask());
        public void Next() => Run(s => s.TrySkipNextAsync().AsTask());
        public void Previous() => Run(s => s.TrySkipPreviousAsync().AsTask());

        public void Seek(double seconds)
        {
            var start = _session?.GetTimelineProperties()?.StartTime ?? TimeSpan.Zero;
            Run(s => s.TryChangePlaybackPositionAsync((start + TimeSpan.FromSeconds(seconds)).Ticks).AsTask());
        }

        private async void Run(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
        {
            var session = _session;
            if (session == null)
                return;
            try
            {
                await action(session);
            }
            catch (Exception ex)
            {
                _out.Error("Media command failed: " + ex.Message);
            }
        }
    }
}
