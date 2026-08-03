using Newtonsoft.Json.Linq;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public class ClientMaterialTkSnapshot
    {
        public bool HasData { get; set; }
        public int? ClientRequestId { get; set; }
        public List<ClientMaterialRowDto> Rows { get; set; } = new();
        public string EmptyMessage { get; set; }
    }

    public static class ClientMaterialTkService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<ClientMaterialTkSnapshot> ReadAsync(int clientRequestId)
        {
            if (clientRequestId <= 0)
                throw new InvalidOperationException("Не указан ID заявки");

            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                Configs.ClientMaterialTkReadUrl(clientRequestId));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка запроса ТК ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            return ParseResponse(responseBody);
        }

        static ClientMaterialTkSnapshot ParseResponse(string responseBody)
        {
            var root = JObject.Parse(responseBody);
            var status = root["status"]?.Value<bool>() ?? false;
            if (!status)
            {
                var error = ReadString(root["error"]) ?? "Ошибка запроса ТК";
                throw new InvalidOperationException(error);
            }

            var snapshot = new ClientMaterialTkSnapshot
            {
                ClientRequestId = ReadInt(root["client_request_id"]),
                Rows = ParseRowsToken(root["data"])
            };

            if (snapshot.Rows.Count == 0)
            {
                snapshot.EmptyMessage = snapshot.ClientRequestId.HasValue
                    ? "В текстовом конструкторе пока нет материалов для этой заявки."
                    : "Ремонт не найден в системе или материалы ТК недоступны.";
                return snapshot;
            }

            snapshot.HasData = true;
            return snapshot;
        }

        static List<ClientMaterialRowDto> ParseRowsToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new List<ClientMaterialRowDto>();

            JArray array;
            if (token.Type == JTokenType.String)
            {
                var raw = token.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return new List<ClientMaterialRowDto>();

                array = JArray.Parse(raw);
            }
            else if (token.Type == JTokenType.Array)
            {
                array = (JArray)token;
            }
            else
            {
                return new List<ClientMaterialRowDto>();
            }

            return array
                .OfType<JObject>()
                .Select(ParseRow)
                .Where(r => r != null)
                .ToList();
        }

        static ClientMaterialRowDto ParseRow(JObject obj)
        {
            if (obj == null)
                return null;

            return new ClientMaterialRowDto
            {
                ClientMaterialId = ReadInt(obj["client_material_id"]),
                RoomId = ReadInt(obj["room_id"]),
                RoomName = ReadString(obj["room_name"]),
                WorkSetId = ReadInt(obj["work_set_id"]),
                WorkSetName = ReadString(obj["work_set_name"]),
                MaterialId = ReadInt(obj["material_id"]),
                MaterialName = ReadString(obj["material_name"]),
                MaterialSetId = ReadInt(obj["material_set_id"]),
                SetName = ReadString(obj["set_name"]),
                IsOptional = ReadInt(obj["is_optional"]) ?? 0
            };
        }

        static string ReadString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            return token.Type == JTokenType.String
                ? token.Value<string>()?.Trim()
                : token.ToString()?.Trim();
        }

        static int? ReadInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            if (token.Type == JTokenType.String
                && int.TryParse(token.Value<string>()?.Trim(), out var value))
                return value;

            return null;
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var root = JObject.Parse(responseBody);
                return ReadString(root["error"]);
            }
            catch
            {
                return null;
            }
        }
    }
}
