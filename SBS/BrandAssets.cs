using System;
using System.Collections.Generic;
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
            foreach (var candidate in GetBitmapCandidates(fileName))
            {
                var image = TryLoadBitmapFromUri(candidate);
                if (image != null)
                    return image;
            }

            throw new FileNotFoundException(
                $"Не найден ресурс «{fileName}» (ни на диске в Resources/, ни в сборке).",
                fileName);
        }

        static IEnumerable<Uri> GetBitmapCandidates(string fileName)
        {
            var diskPath = Path.Combine(ResourcesDirectory, fileName);
            if (File.Exists(diskPath))
                yield return new Uri(diskPath, UriKind.Absolute);

            yield return new Uri(PackResourceBase + fileName, UriKind.Absolute);
        }

        static BitmapImage TryLoadBitmapFromUri(Uri uri)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = uri;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Debug(
                    ex,
                    "Brand asset not loaded from {Uri}",
                    uri);
                return null;
            }
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
