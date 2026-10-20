using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace WindowsBar.Services
{
    /// <summary>Streams the webcam into a bitmap for the Mirror widget.</summary>
    public sealed class CameraService
    {
        private readonly Dispatcher _ui;
        private MediaCapture? _capture;
        private MediaFrameReader? _reader;
        private WriteableBitmap? _bitmap;
        private int _framePending;
        private bool _starting;

        public CameraService(Dispatcher ui) => _ui = ui;

        /// <summary>Raised on the UI thread with each new frame.</summary>
        public event Action<ImageSource>? FrameReady;

        public bool IsRunning => _reader != null;

        public async Task<bool> StartAsync()
        {
            if (_reader != null || _starting)
                return true;
            _starting = true;
            try
            {
                var groups = await MediaFrameSourceGroup.FindAllAsync();
                var match = groups
                    .SelectMany(g => g.SourceInfos.Select(info => (Group: g, Info: info)))
                    .FirstOrDefault(x => x.Info.SourceKind == MediaFrameSourceKind.Color &&
                                         (x.Info.MediaStreamType == MediaStreamType.VideoPreview ||
                                          x.Info.MediaStreamType == MediaStreamType.VideoRecord));
                if (match.Group == null)
                    return false; // no webcam

                _capture = new MediaCapture();
                try
                {
                    // Shared mode so a video call using the camera keeps working.
                    await _capture.InitializeAsync(Settings(match.Group, MediaCaptureSharingMode.SharedReadOnly));
                }
                catch
                {
                    _capture.Dispose();
                    _capture = new MediaCapture();
                    await _capture.InitializeAsync(Settings(match.Group, MediaCaptureSharingMode.ExclusiveControl));
                }

                var source = _capture.FrameSources[match.Info.Id];
                _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
                _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
                _reader.FrameArrived += OnFrameArrived;

                if (await _reader.StartAsync() != MediaFrameReaderStartStatus.Success)
                {
                    await StopAsync();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                // Usually camera privacy settings: "Let desktop apps access your camera".
                Log.Error("Could not start the camera", ex);
                await StopAsync();
                return false;
            }
            finally
            {
                _starting = false;
            }
        }

        private static MediaCaptureInitializationSettings Settings(MediaFrameSourceGroup group, MediaCaptureSharingMode mode) => new()
        {
            SourceGroup = group,
            SharingMode = mode,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        };

        public async Task StopAsync()
        {
            var reader = _reader;
            var capture = _capture;
            _reader = null;
            _capture = null;
            try
            {
                if (reader != null)
                {
                    reader.FrameArrived -= OnFrameArrived;
                    await reader.StopAsync();
                    reader.Dispose();
                }
                capture?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Could not stop the camera", ex);
            }
        }

        private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
        {
            // Drop frames while the previous one is still being drawn.
            if (Interlocked.Exchange(ref _framePending, 1) == 1)
                return;

            try
            {
                using var frame = sender.TryAcquireLatestFrame();
                var source = frame?.VideoMediaFrame?.SoftwareBitmap;
                if (source == null)
                {
                    _framePending = 0;
                    return;
                }

                using var bgra = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                int width = bgra.PixelWidth, height = bgra.PixelHeight;
                var pixels = new byte[width * height * 4];
                bgra.CopyToBuffer(pixels.AsBuffer());

                _ui.InvokeAsync(() =>
                {
                    try
                    {
                        if (_reader == null)
                            return;
                        if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
                        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                        FrameReady?.Invoke(_bitmap);
                    }
                    finally
                    {
                        _framePending = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                _framePending = 0;
                Log.Error("Camera frame failed", ex);
            }
        }
    }
}
