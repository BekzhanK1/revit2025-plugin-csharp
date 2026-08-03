using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace SmartRemont.ExportSpecifications
{
    public static class BrandAssets
    {
        public const string RibbonIconFileName = "export_32.png";

        const string PackResourceBase = "pack://application:,,,/SmartRemont.ExportSpecifications;component/Resources/";

        public static string ResourcesDirectory =>
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
                "Resources");

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
    }
}
