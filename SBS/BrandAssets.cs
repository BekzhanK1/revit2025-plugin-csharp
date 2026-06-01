using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SmartRemont.ExportRooms
{
    public static class BrandAssets
    {
        public const string CompanyLogoFileName = "logo.png";
        public const string RibbonIconFileName = "export_32.png";

        const string PackResourceBase = "pack://application:,,,/SmartRemont.ExportRooms;component/Resources/";

        public static string ResourcesDirectory
        {
            get
            {
                var baseDir = ExportRoomsApplication._path;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(baseDir, "Resources");
            }
        }

        public static BitmapImage LoadBitmap(string fileName)
        {
            var diskPath = Path.Combine(ResourcesDirectory, fileName);
            var uri = File.Exists(diskPath)
                ? new Uri(diskPath, UriKind.Absolute)
                : new Uri(PackResourceBase + fileName);
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        public static bool TryApplyCompanyLogo(Image target, double maxHeight = 48)
        {
            var diskPath = Path.Combine(ResourcesDirectory, CompanyLogoFileName);
            if (!File.Exists(diskPath))
            {
                target.Visibility = Visibility.Collapsed;
                return false;
            }

            target.Source = LoadBitmap(CompanyLogoFileName);
            target.MaxHeight = maxHeight;
            target.Stretch = System.Windows.Media.Stretch.Uniform;
            target.Visibility = Visibility.Visible;
            return true;
        }
    }
}
