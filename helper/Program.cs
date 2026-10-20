using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WindowsBar.Helper
{
    /// <summary>
    /// Talks to the WindowsBar app over stdin/stdout, one JSON message per line.
    /// In:  {"cmd":"toggle"|"next"|"prev"} {"cmd":"seek","position":12.5} {"cmd":"share","paths":[...]}
    /// Out: {"type":"media",...} {"type":"art","data":"data:image/..."} {"type":"fullscreen","value":true}
    /// Quits when the app closes its end of stdin.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new Host());
        }
    }

    internal sealed class Host : ApplicationContext
    {
        private readonly Control _ui = new Control();
        private readonly Output _out = new Output();
        private readonly MediaWatcher _media;
        private readonly FullscreenWatcher _fullscreen;
        private readonly ShareSheet _share = new ShareSheet();

        public Host()
        {
            // Creating the handle gives us a UI thread to marshal everything onto.
            _ = _ui.Handle;
            _media = new MediaWatcher(_ui, _out);
            _fullscreen = new FullscreenWatcher(_out);
            _media.Start();

            var reader = new Thread(ReadCommands) { IsBackground = true, Name = "stdin" };
            reader.Start();
        }

        private void ReadCommands()
        {
            using (var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
            {
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    var command = Json.Read<Command>(line);
                    if (command != null)
                        _ui.BeginInvoke(new Action(() => Handle(command)));
                }
            }
            // The app went away; so do we.
            _ui.BeginInvoke(new Action(ExitThread));
        }

        private void Handle(Command command)
        {
            try
            {
                switch (command.Cmd)
                {
                    case "toggle": _media.TogglePlayPause(); break;
                    case "next": _media.Next(); break;
                    case "prev": _media.Previous(); break;
                    case "seek": _media.Seek(command.Position); break;
                    case "share": _share.Share(command.Paths ?? new string[0]); break;
                }
            }
            catch (Exception ex)
            {
                _out.Error($"{command.Cmd} failed: {ex.Message}");
            }
        }

        protected override void ExitThreadCore()
        {
            _fullscreen.Dispose();
            base.ExitThreadCore();
        }
    }
}
