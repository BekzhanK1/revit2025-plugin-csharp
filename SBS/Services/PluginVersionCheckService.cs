using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class PluginVersionCheckResult
    {
        public bool IsSupported { get; init; }
        public bool UpdateAvailable { get; init; }
        public string ClientVersion { get; init; }
        public string MinSupportedVersionName { get; init; }
        public string LatestVersionName { get; init; }
        public string LatestDownloadUrl { get; init; }
    }

    public static class PluginVersionCheckService
    {
        public const int RevitYear = 2025;

        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static Task<PluginVersionCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            CheckAsync(PluginVersion.GetCurrent(), cancellationToken);

        public static async Task<PluginVersionCheckResult> CheckAsync(
            string clientVersion,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientVersion))
                throw new InvalidOperationException("Не удалось определить версию плагина");

            var url = Configs.PluginVersionCheckUrl(RevitYear, clientVersion.Trim());
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Не удалось проверить версию плагина ({(int)response.StatusCode})";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<PluginVersionCheckResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ проверки версии");

            if (parsed.Status == false || !string.IsNullOrWhiteSpace(parsed.Error))
                throw new InvalidOperationException(parsed.Error ?? "Проверка версии плагина не выполнена");

            var isSupported = parsed.IsSupported ?? true;
            return new PluginVersionCheckResult
            {
                IsSupported = isSupported,
                UpdateAvailable = parsed.UpdateAvailable ?? false,
                ClientVersion = parsed.ClientVersion ?? clientVersion,
                MinSupportedVersionName = parsed.MinSupportedVersionName,
                LatestVersionName = parsed.LatestVersionName,
                LatestDownloadUrl = Configs.ResolveDownloadUrl(parsed.LatestFileUrl),
            };
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var parsed = JsonConvert.DeserializeObject<PluginVersionCheckResponse>(responseBody);
                if (!string.IsNullOrWhiteSpace(parsed?.Error))
                    return parsed.Error;
            }
            catch
            {
                // ignore parse errors
            }

            return null;
        }
    }
}
