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
    public static class RevitEventsService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<RevitEventCreateDataDto> SendDsAreaChangeAsync(
            int remontId,
            IEnumerable<RemontRoomAreaDto> rooms)
        {
            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            if (remontId <= 0)
                throw new InvalidOperationException("Не указан ID ремонта");

            var roomList = rooms?.ToList() ?? new List<RemontRoomAreaDto>();
            if (roomList.Count == 0)
                throw new InvalidOperationException("Нет помещений для отправки");

            var requestBody = new RevitEventCreateRequest
            {
                RemontId = remontId,
                Type = RevitEventTypes.DsAreaChange,
                Payload = new DsAreaChangePayloadDto
                {
                    Source = "revit",
                    Version = 1,
                    Rooms = roomList
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Configs.RevitEventsCreateUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка отправки ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<RevitEventCreateResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка отправки" : parsed.Error);

            return parsed.Data;
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var error = JsonConvert.DeserializeObject<RevitEventCreateResponse>(responseBody);
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
