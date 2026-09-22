using System;
using System.Configuration;
using System.Linq;
using System.Reflection;

namespace SmartRemont.ExportRooms
{
    public static class Configs
    {
        public const string UseTestApiKey = "useTestApi";
        public const string ApiOriginUrlKey = "apiOriginUrl";
        public const string S3OriginUrlKey = "s3OriginUrl";
        public const string ProductionApiOriginUrl = "https://myspace-api.smartremont.kz";
        public const string TestApiOriginUrl = "https://office-testapi.smart-remont.kz";
        const string DefaultS3OriginUrl = "https://s3.smartremont.kz/smartremont";

        public static bool IsTestApi =>
            !string.Equals(ApiOriginUrl, ProductionApiOriginUrl, StringComparison.OrdinalIgnoreCase);

        static bool? _useTestApi;
        static string _apiOriginUrl;
        static string _s3OriginUrl;

        /// <summary>
        /// true — тестовый API, false — боевой. Ключ useTestApi в app.config / SmartRemont.ExportRooms.dll.config.
        /// </summary>
        public static bool UseTestApi
        {
            get
            {
                if (_useTestApi.HasValue)
                    return _useTestApi.Value;

                var fromToggle = ReadAppSetting(UseTestApiKey);
                if (!string.IsNullOrWhiteSpace(fromToggle))
                {
                    _useTestApi = ParseBool(fromToggle);
                    return _useTestApi.Value;
                }

                // Обратная совместимость: если задан только apiOriginUrl — определяем окружение по URL.
                var legacyUrl = ReadAppSetting(ApiOriginUrlKey);
                if (!string.IsNullOrWhiteSpace(legacyUrl))
                {
                    _useTestApi = !string.Equals(
                        NormalizeOrigin(legacyUrl),
                        ProductionApiOriginUrl,
                        StringComparison.OrdinalIgnoreCase);
                    return _useTestApi.Value;
                }

                _useTestApi = false;
                return false;
            }
        }

        /// <summary>
        /// Базовый URL API (origin). По умолчанию из пресетов test/prod (useTestApi).
        /// Необязательный apiOriginUrl переопределяет пресет.
        /// </summary>
        public static string ApiOriginUrl
        {
            get
            {
                if (_apiOriginUrl != null)
                    return _apiOriginUrl;

                var overrideUrl = ReadAppSetting(ApiOriginUrlKey);
                if (!string.IsNullOrWhiteSpace(overrideUrl))
                {
                    _apiOriginUrl = NormalizeOrigin(overrideUrl);
                    return _apiOriginUrl;
                }

                _apiOriginUrl = UseTestApi ? TestApiOriginUrl : ProductionApiOriginUrl;
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
                _s3OriginUrl = NormalizeBaseUrl(
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
                return EnsureS3BucketPrefix(trimmed);
            }

            var relative = trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
            return EnsureS3BucketPrefix(S3OriginUrl + relative);
        }

        public static string PluginVersionCheckUrl(int revitYear, string clientVersion) =>
            $"{ApiOriginUrl}/revit/plugin/version/check/?revit_year={revitYear}&client_version={Uri.EscapeDataString(clientVersion ?? string.Empty)}";

        public static string AuthLoginUrl => $"{ApiOriginUrl}/auth/revit/login/";

        public static string AuthRefreshUrl => $"{ApiOriginUrl}/auth/token/refresh/";

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

        // Office DS API (TK_CHANGE create/bind) — те же эндпоинты, что MySpace remontDS.
        public static string ClientRequestDsListUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/client_request/{clientRequestId}/ds/read/";

        public static string ClientRequestDsAddUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/client_request/{clientRequestId}/ds/add/";

        public static string ClientRequestDsUrl(int clientRequestId, int dsId) =>
            $"{ApiOriginUrl}/client_request/{clientRequestId}/ds/{dsId}/";

        public static string ClientRequestDsTkChangeSetItemCntUrl(int clientRequestId) =>
            $"{ApiOriginUrl}/client_request/{clientRequestId}/ds/tk_change_set_item_cnt/";

        public static string ClientRequestDsTkMaterialUrl(int clientRequestId, int dsId) =>
            $"{ApiOriginUrl}/client_request/{clientRequestId}/ds/{dsId}/tk_material/";

        public static string DsTypesReadUrl =>
            $"{ApiOriginUrl}/client_request/common/ds_types/read/";

        public static string WorkSetsReadUrl =>
            $"{ApiOriginUrl}/common/work_sets/read/";

        static string ReadAppSetting(string key)
        {
            var loc = Assembly.GetExecutingAssembly().Location;
            var config = ConfigurationManager.OpenExeConfiguration(loc);
            return config.AppSettings.Settings[key]?.Value;
        }

        static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            return normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("1", StringComparison.Ordinal)
                || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeOrigin(string url)
        {
            var cleaned = CleanUrl(url);
            if (cleaned == null)
                return url;

            if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrEmpty(uri.Host))
            {
                return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            }

            return cleaned;
        }

        /// <summary>
        /// Как origin, но сохраняет путь. Для S3 это бакет: https://s3.smartremont.kz/smartremont.
        /// </summary>
        static string NormalizeBaseUrl(string url)
        {
            var cleaned = CleanUrl(url);
            if (cleaned == null)
                return url;

            if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrEmpty(uri.Host))
            {
                var path = (uri.AbsolutePath ?? string.Empty).TrimEnd('/');
                var authority = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
                if (string.IsNullOrEmpty(path) || path == "/")
                    return authority;

                return authority + path;
            }

            return cleaned;
        }

        static string EnsureS3BucketPrefix(string absoluteUrl)
        {
            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
                return absoluteUrl;
            if (!Uri.TryCreate(S3OriginUrl, UriKind.Absolute, out var s3Base))
                return absoluteUrl;
            if (!string.Equals(uri.Host, s3Base.Host, StringComparison.OrdinalIgnoreCase))
                return absoluteUrl;

            var bucketPath = (s3Base.AbsolutePath ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(bucketPath) || bucketPath == "/")
                return absoluteUrl;

            var pathAndQuery = uri.PathAndQuery ?? "/";
            if (pathAndQuery.Equals(bucketPath, StringComparison.OrdinalIgnoreCase)
                || pathAndQuery.StartsWith(bucketPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUrl;
            }

            var authority = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            return authority + bucketPath + pathAndQuery;
        }

        static string CleanUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            // Drop accidental non-ASCII (e.g. Cyrillic ё pasted into .kz) — DNS then fails with "хост неизвестен".
            return new string(url.Trim().Where(c => c < 127 && !char.IsControl(c)).ToArray())
                .TrimEnd('/');
        }
    }
}
