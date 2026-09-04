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
                Configs.TkReadUrl(clientRequestId));
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

            var snapshot = ParseResponse(responseBody);
            if (snapshot.HasData)
                await EnrichIsMaterialCntInputAsync(snapshot.Rows, session.AccessToken).ConfigureAwait(false);

            var setsWithoutItems = snapshot.Rows.Count(r =>
                r?.MaterialSetId is > 0 && (r.SetItems == null || r.SetItems.Count == 0));
            ExportRoomsApplication._logger?.Information(
                "TK read cr={ClientRequestId} rows={Rows} sets={Sets} sets_without_items={Bare}",
                clientRequestId,
                snapshot.Rows.Count,
                snapshot.Rows.Count(r => r?.MaterialSetId is > 0),
                setsWithoutItems);

            return snapshot;
        }

        public static ClientMaterialTkSnapshot FlattenSets(ClientMaterialTkSnapshot snapshot)
        {
            if (snapshot?.Rows == null || snapshot.Rows.Count == 0)
                return snapshot;

            snapshot.Rows = FlattenSetRows(snapshot.Rows);
            return snapshot;
        }

        public static List<ClientMaterialRowDto> FlattenSetRows(IReadOnlyList<ClientMaterialRowDto> rows)
        {
            var result = new List<ClientMaterialRowDto>();
            if (rows == null || rows.Count == 0)
                return result;

            foreach (var row in rows)
            {
                if (row == null)
                    continue;

                if (row.IsSetMember)
                {
                    result.Add(CloneRow(row));
                    continue;
                }

                var head = CloneRow(row);
                result.Add(head);

                var items = row.SetItems;
                if (items == null || items.Count == 0)
                    continue;

                var seen = new HashSet<int>();
                if (head.MaterialId is > 0)
                    seen.Add(head.MaterialId.Value);

                foreach (var item in items)
                {
                    if (item?.MaterialId is not > 0)
                        continue;
                    if (!seen.Add(item.MaterialId.Value))
                        continue;

                    result.Add(new ClientMaterialRowDto
                    {
                        ClientMaterialId = head.ClientMaterialId,
                        RoomId = head.RoomId,
                        RoomName = head.RoomName,
                        WorkSetId = head.WorkSetId,
                        WorkSetName = head.WorkSetName,
                        MaterialId = item.MaterialId,
                        MaterialName = string.IsNullOrWhiteSpace(item.MaterialName)
                            ? $"material_id={item.MaterialId}"
                            : item.MaterialName,
                        MaterialSetId = head.MaterialSetId,
                        SetName = head.SetName,
                        MaterialCnt = item.MaterialCnt,
                        IsMaterialCntInput = head.IsMaterialCntInput,
                        IsOptional = head.IsOptional,
                        IsSetMember = true,
                        TkChangeId = head.TkChangeId
                    });
                }
            }

            return result;
        }

        public static ClientMaterialRowDto CloneRow(ClientMaterialRowDto row)
        {
            if (row == null)
                return null;

            return new ClientMaterialRowDto
            {
                ClientMaterialId = row.ClientMaterialId,
                RoomId = row.RoomId,
                RoomName = row.RoomName,
                WorkSetId = row.WorkSetId,
                WorkSetName = row.WorkSetName,
                MaterialId = row.MaterialId,
                MaterialName = row.MaterialName,
                MaterialSetId = row.MaterialSetId,
                SetName = row.SetName,
                MaterialCnt = row.MaterialCnt,
                IsMaterialCntInput = row.IsMaterialCntInput,
                IsOptional = row.IsOptional,
                IsSetMember = row.IsSetMember,
                TkChangeId = row.TkChangeId,
                SetItems = CloneSetItems(row.SetItems)
            };
        }

        static List<ClientMaterialSetItemDto> CloneSetItems(List<ClientMaterialSetItemDto> items)
        {
            if (items == null || items.Count == 0)
                return items;

            return items
                .Where(i => i != null)
                .Select(i => new ClientMaterialSetItemDto
                {
                    MaterialId = i.MaterialId,
                    MaterialName = i.MaterialName,
                    MaterialCnt = i.MaterialCnt
                })
                .ToList();
        }

        public static List<ClientMaterialSetItemDto> ParseSetItems(JObject obj)
        {
            if (obj == null)
                return new List<ClientMaterialSetItemDto>();

            var token = obj["items_json"] ?? obj["items"] ?? obj["set_items"];
            return ParseSetItemsToken(token);
        }

        public static List<ClientMaterialSetItemDto> ParseSetItemsToken(JToken token)
        {
            var list = new List<ClientMaterialSetItemDto>();
            if (token == null || token.Type == JTokenType.Null)
                return list;

            if (token.Type == JTokenType.String)
            {
                var raw = token.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return list;
                try
                {
                    token = JToken.Parse(raw);
                }
                catch
                {
                    return list;
                }
            }

            if (token is JArray array)
            {
                foreach (var item in array.OfType<JObject>())
                {
                    var parsed = ParseSetItem(item);
                    if (parsed != null)
                        list.Add(parsed);
                }

                return list;
            }

            if (token is JObject map)
            {
                foreach (var prop in map.Properties())
                {
                    if (!int.TryParse(prop.Name, out var materialId) || materialId <= 0)
                        continue;
                    list.Add(new ClientMaterialSetItemDto
                    {
                        MaterialId = materialId,
                        MaterialCnt = ReadDouble(prop.Value)
                    });
                }
            }

            return list;
        }

        static ClientMaterialSetItemDto ParseSetItem(JObject obj)
        {
            var materialId = ReadInt(obj["material_id"]);
            if (materialId is not > 0)
                return null;

            return new ClientMaterialSetItemDto
            {
                MaterialId = materialId,
                MaterialName = ReadString(obj["material_name"]),
                MaterialCnt = ReadDouble(obj["material_cnt"])
            };
        }

        /// <summary>
        /// TK read часто не отдаёт флаг явно — добираем с work_set_tab через /common/work_sets/read/.
        /// </summary>
        static async Task EnrichIsMaterialCntInputAsync(List<ClientMaterialRowDto> rows, string accessToken)
        {
            if (rows == null || rows.Count == 0 || string.IsNullOrWhiteSpace(accessToken))
                return;

            Dictionary<int, bool> map = null;
            var needsLookup = rows.Any(r => r.IsMaterialCntInput == null && r.WorkSetId is > 0);
            if (needsLookup)
            {
                try
                {
                    map = await GetWorkSetCntInputMapAsync(accessToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "Failed to load work_sets for is_material_cnt_input");
                }
            }

            foreach (var row in rows)
            {
                if (row.IsMaterialCntInput != null)
                    continue;

                if (row.WorkSetId is > 0
                    && map != null
                    && map.TryGetValue(row.WorkSetId.Value, out var flag))
                {
                    row.IsMaterialCntInput = flag;
                }
                else
                {
                    // Без флага — как «нельзя вводить» в MySpace.
                    row.IsMaterialCntInput = false;
                }
            }
        }

        static Dictionary<int, bool> _workSetCntInputCache;
        static readonly object WorkSetCacheLock = new();

        static async Task<Dictionary<int, bool>> GetWorkSetCntInputMapAsync(string accessToken)
        {
            lock (WorkSetCacheLock)
            {
                if (_workSetCntInputCache != null)
                    return _workSetCntInputCache;
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, Configs.WorkSetsReadUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(body)
                    ?? $"Ошибка справочника work_set ({(int)response.StatusCode})";
                throw new InvalidOperationException(message);
            }

            var map = ParseWorkSetCntInputMap(body);
            lock (WorkSetCacheLock)
            {
                _workSetCntInputCache ??= map;
                return _workSetCntInputCache;
            }
        }

        static Dictionary<int, bool> ParseWorkSetCntInputMap(string responseBody)
        {
            var map = new Dictionary<int, bool>();
            if (string.IsNullOrWhiteSpace(responseBody))
                return map;

            var root = JObject.Parse(responseBody);
            var data = root["data"];
            if (data is JObject dataObj && dataObj["data"] != null)
                data = dataObj["data"];

            if (data is not JArray array)
                return map;

            foreach (var token in array.OfType<JObject>())
            {
                var id = ReadInt(token["work_set_id"]);
                if (id is not > 0)
                    continue;
                map[id.Value] = ReadBool(token["is_material_cnt_input"]) == true;
            }

            return map;
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
                MaterialCnt = ReadDouble(obj["material_cnt"]),
                IsMaterialCntInput = ReadBool(obj["is_material_cnt_input"]),
                IsOptional = ReadInt(obj["is_optional"]) ?? 0,
                TkChangeId = ReadInt(obj["tk_change_id"]),
                SetItems = ParseSetItems(obj)
            };
        }

        static bool? ReadBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            if (token.Type == JTokenType.Integer)
                return token.Value<int>() != 0;

            if (token.Type == JTokenType.Float)
                return Math.Abs(token.Value<double>()) > double.Epsilon;

            if (token.Type == JTokenType.String)
            {
                var raw = token.Value<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return null;
                if (bool.TryParse(raw, out var b))
                    return b;
                if (int.TryParse(raw, out var i))
                    return i != 0;
                if (string.Equals(raw, "t", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "да", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(raw, "f", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "нет", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return null;
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

            if (token.Type == JTokenType.Float)
                return (int)token.Value<double>();

            if (token.Type == JTokenType.String
                && int.TryParse(token.Value<string>()?.Trim(), out var value))
                return value;

            return null;
        }

        static double? ReadDouble(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<double>();

            if (token.Type == JTokenType.String
                && double.TryParse(
                    token.Value<string>()?.Trim()?.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
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
