using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SmartRemont.ExportRooms.Views
{
    public partial class LoadingOverlay : UserControl
    {
        public event EventHandler CancelRequested;

        public LoadingOverlay()
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(OverlayLogoImage);
            Visibility = Visibility.Collapsed;
        }

        public void Show(string message, bool indeterminate = true, bool allowCancel = false)
        {
            Opacity = 1;
            Visibility = Visibility.Visible;
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(message) ? "Загрузка..." : message.Trim();
            ProgressDetailTextBlock.Visibility = Visibility.Collapsed;
            ProgressDetailTextBlock.Text = string.Empty;
            OverlayProgressBar.IsIndeterminate = indeterminate;
            if (indeterminate)
                OverlayProgressBar.Value = 0;
            CancelButton.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;
        }

        void CancelButton_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

        public void UpdateProgress(int done, int total, string message, bool indeterminate = false)
        {
            Visibility = Visibility.Visible;
            Opacity = 1;
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(message) ? "Загрузка..." : message.Trim();
            OverlayProgressBar.IsIndeterminate = indeterminate;

            if (indeterminate || total <= 0)
            {
                ProgressDetailTextBlock.Visibility = Visibility.Collapsed;
                ProgressDetailTextBlock.Text = string.Empty;
                if (indeterminate)
                    OverlayProgressBar.Value = 0;
                return;
            }

            var safeTotal = Math.Max(total, 1);
            var safeDone = Math.Min(Math.Max(done, 0), safeTotal);
            OverlayProgressBar.Maximum = safeTotal;
            OverlayProgressBar.Value = safeDone;

            var percent = (int)Math.Round(100.0 * safeDone / safeTotal);
            ProgressDetailTextBlock.Text = $"{safeDone} из {safeTotal} ({percent}%)";
            ProgressDetailTextBlock.Visibility = Visibility.Visible;
        }

        public void HideImmediate()
        {
            OverlayProgressBar.IsIndeterminate = false;
            OverlayProgressBar.Value = 0;
            ProgressDetailTextBlock.Visibility = Visibility.Collapsed;
            ProgressDetailTextBlock.Text = string.Empty;
            Visibility = Visibility.Collapsed;
        }

        public async Task HideAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            var da = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            da.Completed += (s, e) =>
            {
                HideImmediate();
                tcs.SetResult(true);
            };

            BeginAnimation(OpacityProperty, da);

            await tcs.Task;
        }
    }
}
