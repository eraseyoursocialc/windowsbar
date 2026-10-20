using System.Windows.Controls;
using System.Windows.Input;
using WindowsBar.Services;

namespace WindowsBar.Widgets
{
    /// <summary>Click the circle to see yourself; click again (or close the island) to turn the camera off.</summary>
    public partial class MirrorWidget : UserControl
    {
        private CameraService? _camera;

        public MirrorWidget()
        {
            InitializeComponent();
        }

        public void Attach(CameraService camera)
        {
            _camera = camera;
            _camera.FrameReady += frame =>
            {
                PreviewBrush.ImageSource = frame;
                Lens.Visibility = System.Windows.Visibility.Visible;
            };
        }

        private async void Circle_Click(object sender, MouseButtonEventArgs e)
        {
            if (_camera == null)
                return;

            if (_camera.IsRunning)
            {
                await StopAsync();
                return;
            }

            PlaceholderText.Text = "Starting…";
            if (await _camera.StartAsync())
            {
                PlaceholderGlyph.Text = "";
                PlaceholderText.Text = "Mirror";
                Circle.ToolTip = "Click to turn the camera off";
            }
            else
            {
                PlaceholderGlyph.Text = ""; // warning
                PlaceholderText.Text = "No camera";
                Circle.ToolTip = "Couldn't open the camera. Check Settings > Privacy > Camera > " +
                                 "\"Let desktop apps access your camera\".";
            }
        }

        public async System.Threading.Tasks.Task StopAsync()
        {
            if (_camera == null || !_camera.IsRunning)
                return;
            await _camera.StopAsync();
            Lens.Visibility = System.Windows.Visibility.Collapsed;
            PreviewBrush.ImageSource = null;
        }
    }
}
