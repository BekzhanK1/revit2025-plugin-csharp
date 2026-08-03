using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartRemont.ExportRooms.DTO;
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
    public class DsRoomChangeSnapshot
    {
        public bool HasData { get; set; }
        public int? DsId { get; set; }
        public string DsDate { get; set; }
        public string DsTypeName { get; set; }
        public double? WallHeightM { get; set; }
        public List<DsRoomChangeRoomDto> Rooms { get; set; } = new();
        public string EmptyMessage { get; set; }
    }

    public static class DsRoomChangeService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<DsRoomChangeSnapshot> ReadAsync(int clientRequestId)
        {
            if (clientRequestId <= 0)
                throw new InvalidOperationException("Не указан ID заявки");

            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                Configs.DsRoomChangeReadUrl(clientRequestId));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка запроса ДС ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = ParseResponse(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка запроса ДС" : parsed.Error);

            return MapSnapshot(parsed);
        }

        /// <summary>
        /// POST /revit/plugin/ds/room-change/apply/ — прямая запись, требует remont_id != null (PLUGIN_API.md §3.2).
        /// </summary>
        public static async Task<DsRoomChangeApplyDataDto> ApplyAsync(
            int clientRequestId,
            double wallHeight,
            IEnumerable<DsRoomChangeApplyRoomDto> rooms)
        {
            if (clientRequestId <= 0)
                throw new InvalidOperationException("Не указана заявка");

            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            var payloadRooms = rooms?.Where(r => r != null && r.RoomId > 0).ToList()
                ?? new List<DsRoomChangeApplyRoomDto>();
            if (payloadRooms.Count == 0)
                throw new InvalidOperationException("Нет помещений для отправки");

            var requestBody = new DsRoomChangeApplyRequest
            {
                ClientRequestId = clientRequestId,
                WallHeight = wallHeight > 0d ? wallHeight : null,
                Rooms = payloadRooms
            };

            var json = JsonConvert.SerializeObject(requestBody);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Configs.DsRoomChangeApplyUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка отправки ДС ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<DsRoomChangeApplyResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка отправки ДС" : parsed.Error);

            return parsed.Data ?? new DsRoomChangeApplyDataDto();
        }

        static DsRoomChangeReadResponse ParseResponse(string responseBody)
        {
            var root = JObject.Parse(responseBody);
            return new DsRoomChangeReadResponse
            {
                Status = root["status"]?.Value<bool>() ?? false,
                Error = ReadString(root["error"]),
                RemontId = ReadInt(root["remont_id"]),
                ClientRequestId = ReadInt(root["client_request_id"]),
                DsId = ReadInt(root["ds_id"]),
                Data = ParseBodyToken(root["data"])
            };
        }

        static string ReadString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            return token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString();
        }

        static DsRoomChangeBodyDto ParseBodyToken(JToken dataToken)
        {
            if (dataToken == null || dataToken.Type == JTokenType.Null)
                return null;

            JObject bodyObject;
            if (dataToken.Type == JTokenType.String)
            {
                var raw = dataToken.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                bodyObject = JObject.Parse(raw);
            }
            else if (dataToken.Type == JTokenType.Object)
            {
                bodyObject = (JObject)dataToken;
            }
            else
            {
                return null;
            }

            var body = new DsRoomChangeBodyDto
            {
                DsInfo = bodyObject["ds_info"]?.ToObject<DsRoomChangeInfoDto>(),
                WallHeight = ReadDouble(bodyObject["wall_height"]),
                WallHeightNew = ReadDouble(bodyObject["wall_height_new"])
            };

            var roomsToken = bodyObject["data"];
            if (roomsToken is JArray roomsArray)
            {
                body.Rooms = roomsArray
                    .OfType<JObject>()
                    .Select(ParseRoom)
                    .Where(r => r != null)
                    .ToList();
            }

            return body;
        }

        static DsRoomChangeRoomDto ParseRoom(JObject roomObject)
        {
            if (roomObject == null)
                return null;

            var roomName = roomObject["room_name"]?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(roomName))
                return null;

            return new DsRoomChangeRoomDto
            {
                DsRoomChangeId = ReadInt(roomObject["ds_room_change_id"]),
                RoomId = ReadInt(roomObject["room_id"]),
                RoomName = roomName,
                RoomArea = ReadDouble(roomObject["room_area"]),
                ActionCode = roomObject["action_code"]?.Value<string>(),
                OrderNum = ReadInt(roomObject["order_num"]),
                PrevRoomArea = ReadDouble(roomObject["prev_room_area"])
            };
        }

        static double? ReadDouble(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<double>();

            if (token.Type == JTokenType.String)
            {
                var text = token.Value<string>()?.Trim().Replace(',', '.');
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : null;
            }

            return null;
        }

        static int? ReadInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            if (token.Type == JTokenType.String
                && int.TryParse(token.Value<string>()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;

            return null;
        }

        static DsRoomChangeSnapshot MapSnapshot(DsRoomChangeReadResponse parsed)
        {
            var snapshot = new DsRoomChangeSnapshot();
            var body = parsed.Data;
            var wallHeight = ResolveWallHeight(body);

            if (parsed.DsId == null && (body?.Rooms == null || body.Rooms.Count == 0) && wallHeight == null)
            {
                snapshot.EmptyMessage = "В системе пока нет ДС по изменению площадей для этого ремонта.";
                return snapshot;
            }

            var rooms = body?.Rooms?
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RoomName))
                .ToList() ?? new List<DsRoomChangeRoomDto>();

            if (rooms.Count == 0 && wallHeight == null)
            {
                snapshot.EmptyMessage = "В системе пока нет ДС по изменению площадей для этого ремонта.";
                return snapshot;
            }

            snapshot.HasData = true;
            snapshot.DsId = parsed.DsId ?? body?.DsInfo?.DsId;
            snapshot.DsDate = body?.DsInfo?.DsDate;
            snapshot.DsTypeName = body?.DsInfo?.DsTypeName;
            snapshot.WallHeightM = wallHeight;
            snapshot.Rooms = rooms;
            return snapshot;
        }

        static double? ResolveWallHeight(DsRoomChangeBodyDto body)
        {
            if (body == null)
                return null;

            if (body.WallHeight is > 0d)
                return Math.Round(body.WallHeight.Value, 2);

            if (body.WallHeightNew is > 0d)
                return Math.Round(body.WallHeightNew.Value, 2);

            return null;
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var error = JsonConvert.DeserializeObject<DsRoomChangeReadResponse>(responseBody);
                if (!string.IsNullOrWhiteSpace(error?.Error))
                    return error.Error;
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}
