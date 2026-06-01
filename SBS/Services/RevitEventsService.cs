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
    public static class RevitEventsService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static Task<RevitEventCreateDataDto> SendDsAreaChangeAsync(
            int remontId,
            double wallHeight,
            IEnumerable<RemontRoomAreaDto> rooms) =>
            SendAsync(remontId, RevitEventTypes.DsAreaChange, new DsAreaChangePayloadDto
            {
                Source = "revit",
                Version = 1,
                WallHeight = wallHeight,
                Rooms = rooms?.ToList() ?? new List<RemontRoomAreaDto>()
            }, "Нет помещений для отправки");

        public static Task<RevitEventCreateDataDto> SendMeasuresAsync(
            int remontId,
            IEnumerable<RoomMeasurementsRoomRow> rooms) =>
            SendAsync(remontId, RevitEventTypes.Measures, BuildMeasuresPayload(rooms),
                "Нет замеров для отправки");

        public static async Task<RevitEventStatusDataDto> GetStatusAsync(int remontId, string eventType)
        {
            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            if (remontId <= 0)
                throw new InvalidOperationException("Не указан ID ремонта");

            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("Не указан тип события", nameof(eventType));

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                Configs.RevitEventStatusUrl(remontId, eventType));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка запроса статуса ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<RevitEventStatusResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка запроса статуса" : parsed.Error);

            return parsed.Data;
        }

        static MeasuresPayloadDto BuildMeasuresPayload(IEnumerable<RoomMeasurementsRoomRow> rooms)
        {
            var payload = new MeasuresPayloadDto { Source = "revit", Version = 1 };
            foreach (var room in rooms ?? Enumerable.Empty<RoomMeasurementsRoomRow>())
            {
                if (string.IsNullOrWhiteSpace(room?.RoomName))
                    continue;

                var parameters = (room.Parameters ?? new List<RoomMeasurementParamItem>())
                    .Where(p => p.param_value.HasValue)
                    .Select(p => new MeasureParamDto
                    {
                        ParamCode = p.param_code,
                        ParamName = p.param_name,
                        ParamValue = p.param_value
                    })
                    .ToList();

                if (parameters.Count == 0)
                    continue;

                payload.Rooms.Add(new MeasuresRoomDto
                {
                    RoomName = room.RoomName.Trim(),
                    Parameters = parameters
                });
            }

            return payload;
        }

        static async Task<RevitEventCreateDataDto> SendAsync(
            int remontId,
            string eventType,
            object payload,
            string emptyPayloadMessage)
        {
            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            if (remontId <= 0)
                throw new InvalidOperationException("Не указан ID ремонта");

            if (payload is DsAreaChangePayloadDto areaPayload && (areaPayload.Rooms?.Count ?? 0) == 0)
                throw new InvalidOperationException(emptyPayloadMessage);

            if (payload is MeasuresPayloadDto measuresPayload && (measuresPayload.Rooms?.Count ?? 0) == 0)
                throw new InvalidOperationException(emptyPayloadMessage);

            var requestBody = new RevitEventCreateRequest
            {
                RemontId = remontId,
                Type = eventType,
                Payload = payload
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
