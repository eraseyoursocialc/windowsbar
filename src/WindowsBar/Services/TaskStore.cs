using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WindowsBar.Services
{
    public sealed class TaskItem : INotifyPropertyChanged
    {
        private bool _done;

        public string Text { get; set; } = "";

        public bool Done
        {
            get => _done;
            set
            {
                _done = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Done)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>The to-do list, saved between restarts.</summary>
    public sealed class TaskStore
    {
        private static string FilePath => Path.Combine(AppPaths.Dir, "tasks.json");

        public ObservableCollection<TaskItem> Items { get; } = new();

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return;
                foreach (var item in JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(FilePath)) ?? new())
                    Items.Add(item);
            }
            catch (Exception ex)
            {
                Log.Error("Could not read tasks", ex);
            }
        }

        public void Add(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return;
            Items.Insert(0, new TaskItem { Text = text });
            Save();
        }

        public void Toggle(TaskItem item)
        {
            item.Done = !item.Done;
            Save();
        }

        public void Remove(TaskItem item)
        {
            Items.Remove(item);
            Save();
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Items.ToList()));
            }
            catch (Exception ex)
            {
                Log.Error("Could not save tasks", ex);
            }
        }
    }
}
