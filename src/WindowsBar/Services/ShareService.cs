using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace WindowsBar.Services
{
    /// <summary>
    /// Opens the Windows share sheet (Nearby Share, Phone Link, Mail…) for files.
    /// This is the Windows counterpart of AirDrop.
    /// </summary>
    public static class ShareService
    {
        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        private static readonly Guid DataTransferManagerId = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

        private static DataTransferManager? _manager;
        private static IReadOnlyList<string> _pending = Array.Empty<string>();

        public static void Share(Window owner, IReadOnlyList<string> paths)
        {
            try
            {
                var hwnd = new WindowInteropHelper(owner).Handle;
                var interop = DataTransferManager.As<IDataTransferManagerInterop>();

                if (_manager == null)
                {
                    var iid = DataTransferManagerId;
                    var ptr = interop.GetForWindow(hwnd, ref iid);
                    _manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);
                    _manager.DataRequested += OnDataRequested;
                }

                _pending = paths;
                interop.ShowShareUIForWindow(hwnd);
            }
            catch (Exception ex)
            {
                Log.Error("Could not open the share sheet", ex);
            }
        }

        private static async void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                var items = new List<IStorageItem>();
                foreach (var path in _pending)
                {
                    if (File.Exists(path))
                        items.Add(await StorageFile.GetFileFromPathAsync(path));
                    else if (Directory.Exists(path))
                        items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                }

                args.Request.Data.Properties.Title = items.Count == 1 ? items[0].Name : $"{items.Count} items";
                args.Request.Data.SetStorageItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("Could not prepare files for sharing", ex);
                args.Request.FailWithDisplayText("These files can't be shared.");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
