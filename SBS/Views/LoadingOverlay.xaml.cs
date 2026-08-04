using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SmartRemont.ExportRooms.Views
{
    public partial class LoadingOverlay : UserControl
    {
        public LoadingOverlay()
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(OverlayLogoImage);
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
                this.Visibility = Visibility.Collapsed;
                tcs.SetResult(true);
            };
            
            this.BeginAnimation(UIElement.OpacityProperty, da);
            
            await tcs.Task;
        }
    }
}
