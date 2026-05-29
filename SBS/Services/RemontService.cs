using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public static class RemontService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<IReadOnlyList<RemontOption>> QuickSearchAsync(bool byRemontId, int id)
        {
            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            var request = new QuickSearchRequest();
            if (byRemontId)
                request.RemontId = id;
            else
                request.ClientRequestId = id;

            var json = JsonConvert.SerializeObject(request);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Configs.QuickSearchUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка поиска ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                else if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    message = "Нет права на поиск ремонтов (OA__RemontFormQuickSearch).";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<QuickSearchResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка поиска" : parsed.Error);

            return (parsed.Data ?? new List<QuickSearchItemDto>())
                .Select(ToRemontOption)
                .ToList();
        }

        static RemontOption ToRemontOption(QuickSearchItemDto item) =>
            new RemontOption
            {
                ClientRequestId = item.ClientRequestId,
                RemontId = item.RemontId,
                Name = FormatDisplayName(item),
                ClientName = item.ClientName?.Trim(),
                ResidentName = item.ResidentName?.Trim(),
                FlatNum = item.FlatNum?.Trim(),
                PresetName = item.PresetName?.Trim()
            };

        static string FormatDisplayName(QuickSearchItemDto item)
        {
            var parts = new List<string>();
            if (item.RemontId.HasValue)
                parts.Add($"Ремонт #{item.RemontId}");
            parts.Add($"Заявка #{item.ClientRequestId}");

            if (!string.IsNullOrWhiteSpace(item.ClientName))
                parts.Add(item.ClientName.Trim());

            if (!string.IsNullOrWhiteSpace(item.ResidentName))
            {
                var flat = string.IsNullOrWhiteSpace(item.FlatNum) ? "" : $", кв. {item.FlatNum.Trim()}";
                parts.Add($"{item.ResidentName.Trim()}{flat}");
            }

            if (!string.IsNullOrWhiteSpace(item.RemontStatusName))
                parts.Add(item.RemontStatusName.Trim());
            else if (!string.IsNullOrWhiteSpace(item.RequestStatusName))
                parts.Add(item.RequestStatusName.Trim());

            if (!string.IsNullOrWhiteSpace(item.RemontType))
                parts.Add(item.RemontType.Trim());

            return string.Join(" · ", parts);
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var error = JsonConvert.DeserializeObject<QuickSearchResponse>(responseBody);
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
