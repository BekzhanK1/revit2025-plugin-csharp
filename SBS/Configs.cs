using System;
using System.Configuration;
using System.Reflection;

namespace SmartRemont.ExportRooms
{
    public static class Configs
    {
        public const string ApiOriginUrlKey = "apiOriginUrl";
        public const string S3OriginUrlKey = "s3OriginUrl";
        const string DefaultApiOriginUrl = "https://office-testapi.smart-remont.kz";
        const string DefaultS3OriginUrl = "https://s3.smartremont.kz/smartremont";

        static string _apiOriginUrl;
        static string _s3OriginUrl;

        /// <summary>
        /// Базовый URL API (origin). Меняется в app.config / SmartRemont.ExportRooms.dll.config, ключ apiOriginUrl.
        /// </summary>
        public static string ApiOriginUrl
        {
            get
            {
                if (_apiOriginUrl != null)
                    return _apiOriginUrl;

                var fromConfig = ReadAppSetting(ApiOriginUrlKey);
                _apiOriginUrl = NormalizeOrigin(
                    string.IsNullOrWhiteSpace(fromConfig) ? DefaultApiOriginUrl : fromConfig);
                return _apiOriginUrl;
            }
        }

        /// <summary>
        /// Базовый URL S3/MinIO для revit_file_url и surfaces_file_url, если API вернул относительный path.
        /// </summary>
        public static string S3OriginUrl
        {
            get
            {
                if (_s3OriginUrl != null)
                    return _s3OriginUrl;

                var fromConfig = ReadAppSetting(S3OriginUrlKey);
                _s3OriginUrl = NormalizeOrigin(
                    string.IsNullOrWhiteSpace(fromConfig) ? DefaultS3OriginUrl : fromConfig);
                return _s3OriginUrl;
            }
        }

        /// <summary>
        /// Абсолютный URL для скачивания: http(s) как есть, иначе дополняется S3OriginUrl.
        /// </summary>
        public static string ResolveDownloadUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            var trimmed = url.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
                && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                return trimmed;
            }

            return trimmed.StartsWith("/", StringComparison.Ordinal)
                ? S3OriginUrl + trimmed
                : S3OriginUrl + "/" + trimmed;
        }

        public static string AuthLoginUrl => $"{ApiOriginUrl}/auth/revit/login/";

        public static string QuickSearchUrl => $"{ApiOriginUrl}/client_request/quick_search/";

        public static string MaterialValidationUrl => $"{ApiOriginUrl}/common/catalog/validate_material_ids/";

        public static string RevitMaterialReadUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/revit/plugin/material/read/?client_request_id={clientRequestId}";

        // Единый неймспейс /revit/plugin/ — display + apply, primary key client_request_id (PLUGIN_API.md).
        public static string TkReadUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/revit/plugin/tk/read/?client_request_id={clientRequestId}";

        public static string DsRoomChangeReadUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/revit/plugin/ds/room-change/read/?client_request_id={clientRequestId}";

        public static string DsRoomChangeApplyUrl => $"{ApiOriginUrl}/revit/plugin/ds/room-change/apply/";

        public static string MeasuresReadUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/revit/plugin/measures/read/?client_request_id={clientRequestId}";

        public static string MeasuresApplyUrl => $"{ApiOriginUrl}/revit/plugin/measures/apply/";

        static string ReadAppSetting(string key)
        {
            var loc = Assembly.GetExecutingAssembly().Location;
            var config = ConfigurationManager.OpenExeConfiguration(loc);
            return config.AppSettings.Settings[key]?.Value;
        }

        static string NormalizeOrigin(string url) =>
            url?.Trim().TrimEnd('/');
    }
}
