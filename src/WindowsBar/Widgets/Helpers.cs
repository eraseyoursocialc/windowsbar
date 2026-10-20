using System;
using System.Windows;
using System.Windows.Media;

namespace WindowsBar.Widgets
{
    internal static class Helpers
    {
        /// <summary>"3:07" or "1:02:45".</summary>
        public static string Clock(TimeSpan t)
        {
            if (t < TimeSpan.Zero)
                t = TimeSpan.Zero;
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";
        }

        /// <summary>True if <paramref name="child"/> sits inside <paramref name="parent"/>.</summary>
        public static bool IsInside(DependencyObject? child, DependencyObject parent)
        {
            while (child != null)
            {
                if (child == parent)
                    return true;
                child = child is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(child)
                    : LogicalTreeHelper.GetParent(child);
            }
            return false;
        }

        /// <summary>Nearest ancestor of a given type, starting with the element itself.</summary>
        public static T? FindAncestor<T>(DependencyObject? child, DependencyObject? stopAt = null) where T : DependencyObject
        {
            while (child != null && child != stopAt)
            {
                if (child is T match)
                    return match;
                child = child is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(child)
                    : LogicalTreeHelper.GetParent(child);
            }
            return null;
        }
    }
}
