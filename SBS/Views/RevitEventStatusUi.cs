using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public static class RevitEventStatusUi
    {
        public static void ApplyFeatureBadge(Button button, RevitEventStatusDataDto status)
        {
            button.ApplyTemplate();

            var badge = button.Template.FindName("SentBadge", button) as Border;
            var badgeText = button.Template.FindName("SentBadgeText", button) as TextBlock;
            if (badge == null || badgeText == null)
                return;

            var text = RevitEventStatusFormatter.FormatBadgeText(status);
            if (string.IsNullOrEmpty(text))
            {
                badge.Visibility = Visibility.Collapsed;
                return;
            }

            badge.Visibility = Visibility.Visible;
            badgeText.Text = text;

            if (status.IsImported == true)
            {
                badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCFCE7"));
                badge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                badgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"));
            }
            else
            {
                badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
                badge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BFDBFE"));
                badgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));
            }
        }

        public static void ApplyBanner(Border banner, TextBlock bannerText, RevitEventStatusDataDto status)
        {
            var text = RevitEventStatusFormatter.FormatBannerText(status);
            if (string.IsNullOrEmpty(text))
            {
                banner.Visibility = Visibility.Collapsed;
                return;
            }

            banner.Visibility = Visibility.Visible;
            bannerText.Text = text;

            if (status.IsImported == true)
            {
                banner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ECFDF5"));
                banner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                bannerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"));
            }
            else
            {
                banner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF"));
                banner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BFDBFE"));
                bannerText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E40AF"));
            }
        }
    }
}
