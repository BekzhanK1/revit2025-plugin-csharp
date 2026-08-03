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

        public static string RevitEventsCreateUrl => $"{ApiOriginUrl}/common/revit_events/create/";

        public static string MaterialValidationUrl => $"{ApiOriginUrl}/common/catalog/validate_material_ids/";

        // Замеры / ДС события (create+status) остаются на remont_id, пока не готов task-06 (client-request-primary).
        public static string RevitEventStatusUrl(int remontId, string eventType) =>
            $"{ApiOriginUrl}/common/revit_events/status/?remont_id={remontId}&type={Uri.EscapeDataString(eventType ?? "")}";

        // Материалы / ТК / ДС read — primary key client_request_id (client-request-primary, task-02/04/05).
        public static string DsRoomChangeReadUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/common/ds/room-change/read/?client_request_id={clientRequestId}";

        public static string ClientMaterialTkReadUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/common/client_material/tk/read/?client_request_id={clientRequestId}";

        public static string RevitMaterialReadUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/revit/material/read/?client_request_id={clientRequestId}";

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
