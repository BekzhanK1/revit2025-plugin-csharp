using System;
using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public static class WindowLayoutHelper
    {
        public static void UseFullWorkAreaHeight(Window window)
        {
            window.SourceInitialized += (_, __) => Apply(window);
            window.Loaded += (_, __) => Apply(window);
        }

        static void Apply(Window window)
        {
            var area = SystemParameters.WorkArea;
            window.MaxHeight = area.Height;
            window.Height = area.Height;
            window.Top = area.Top;

            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            if (width > 0 && !double.IsNaN(width) && width <= area.Width)
                window.Left = area.Left + (area.Width - width) / 2;
        }
    }
}
