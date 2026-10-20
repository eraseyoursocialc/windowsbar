using System;
using Microsoft.Win32;

namespace WindowsBar.Services
{
    /// <summary>Turns "start with Windows" on and off through the current user's Run key.</summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WindowsBar";

        public static bool IsEnabled
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled && Environment.ProcessPath is string exe)
                    key.SetValue(ValueName, $"\"{exe}\"");
                else
                    key.DeleteValue(ValueName, false);
            }
            catch (Exception ex)
            {
                Log.Error("Could not change startup setting", ex);
            }
        }
    }
}
