using SmartRemont.ExportRooms;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartRemont.ExportRooms.Views
{
    /// <summary>
    /// Full-height layout within <see cref="SystemParameters.WorkArea"/> and horizontal centering
    /// for wide hub/home frames. Width targets (see DECISIONS #6): Home 900 px, Hub 960 px — applied in XAML by task-05.
    /// </summary>
    public static class WindowLayoutHelper
    {
        /// <summary>Default <see cref="Window.Width"/> for <see cref="HomeWindow"/> (task-05).</summary>
        public const double HomeDefaultWidth = 900;

        /// <summary>Default <see cref="Window.Width"/> for <see cref="RemontHubWindow"/> (task-05).</summary>
        public const double HubDefaultWidth = 960;

        public static void UseFullWorkArea(Window window)
        {
            EnableEnvironmentBranding(window);
            window.SourceInitialized += (_, __) => ApplyFull(window);
            window.Loaded += (_, __) => ApplyFull(window);
        }

        static void ApplyFull(Window window)
        {
            var area = SystemParameters.WorkArea;
            window.WindowState = WindowState.Normal;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = area.Left;
            window.Top = area.Top;
            window.Width = area.Width;
            window.Height = area.Height;
            window.MaxWidth = area.Width;
            window.MaxHeight = area.Height;
        }

        public static void UseFullWorkAreaHeight(Window window)
        {
            EnableEnvironmentBranding(window);
            window.SourceInitialized += (_, __) => Apply(window);
            window.Loaded += (_, __) => Apply(window);
        }

        public static void EnableEnvironmentBranding(Window window)
        {
            if (window == null)
                return;

            window.Loaded += (_, __) => ApplyEnvironmentBranding(window);
        }

        public static void ApplyEnvironmentBranding(Window window)
        {
            if (window == null)
                return;

            window.Title = AppBranding.NormalizeWindowTitle(window.Title);
            ApplyBrandLabels(window);
        }

        public static void ApplyBrandLabels(DependencyObject root)
        {
            if (!Configs.IsTestApi || root == null)
                return;

            ApplyBrandTextDeep(root);
        }

        static void ApplyBrandTextDeep(DependencyObject root)
        {
            if (root == null)
                return;

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.TextBlock textBlock
                    && string.Equals(textBlock.Text, AppBranding.ProductName, StringComparison.Ordinal))
                {
                    textBlock.Text = AppBranding.DisplayName;
                }

                ApplyBrandTextDeep(child);
            }
        }

        static void Apply(Window window)
        {
            var area = SystemParameters.WorkArea;
            window.MaxHeight = area.Height;
            window.Height = area.Height;
            window.Top = area.Top;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            CenterHorizontally(window, area);

            if (window.ActualWidth <= 0)
            {
                window.Dispatcher.BeginInvoke(
                    new Action(() => CenterHorizontally(window)),
                    DispatcherPriority.Loaded);
            }
        }

        static void CenterHorizontally(Window window, Rect? workArea = null)
        {
            var area = workArea ?? SystemParameters.WorkArea;
            var width = GetEffectiveWidth(window);
            if (width <= 0 || double.IsNaN(width))
                return;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = Math.Max(area.Left, area.Left + (area.Width - width) / 2);
        }

        static double GetEffectiveWidth(Window window)
        {
            if (window.ActualWidth > 0)
                return window.ActualWidth;

            if (window.Width > 0 && !double.IsNaN(window.Width))
                return window.Width;

            return 0;
        }
    }
}
