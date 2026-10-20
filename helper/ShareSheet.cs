using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Forms;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace WindowsBar.Helper
{
    /// <summary>
    /// Opens the Windows share sheet (Nearby Share, Phone Link, Mail…) for files —
    /// the Windows counterpart of AirDrop.
    /// </summary>
    internal sealed class ShareSheet
    {
        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDataTransferManagerInterop
        {
            DataTransferManager GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        private static readonly Guid DataTransferManagerId =
            new Guid(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

        private Form _anchor;
        private DataTransferManager _manager;
        private string[] _paths = new string[0];

        public void Share(string[] paths)
        {
            _paths = paths;
            EnsureAnchor();

            var interop = (IDataTransferManagerInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(DataTransferManager));
            if (_manager == null)
            {
                var iid = DataTransferManagerId;
                _manager = interop.GetForWindow(_anchor.Handle, ref iid);
                _manager.DataRequested += OnDataRequested;
            }
            interop.ShowShareUIForWindow(_anchor.Handle);
        }

        /// <summary>
        /// The share sheet needs a window of ours to belong to. This one is invisible and sits at the
        /// top-middle of the screen, where the island is, so the sheet opens near it.
        /// </summary>
        private void EnsureAnchor()
        {
            if (_anchor != null)
                return;
            var screen = Screen.PrimaryScreen.Bounds;
            _anchor = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(2, 2),
                Location = new Point(screen.Left + screen.Width / 2, screen.Top + 40),
                Opacity = 0.01,
                TopMost = true,
            };
            _anchor.Show();
        }

        private async void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var path in _paths)
                {
                    if (File.Exists(path))
                        items.Add(await StorageFile.GetFileFromPathAsync(path));
                    else if (Directory.Exists(path))
                        items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                }

                args.Request.Data.Properties.Title = items.Count == 1 ? items[0].Name : $"{items.Count} items";
                args.Request.Data.SetStorageItems(items);
            }
            catch (Exception)
            {
                args.Request.FailWithDisplayText("These files can't be shared.");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
