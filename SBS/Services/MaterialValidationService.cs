using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class MaterialValidationResult
    {
        public IReadOnlySet<string> FoundIds { get; init; }
        public int RequestedCount { get; init; }
        public string RequestUrl { get; init; }
    }

    public static class MaterialValidationService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static bool IsNumericMaterialId(string id) =>
            !string.IsNullOrWhiteSpace(id)
            && id != "—"
            && long.TryParse(id.Trim(), out _);

        public static Task<MaterialValidationResult> ValidateMaterialIdsAsync(IEnumerable<string> materialIds) =>
            ValidateMaterialIdsAsync(materialIds, ExportRoomsApplication.CurrentSession?.AccessToken);

        public static async Task<MaterialValidationResult> ValidateMaterialIdsAsync(
            IEnumerable<string> materialIds,
            string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new InvalidOperationException("Требуется авторизация");

            var ids = materialIds?
                .Where(IsNumericMaterialId)
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            var requestUrl = Configs.MaterialValidationUrl;
            if (ids.Count == 0)
            {
                return new MaterialValidationResult
                {
                    FoundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    RequestedCount = 0,
                    RequestUrl = requestUrl
                };
            }

            var request = new MaterialValidationRequest { MaterialIds = ids };
            var json = JsonConvert.SerializeObject(request);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            ExportRoomsApplication._logger?.Information(
                "Material validation request: {Url}, ids={Count}",
                requestUrl,
                ids.Count);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            ExportRoomsApplication._logger?.Information(
                "Material validation response: {StatusCode}, body={Body}",
                (int)response.StatusCode,
                Truncate(responseBody, 500));

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка проверки ID ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException($"{message} · {requestUrl}");
            }

            var parsed = JsonConvert.DeserializeObject<MaterialValidationResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка проверки ID" : parsed.Error);

            var foundIds = new HashSet<string>(
                (parsed.Data?.FoundIds ?? new List<string>())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            return new MaterialValidationResult
            {
                FoundIds = foundIds,
                RequestedCount = ids.Count,
                RequestUrl = requestUrl
            };
        }

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength) + "…";
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var error = JsonConvert.DeserializeObject<MaterialValidationResponse>(responseBody);
                if (!string.IsNullOrWhiteSpace(error?.Error))
                    return error.Error;

                dynamic raw = JsonConvert.DeserializeObject(responseBody);
                if (raw?.error != null)
                    return raw.error.ToString();
                if (raw?.detail != null)
                    return raw.detail.ToString();
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}
