using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// GET /revit/plugin/measures/read/ + POST /revit/plugin/measures/apply/ — прямая запись,
    /// без event-буфера. Работает без remont (PLUGIN_API.md §2.3, §3.1).
    /// </summary>
    public static class MeasuresService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<List<MeasureRoomInfoDto>> ReadAsync(int clientRequestId)
        {
            if (clientRequestId <= 0)
                throw new InvalidOperationException("Не указан ID заявки");

            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                Configs.MeasuresReadUrl(clientRequestId));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка запроса замеров ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<MeasuresReadResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка запроса замеров" : parsed.Error);

            return parsed.Data ?? new List<MeasureRoomInfoDto>();
        }

        public static async Task<(List<MeasureRoomInfoDto> Data, bool Status, string Error)> TryReadAsync(int clientRequestId)
        {
            try
            {
                if (clientRequestId <= 0) return (null, false, "Не указан ID заявки");
                var session = ExportRoomsApplication.CurrentSession;
                if (session == null || string.IsNullOrWhiteSpace(session.AccessToken)) return (null, false, "Требуется авторизация");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, Configs.MeasuresReadUrl(clientRequestId));
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

                using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var message = TryReadErrorMessage(responseBody) ?? $"Ошибка запроса замеров ({(int)response.StatusCode})";
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) message = "Сессия истекла. Выйдите и войдите снова.";
                    return (null, false, message);
                }

                var parsed = JsonConvert.DeserializeObject<MeasuresReadResponse>(responseBody);
                if (parsed == null) return (null, false, "Сервер вернул некорректный ответ");
                if (!parsed.Status) return (null, false, string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка запроса замеров" : parsed.Error);

                return (parsed.Data ?? new List<MeasureRoomInfoDto>(), true, null);
            }
            catch (Exception ex)
            {
                return (null, false, ex.Message);
            }
        }

        /// <summary>Ключ — базовое имя комнаты (RoomNameMatcher), значение — системный room_id.</summary>
        public static Dictionary<string, int> BuildRoomIdsByKey(IEnumerable<MeasureRoomInfoDto> rooms) =>
            (rooms ?? Enumerable.Empty<MeasureRoomInfoDto>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RoomName) && r.RoomId > 0 && r.PlanirovkaRoomId > 0)
                .GroupBy(r => RoomNameMatcher.GetBaseName(r.RoomName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().RoomId, StringComparer.OrdinalIgnoreCase);

        public static async Task<MeasuresApplyDataDto> ApplyAsync(
            int clientRequestId,
            IEnumerable<RoomMeasurementsRoomRow> rooms,
            IReadOnlyDictionary<string, int> roomIdsByKey)
        {
            if (clientRequestId <= 0)
                throw new InvalidOperationException("Не указана заявка");

            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            var payloadRooms = BuildApplyRooms(rooms, roomIdsByKey);
            if (payloadRooms.Count == 0)
                throw new InvalidOperationException("Нет замеров для отправки");

            var requestBody = new MeasuresApplyRequest
            {
                ClientRequestId = clientRequestId,
                Rooms = payloadRooms
            };

            var json = JsonConvert.SerializeObject(requestBody);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Configs.MeasuresApplyUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка отправки замеров ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<MeasuresApplyResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка отправки замеров" : parsed.Error);

            return parsed.Data ?? new MeasuresApplyDataDto();
        }

        static List<MeasureApplyRoomDto> BuildApplyRooms(
            IEnumerable<RoomMeasurementsRoomRow> rooms,
            IReadOnlyDictionary<string, int> roomIdsByKey)
        {
            var result = new List<MeasureApplyRoomDto>();
            foreach (var room in rooms ?? Enumerable.Empty<RoomMeasurementsRoomRow>())
            {
                if (string.IsNullOrWhiteSpace(room?.RoomName))
                    continue;

                var parameters = (room.Parameters ?? new List<RoomMeasurementParamItem>())
                    .Where(p => p.param_value.HasValue && !string.IsNullOrWhiteSpace(p.param_code))
                    .Select(p => new MeasureApplyParamDto
                    {
                        ParamCode = p.param_code,
                        ParamValue = p.param_value.Value.ToString("0.####", CultureInfo.InvariantCulture)
                    })
                    .ToList();

                if (parameters.Count == 0)
                    continue;

                var key = RoomNameMatcher.GetBaseName(room.RoomName);
                var roomId = roomIdsByKey != null && roomIdsByKey.TryGetValue(key, out var id) ? id : 0;

                result.Add(new MeasureApplyRoomDto
                {
                    RoomId = roomId,
                    RoomName = room.RoomName.Trim(),
                    Params = parameters
                });
            }

            return result;
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var root = JObject.Parse(responseBody);
                return root["error"]?.Value<string>();
            }
            catch
            {
                return null;
            }
        }
    }
}
