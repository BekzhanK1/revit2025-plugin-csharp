using System;
using System.Configuration;
using System.Reflection;

namespace SmartRemont.ExportRooms
{
    public static class Configs
    {
        public const string ApiOriginUrlKey = "apiOriginUrl";
        const string DefaultApiOriginUrl = "https://office-testapi.smart-remont.kz";

        static string _apiOriginUrl;

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

        public static string AuthLoginUrl => $"{ApiOriginUrl}/auth/revit/login/";

        public static string QuickSearchUrl => $"{ApiOriginUrl}/client_request/quick_search/";

        public static string RevitEventsCreateUrl => $"{ApiOriginUrl}/common/revit_events/create/";

        public static string MaterialValidationUrl => $"{ApiOriginUrl}/common/catalog/validate_material_ids/";

        public static string RevitEventStatusUrl(int remontId, string eventType) =>
            $"{ApiOriginUrl}/common/revit_events/status/?remont_id={remontId}&type={Uri.EscapeDataString(eventType ?? "")}";

        public static string DsRoomChangeReadUrl(int remontId) =>
            $"{ApiOriginUrl}/common/ds/room-change/read/?remont_id={remontId}";

        public static string ClientMaterialTkReadUrl(int remontId) =>
            $"{ApiOriginUrl}/common/client_material/tk/read/?remont_id={remontId}";

        public static string RevitMaterialReadUrl(int remontId) =>
            $"{ApiOriginUrl}/revit/material/read/?remont_id={remontId}";

        /// <summary>
        /// Временная ссылка на общий surfaces.rvt (пока не в БД). Заполнить вручную перед синхронизацией surface-материалов.
        /// </summary>
        public const string SurfacesRvtUrl = "http://minio.retrograd.app/api/v1/download-shared-object/aHR0cHM6Ly9zMy5yZXRyb2dyYWQuYXBwL3Jldml0L3N1cmZhY2VzLnJ2dD9YLUFtei1BbGdvcml0aG09QVdTNC1ITUFDLVNIQTI1NiZYLUFtei1DcmVkZW50aWFsPVJDOTBMWElMQkY2RFEyU1lTUUdDJTJGMjAyNjA3MDElMkZ1cy1lYXN0LTElMkZzMyUyRmF3czRfcmVxdWVzdCZYLUFtei1EYXRlPTIwMjYwNzAxVDEyMTA0MVomWC1BbXotRXhwaXJlcz00MzIwMCZYLUFtei1TZWN1cml0eS1Ub2tlbj1leUpoYkdjaU9pSklVelV4TWlJc0luUjVjQ0k2SWtwWFZDSjkuZXlKaFkyTmxjM05MWlhraU9pSlNRemt3VEZoSlRFSkdOa1JSTWxOWlUxRkhReUlzSW1WNGNDSTZNVGM0TWpreU56SXdOaXdpY0dGeVpXNTBJam9pYzIxeVpXMXZiblFpZlEuS0hqaTBNRFR5SG13d0dvZkVFM0FfakxYdXN0MmkwdWdHN1h4UDF2ejlPUlE1Nzk3N0hRbC00N0NHUUQ3ODJYOUlrUFhhS3otVnZfV2VyZ0FRb3BMZHcmWC1BbXotU2lnbmVkSGVhZGVycz1ob3N0JnZlcnNpb25JZD1udWxsJlgtQW16LVNpZ25hdHVyZT0xNWQyMjA5Yjk3Yjc4MDYzNTNhYzRmMzY3NTg5Zjg1YmQ3ZmM1MjFhMzNkMzIxOTBiMjkxMDNhNjFjM2QyY2E1";

        public static bool HasSurfacesRvtUrl =>
            !string.IsNullOrWhiteSpace(SurfacesRvtUrl);

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
