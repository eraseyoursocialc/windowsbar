using System;
using System.IO;
using System.Text.Json;

namespace WindowsBar.Services
{
    /// <summary>Where WindowsBar keeps its settings, tray contents and log.</summary>
    public static class AppPaths
    {
        public static string Dir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsBar");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }

    public static class Log
    {
        private static readonly object Gate = new();

        public static void Error(string message, Exception? ex = null)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(Path.Combine(AppPaths.Dir, "log.txt"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{(ex != null ? ": " + ex : "")}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }

    public sealed class Settings
    {
        public bool HideInFullscreen { get; set; } = true;

        private static string FilePath => Path.Combine(AppPaths.Dir, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch (Exception ex)
            {
                Log.Error("Could not read settings", ex);
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Error("Could not save settings", ex);
            }
        }
    }
}
