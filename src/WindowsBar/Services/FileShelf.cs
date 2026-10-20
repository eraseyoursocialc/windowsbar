using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace WindowsBar.Services
{
    public sealed class ShelfItem
    {
        public ShelfItem(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(Name))
                Name = path; // a drive root like C:\
            Icon = Native.GetShellIcon(path);
        }

        public string Path { get; }
        public string Name { get; }
        public ImageSource? Icon { get; }
        public bool Exists => File.Exists(Path) || Directory.Exists(Path);
    }

    /// <summary>
    /// A temporary holding spot for files: drop them on the island, drag them out later.
    /// Only the paths are kept; the files themselves never move unless you move them.
    /// </summary>
    public sealed class FileShelf
    {
        private static string FilePath => System.IO.Path.Combine(AppPaths.Dir, "shelf.json");

        public ObservableCollection<ShelfItem> Items { get; } = new();

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;
                var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new();
                foreach (var path in paths)
                {
                    var item = new ShelfItem(path);
                    if (item.Exists)
                        Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not read the file shelf", ex);
            }
        }

        public void Add(string path)
        {
            if (Items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                return;
            Items.Add(new ShelfItem(path));
            Save();
        }

        public void Remove(ShelfItem item)
        {
            Items.Remove(item);
            Save();
        }

        public void Clear()
        {
            Items.Clear();
            Save();
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Items.Select(i => i.Path).ToList()));
            }
            catch (Exception ex)
            {
                Log.Error("Could not save the file shelf", ex);
            }
        }
    }
}
