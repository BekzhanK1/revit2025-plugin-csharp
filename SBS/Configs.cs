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
