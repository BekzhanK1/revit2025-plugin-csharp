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
    public sealed class DsTkChangeItem
    {
        public int DsId { get; init; }
        public string DsDate { get; init; }
        public string DsTypeName { get; init; }
        public string DsTypeCode { get; init; }
        public int? IsAccept { get; init; }
        public int? CardId { get; init; }

        public bool CanEdit =>
            CardId == null
            && IsAccept != 1
            && IsAccept != 2;

        /// <summary>Уже ушла в согласование / утверждена / отказана — новую TK_CHANGE не создаём.</summary>
        public bool IsLocked => !CanEdit;

        public string StatusDisplay
        {
            get
            {
                // Сначала финальный статус, даже если card_id ещё не сброшен.
                if (IsAccept == 1)
                    return "Утверждена";
                if (IsAccept == 2)
                    return "Отказана";
                if (CardId != null)
                    return "На согласовании";
                return "Черновик";
            }
        }

        public string ListLabel
        {
            get
            {
                var date = string.IsNullOrWhiteSpace(DsDate) ? "—" : DsDate.Trim();
                return $"№{DsId} · {date} · {StatusDisplay}";
            }
        }
    }

    public sealed class DsTkChangeBindState
    {
        public List<DsTkChangeItem> Items { get; init; } = new();
        public DsTkChangeItem Selected { get; set; }
        public string Error { get; init; }

        public IReadOnlyList<DsTkChangeItem> EditableItems =>
            Items.Where(i => i.CanEdit).ToList();

        /// <summary>
        /// Автовыбор: ровно один редактируемый черновик; иначе null (пользователь выбирает).
        /// </summary>
        public static DsTkChangeItem PreferAutoSelect(IReadOnlyList<DsTkChangeItem> items)
        {
            var editable = items?.Where(i => i != null && i.CanEdit).ToList()
                ?? new List<DsTkChangeItem>();
            return editable.Count == 1 ? editable[0] : null;
        }

        /// <summary>
        /// Для бейджа хаба: один черновик, иначе самый «интересный» статус (согласование / утверждена / …).
        /// </summary>
        public static DsTkChangeItem PreferHubBadge(IReadOnlyList<DsTkChangeItem> items)
        {
            if (items == null || items.Count == 0)
                return null;

            var editable = items.Where(i => i.CanEdit).OrderByDescending(i => i.DsId).ToList();
            if (editable.Count == 1)
                return editable[0];
            if (editable.Count > 1)
                return null; // несколько черновиков — «выбрать в окне»

            // Нет черновиков — покажем самый свежий locked
            return items.OrderByDescending(i => i.DsId).FirstOrDefault();
        }
    }

    public enum DsTkQtyApplyMode
    {
        /// <summary>Все qty≠ с ведомостью.</summary>
        All,
        /// <summary>Только позиции с is_material_cnt_input (как инпут в MySpace).</summary>
        EditableOnly,
        /// <summary>Объёмы не отправлять.</summary>
        Skip
    }

    public sealed class DsTkQtyApplyCandidate
    {
        public int ClientMaterialId { get; init; }
        public int? MaterialSetId { get; init; }
        public int MaterialId { get; init; }
        public bool IsSetMember { get; init; }
        public int? TkChangeId { get; init; }
        public double? HeadMaterialCnt { get; init; }
        public string MaterialName { get; init; }
        public string RoomName { get; init; }
        public string WorkSetName { get; init; }
        public string QtyUnit { get; init; }
        public double? TkQty { get; init; }
        public double ScheduleQty { get; init; }
        public bool IsMaterialCntInput { get; init; }

        public string MaterialDisplay =>
            string.IsNullOrWhiteSpace(MaterialName)
                ? (MaterialId > 0 ? $"material_id={MaterialId}" : "—")
                : MaterialName.Trim();

        public string RoomDisplay =>
            string.IsNullOrWhiteSpace(RoomName) ? "—" : RoomName.Trim();

        public string WorkSetDisplay =>
            string.IsNullOrWhiteSpace(WorkSetName) ? "—" : WorkSetName.Trim();

        public string QtyUnitDisplay =>
            string.IsNullOrWhiteSpace(QtyUnit) ? "" : QtyUnit.Trim();

        public string TkQtyDisplay =>
            TkQty?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";

        public string ScheduleQtyDisplay =>
            ScheduleQty.ToString("0.##", CultureInfo.InvariantCulture);

        public string DeltaDisplay
        {
            get
            {
                if (TkQty == null)
                    return ScheduleQtyDisplay;
                var delta = ScheduleQty - TkQty.Value;
                var sign = delta > 0 ? "+" : string.Empty;
                return sign + delta.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        public string MyspaceEditableDisplay => IsMaterialCntInput ? "да" : "нет";
    }

    public sealed class DsTkQtyApplyPreview
    {
        public DsTkQtyApplyMode Mode { get; init; }
        public IReadOnlyList<DsTkQtyApplyCandidate> Candidates { get; init; } =
            Array.Empty<DsTkQtyApplyCandidate>();
        public int TotalQtyMismatch { get; init; }
        public int SkippedNotEditable { get; init; }
        public int ProjectAlertCount { get; init; }

        public string ModeTitle => Mode switch
        {
            DsTkQtyApplyMode.EditableOnly => "Разрешённые объёмы (с полем ввода)",
            DsTkQtyApplyMode.All => "Разрешённые объёмы",
            _ => "Объёмы не передаются"
        };

        public string ModeHint => Mode switch
        {
            DsTkQtyApplyMode.EditableOnly =>
                "В ДС уйдут только объёмы, которые можно править в MySpace.",
            DsTkQtyApplyMode.All =>
                "В ДС только позиции с полем ввода. Алерты проекта остаются в сверке.",
            _ => "Объёмы не отправляются."
        };
    }

    public sealed class DsTkQtyApplyResult
    {
        public int Attempted { get; init; }
        public int Succeeded { get; init; }
        public int Failed { get; init; }
        public int Skipped { get; init; }
        public List<string> Errors { get; init; } = new();
    }

    /// <summary>
    /// Office DS API для TK_CHANGE: список / тип / создать пустую / шапка / qty.
    /// </summary>
    public static class DsTkChangeService
    {
        public const string TkChangeTypeCode = "TK_CHANGE";
        public const string QtyUpdGrant = "OA__RemontFormDSTkUpd";

        static readonly HttpClient Http = new HttpClient
        {
            // Office DS не должен подвешивать сверку / хаб на 30с.
            Timeout = TimeSpan.FromSeconds(12)
        };

        // Apply qty возвращает полный TK — 12с мало.
        static readonly HttpClient ApplyHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        static int? _cachedTkChangeTypeId;

        public static async Task<(DsTkChangeBindState State, bool Ok, string Error)> TryListAsync(int clientRequestId)
        {
            try
            {
                var items = await ListTkChangeAsync(clientRequestId).ConfigureAwait(false);
                var state = new DsTkChangeBindState
                {
                    Items = items,
                    Selected = DsTkChangeBindState.PreferAutoSelect(items)
                };
                return (state, true, null);
            }
            catch (Exception ex)
            {
                return (new DsTkChangeBindState { Error = ex.Message }, false, ex.Message);
            }
        }

        public static async Task<List<DsTkChangeItem>> ListTkChangeAsync(int clientRequestId)
        {
            EnsureRequest(clientRequestId);
            var session = RequireSession();

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                Configs.ClientRequestDsListUrl(clientRequestId));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            EnsureSuccess(response, body, "списка ДС");

            return ParseDsList(body)
                .Where(IsTkChangeRow)
                .OrderByDescending(i => i.DsId)
                .ToList();
        }

        static bool IsTkChangeRow(DsTkChangeItem item)
        {
            if (item == null)
                return false;
            if (string.Equals(item.DsTypeCode, TkChangeTypeCode, StringComparison.OrdinalIgnoreCase))
                return true;
            // На случай если SP не отдал ds_type_code.
            var name = item.DsTypeName ?? string.Empty;
            return name.IndexOf("TK_CHANGE", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("текстов", StringComparison.OrdinalIgnoreCase) >= 0
                   || (name.IndexOf("ТК", StringComparison.OrdinalIgnoreCase) >= 0
                       && name.IndexOf("изменен", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static async Task<int> GetTkChangeTypeIdAsync()
        {
            if (_cachedTkChangeTypeId is > 0)
                return _cachedTkChangeTypeId.Value;

            var session = RequireSession();
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, Configs.DsTypesReadUrl);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            EnsureSuccess(response, body, "типов ДС");

            var root = JObject.Parse(body);
            if (root["status"]?.Value<bool>() == false)
                throw new InvalidOperationException(ReadError(root) ?? "Ошибка загрузки типов ДС");

            var data = root["data"] as JArray
                ?? (root["data"]?["data"] as JArray);

            if (data == null)
                throw new InvalidOperationException("Сервер не вернул типы ДС");

            foreach (var token in data.OfType<JObject>())
            {
                var code = ReadString(token["ds_type_code"]);
                if (!string.Equals(code, TkChangeTypeCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = ReadInt(token["ds_type_id"]);
                if (id is > 0)
                {
                    _cachedTkChangeTypeId = id;
                    return id.Value;
                }
            }

            throw new InvalidOperationException("Тип ДС TK_CHANGE не найден в справочнике");
        }

        public static async Task<DsTkChangeItem> CreateEmptyAsync(int clientRequestId)
        {
            EnsureRequest(clientRequestId);
            var session = RequireSession();
            var typeId = await GetTkChangeTypeIdAsync().ConfigureAwait(false);

            var payload = new JObject { ["ds_type_id"] = typeId };
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                Configs.ClientRequestDsAddUrl(clientRequestId));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            httpRequest.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            EnsureSuccess(response, body, "создания ДС");

            var root = JObject.Parse(body);
            if (root["status"]?.Value<bool>() == false)
                throw new InvalidOperationException(ReadError(root) ?? "Ошибка создания ДС");

            var header = root["header"] as JObject
                ?? root["data"]?["header"] as JObject
                ?? root["data"] as JObject;

            var item = ParseItem(header);
            if (item == null || item.DsId <= 0)
                throw new InvalidOperationException("Сервер не вернул ds_id созданной ДС");

            // Тип мог не прийти в header — дополним.
            if (string.IsNullOrWhiteSpace(item.DsTypeCode))
            {
                item = new DsTkChangeItem
                {
                    DsId = item.DsId,
                    DsDate = item.DsDate,
                    DsTypeName = string.IsNullOrWhiteSpace(item.DsTypeName) ? "Изменение ТК" : item.DsTypeName,
                    DsTypeCode = TkChangeTypeCode,
                    IsAccept = item.IsAccept,
                    CardId = item.CardId
                };
            }

            return item;
        }

        public static async Task<DsTkChangeItem> ReadHeaderAsync(int clientRequestId, int dsId)
        {
            EnsureRequest(clientRequestId);
            if (dsId <= 0)
                throw new InvalidOperationException("Не указан ID ДС");

            var session = RequireSession();
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                Configs.ClientRequestDsUrl(clientRequestId, dsId));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            EnsureSuccess(response, body, "чтения ДС");

            var root = JObject.Parse(body);
            if (root["status"]?.Value<bool>() == false)
                throw new InvalidOperationException(ReadError(root) ?? "Ошибка чтения ДС");

            var header = root["header"] as JObject
                ?? root["data"]?["header"] as JObject
                ?? root["data"]?["ds_info"] as JObject;

            var item = ParseItem(header);
            if (item == null || item.DsId <= 0)
            {
                item = ParseItem(root["data"] as JObject);
            }

            if (item == null || item.DsId <= 0)
                throw new InvalidOperationException("Сервер не вернул шапку ДС");

            if (string.IsNullOrWhiteSpace(item.DsTypeCode))
            {
                item = new DsTkChangeItem
                {
                    DsId = item.DsId,
                    DsDate = item.DsDate,
                    DsTypeName = item.DsTypeName,
                    DsTypeCode = TkChangeTypeCode,
                    IsAccept = item.IsAccept,
                    CardId = item.CardId
                };
            }

            return item;
        }

        public sealed class DsTkMaterialRow
        {
            public int ClientMaterialId { get; init; }
            public int? MaterialId { get; init; }
            public int? MaterialSetId { get; init; }
            public string RoomName { get; init; }
            public string MaterialName { get; init; }
            public string WorkSetName { get; init; }
            public double? MaterialCnt { get; init; }
            public bool IsMaterialCntInput { get; init; }
            public int? ActionType { get; init; }
            public int? TkChangeId { get; init; }
            public List<ClientMaterialSetItemDto> SetItems { get; init; }
        }

        /// <summary>
        /// Материалы ДС с эффективным qty (уже с учётом tk_change), как в MySpace.
        /// </summary>
        public static async Task<List<DsTkMaterialRow>> ReadTkMaterialsAsync(int clientRequestId, int dsId)
        {
            EnsureRequest(clientRequestId);
            if (dsId <= 0)
                throw new InvalidOperationException("Не указан ID ДС");

            var session = RequireSession();
            var url = Configs.ClientRequestDsTkMaterialUrl(clientRequestId, dsId);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            EnsureSuccess(response, body, "материалов ДС ТК");

            ExportRoomsApplication._logger?.Information(
                "DS TK materials read cr={ClientRequestId} ds={DsId} http={Status} bytes={Bytes}",
                clientRequestId,
                dsId,
                (int)response.StatusCode,
                body?.Length ?? 0);

            return ParseTkMaterials(body);
        }

        /// <summary>
        /// Подменяет qty / состав набора в снимке ТК значениями из ДС по (client_material_id, material_id).
        /// </summary>
        public static ClientMaterialTkSnapshot ApplyDsQtyOverlay(
            ClientMaterialTkSnapshot tk,
            IReadOnlyList<DsTkMaterialRow> dsRows)
        {
            if (tk?.Rows == null || tk.Rows.Count == 0 || dsRows == null || dsRows.Count == 0)
                return tk;

            var byCm = dsRows
                .Where(r => r != null && r.ClientMaterialId > 0)
                .GroupBy(r => r.ClientMaterialId)
                .ToDictionary(g => g.Key, g => g.First());

            var byKey = dsRows
                .Where(r => r != null && r.ClientMaterialId > 0 && r.MaterialId is > 0)
                .GroupBy(r => (r.ClientMaterialId, r.MaterialId!.Value))
                .ToDictionary(g => g.Key, g => g.First());

            var applied = 0;
            foreach (var row in tk.Rows)
            {
                if (row?.ClientMaterialId is not > 0)
                    continue;
                if (!byCm.TryGetValue(row.ClientMaterialId.Value, out var ds))
                    continue;

                if (ds.ActionType == 1)
                {
                    row.IsMaterialCntInput = false;
                    applied++;
                    continue;
                }

                row.IsMaterialCntInput = ds.IsMaterialCntInput;
                if (ds.MaterialSetId is > 0)
                    row.MaterialSetId = ds.MaterialSetId;
                if (ds.TkChangeId is > 0)
                    row.TkChangeId = ds.TkChangeId;

                if (!row.IsSetMember)
                {
                    if (ds.MaterialCnt != null)
                        row.MaterialCnt = ds.MaterialCnt;
                    if (ds.SetItems is { Count: > 0 })
                        row.SetItems = ds.SetItems;
                    applied++;
                    continue;
                }

                if (row.MaterialId is > 0
                    && byKey.TryGetValue((row.ClientMaterialId.Value, row.MaterialId.Value), out var dsItem)
                    && dsItem.MaterialCnt != null)
                {
                    row.MaterialCnt = dsItem.MaterialCnt;
                    applied++;
                    continue;
                }

                var fromSet = ds.SetItems?
                    .FirstOrDefault(i => i.MaterialId == row.MaterialId);
                if (fromSet?.MaterialCnt != null)
                    row.MaterialCnt = fromSet.MaterialCnt;
                applied++;
            }

            ExportRoomsApplication._logger?.Information(
                "DS TK qty overlay applied={Applied} dsRows={DsRows} tkRows={TkRows}",
                applied,
                dsRows.Count,
                tk.Rows.Count);

            return tk;
        }

        static List<DsTkMaterialRow> ParseTkMaterials(string responseBody)
        {
            var list = new List<DsTkMaterialRow>();
            if (string.IsNullOrWhiteSpace(responseBody))
                return list;

            var root = JObject.Parse(responseBody);
            if (root["status"]?.Value<bool>() == false)
                throw new InvalidOperationException(ReadError(root) ?? "Ошибка чтения материалов ДС");

            var data = root["data"];
            if (data is JObject dataObj && dataObj["data"] != null)
                data = dataObj["data"];

            if (data is not JArray array)
                return list;

            foreach (var token in array.OfType<JObject>())
            {
                var cm = ReadInt(token["client_material_id"]) ?? 0;
                if (cm <= 0)
                    continue;

                list.Add(new DsTkMaterialRow
                {
                    ClientMaterialId = cm,
                    MaterialId = ReadInt(token["material_id"]),
                    MaterialSetId = ReadInt(token["material_set_id"]),
                    RoomName = ReadString(token["room_name"]),
                    MaterialName = ReadString(token["material_name"]),
                    WorkSetName = ReadString(token["work_set_name"]),
                    MaterialCnt = ReadDouble(token["material_cnt"]),
                    IsMaterialCntInput = ReadBool(token["is_material_cnt_input"]) == true,
                    ActionType = ReadInt(token["action_type"]),
                    TkChangeId = ReadInt(token["tk_change_id"]),
                    SetItems = ClientMaterialTkService.ParseSetItems(token)
                });
            }

            return list;
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
            }

            return null;
        }

        static double? ReadDouble(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (token.Type is JTokenType.Float or JTokenType.Integer)
                return token.Value<double>();
            if (token.Type == JTokenType.String
                && double.TryParse(
                    token.Value<string>()?.Trim()?.Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
                return value;
            return null;
        }

        public static IReadOnlyList<DsTkQtyApplyCandidate> CollectQtyCandidates(
            DsTkCompareResult compare,
            DsTkQtyApplyMode mode)
        {
            return BuildQtyApplyPreview(compare, mode).Candidates;
        }

        public static DsTkQtyApplyPreview BuildQtyApplyPreview(
            DsTkCompareResult compare,
            DsTkQtyApplyMode mode)
        {
            if (compare?.Rooms == null)
            {
                return new DsTkQtyApplyPreview
                {
                    Mode = mode,
                    Candidates = Array.Empty<DsTkQtyApplyCandidate>(),
                    TotalQtyMismatch = 0,
                    SkippedNotEditable = 0,
                    ProjectAlertCount = 0
                };
            }

            var projectAlerts = compare.QtyProjectAlertCount;
            var allEligible = new List<DsTkQtyApplyCandidate>();
            foreach (var room in compare.Rooms)
            {
                if (room?.Rows == null)
                    continue;

                foreach (var row in room.Rows)
                {
                    // В ДС шлём только то, что MySpace позволяет править.
                    if (row == null || row.QtyStatusKey != "qty_mismatch")
                        continue;
                    if (!row.IsMaterialCntInput)
                        continue;
                    if (row.ClientMaterialId is not > 0)
                        continue;
                    if (row.ScheduleQty == null)
                        continue;

                    allEligible.Add(new DsTkQtyApplyCandidate
                    {
                        ClientMaterialId = row.ClientMaterialId.Value,
                        MaterialSetId = row.MaterialSetId is > 0 ? row.MaterialSetId : null,
                        MaterialId = row.MaterialId,
                        IsSetMember = row.IsSetMember,
                        TkChangeId = row.TkChangeId,
                        HeadMaterialCnt = FindHeadQty(compare, row.ClientMaterialId.Value),
                        MaterialName = row.MaterialName,
                        RoomName = room.RoomName,
                        WorkSetName = row.WorkSetName,
                        QtyUnit = row.QtyUnit,
                        TkQty = row.TkQty,
                        ScheduleQty = row.ScheduleQty.Value,
                        IsMaterialCntInput = true
                    });
                }
            }

            // Шапка и элемент набора — разные позиции (один cm, разные material_id).
            allEligible = allEligible
                .GroupBy(c => (c.ClientMaterialId, c.MaterialId))
                .Select(g => g.First())
                .OrderBy(c => c.RoomDisplay, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.MaterialDisplay, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = allEligible.Count;
            if (mode == DsTkQtyApplyMode.Skip)
            {
                return new DsTkQtyApplyPreview
                {
                    Mode = mode,
                    Candidates = Array.Empty<DsTkQtyApplyCandidate>(),
                    TotalQtyMismatch = total,
                    SkippedNotEditable = 0,
                    ProjectAlertCount = projectAlerts
                };
            }

            // All и EditableOnly — одно и то же: только MySpace-input.
            return new DsTkQtyApplyPreview
            {
                Mode = mode,
                Candidates = allEligible,
                TotalQtyMismatch = total,
                SkippedNotEditable = projectAlerts,
                ProjectAlertCount = projectAlerts
            };
        }

        static double? FindHeadQty(DsTkCompareResult compare, int clientMaterialId)
        {
            foreach (var room in compare?.Rooms ?? Enumerable.Empty<DsTkCompareRoom>())
            {
                var head = room.Rows?.FirstOrDefault(r =>
                    r.ClientMaterialId == clientMaterialId && !r.IsSetMember);
                if (head != null)
                    return head.TkQty;
            }

            return null;
        }

        public static async Task<DsTkQtyApplyResult> ApplyQtyAsync(
            int clientRequestId,
            int dsId,
            IReadOnlyList<DsTkQtyApplyCandidate> candidates,
            DsTkCompareResult compare = null)
        {
            EnsureRequest(clientRequestId);
            if (dsId <= 0)
                throw new InvalidOperationException("Не указан ID ДС");
            if (candidates == null || candidates.Count == 0)
            {
                return new DsTkQtyApplyResult
                {
                    Attempted = 0,
                    Succeeded = 0,
                    Failed = 0,
                    Skipped = 0
                };
            }

            var session = RequireSession();
            if (!session.HasGrant(QtyUpdGrant))
                throw new InvalidOperationException($"Нет права {QtyUpdGrant} — изменение объёмов в ДС недоступно.");

            var url = Configs.ClientRequestDsTkChangeSetItemCntUrl(clientRequestId);
            ExportRoomsApplication._logger?.Information(
                "DS TK qty apply START cr={ClientRequestId} ds={DsId} count={Count} url={Url} apiOrigin={ApiOrigin}",
                clientRequestId,
                dsId,
                candidates.Count,
                url,
                Configs.ApiOriginUrl);

            var succeeded = 0;
            var failed = 0;
            var errors = new List<string>();

            foreach (var group in candidates.GroupBy(c => c.ClientMaterialId))
            {
                var items = group.ToList();
                var first = items[0];
                try
                {
                    if (first.MaterialSetId is > 0)
                    {
                        var payload = BuildSetItemPayload(items, compare);
                        await SetItemCntAsync(
                                clientRequestId,
                                dsId,
                                first,
                                session.AccessToken,
                                payload)
                            .ConfigureAwait(false);
                        succeeded += items.Count;
                    }
                    else
                    {
                        foreach (var item in items)
                        {
                            await SetItemCntAsync(clientRequestId, dsId, item, session.AccessToken)
                                .ConfigureAwait(false);
                            succeeded++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed += items.Count;
                    var label = string.IsNullOrWhiteSpace(first.MaterialName)
                        ? $"material_id={first.MaterialId}"
                        : first.MaterialName.Trim();
                    var room = first.RoomDisplay;
                    errors.Add($"{room}: {label} — {ex.Message}");
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "DS TK set item cnt failed ds={DsId} cm={ClientMaterialId} material={MaterialId} room={Room}",
                        dsId,
                        first.ClientMaterialId,
                        first.MaterialId,
                        room);
                }
            }

            ExportRoomsApplication._logger?.Information(
                "DS TK qty apply END cr={ClientRequestId} ds={DsId} attempted={Attempted} ok={Succeeded} fail={Failed}",
                clientRequestId,
                dsId,
                candidates.Count,
                succeeded,
                failed);

            return new DsTkQtyApplyResult
            {
                Attempted = candidates.Count,
                Succeeded = succeeded,
                Failed = failed,
                Skipped = 0,
                Errors = errors
            };
        }

        sealed class SetItemCntPayload
        {
            public double? HeadCnt { get; init; }
            public List<int> MaterialIds { get; init; } = new();
            public List<double> MaterialCnts { get; init; } = new();
        }

        static SetItemCntPayload BuildSetItemPayload(
            IReadOnlyList<DsTkQtyApplyCandidate> group,
            DsTkCompareResult compare)
        {
            var first = group[0];
            var changed = group
                .Where(c => c.MaterialId > 0)
                .GroupBy(c => c.MaterialId)
                .ToDictionary(g => g.Key, g => g.First().ScheduleQty);

            var members = new List<DsTkCompareRow>();
            DsTkCompareRow head = null;
            foreach (var room in compare?.Rooms ?? Enumerable.Empty<DsTkCompareRoom>())
            {
                if (room?.Rows == null)
                    continue;
                foreach (var row in room.Rows)
                {
                    if (row.ClientMaterialId != first.ClientMaterialId)
                        continue;
                    if (row.IsSetMember && row.MaterialId > 0)
                        members.Add(row);
                    else if (!row.IsSetMember)
                        head ??= row;
                }
            }

            var isUpdate = first.TkChangeId is > 0;
            var toSend = isUpdate
                ? members.Where(m => changed.ContainsKey(m.MaterialId)).ToList()
                : members;

            var ids = new List<int>();
            var cnts = new List<double>();
            foreach (var row in toSend)
            {
                ids.Add(row.MaterialId);
                cnts.Add(changed.TryGetValue(row.MaterialId, out var qty) ? qty : row.TkQty ?? 0);
            }

            if (ids.Count == 0)
            {
                foreach (var item in group)
                {
                    if (item.MaterialId <= 0)
                        continue;
                    ids.Add(item.MaterialId);
                    cnts.Add(item.ScheduleQty);
                }
            }

            var headCnt = first.HeadMaterialCnt ?? head?.TkQty;
            var headChange = group.FirstOrDefault(c => !c.IsSetMember);
            if (headChange != null)
                headCnt = headChange.ScheduleQty;

            return new SetItemCntPayload
            {
                HeadCnt = headCnt,
                MaterialIds = ids,
                MaterialCnts = cnts
            };
        }

        static async Task SetItemCntAsync(
            int clientRequestId,
            int dsId,
            DsTkQtyApplyCandidate item,
            string accessToken,
            SetItemCntPayload setPayload = null)
        {
            var url = Configs.ClientRequestDsTkChangeSetItemCntUrl(clientRequestId);
            var idArr = new JArray();
            var cntArr = new JArray();
            if (setPayload != null)
            {
                for (var i = 0; i < setPayload.MaterialIds.Count; i++)
                {
                    idArr.Add(setPayload.MaterialIds[i]);
                    cntArr.Add(setPayload.MaterialCnts[i]);
                }
            }
            else if (item.IsSetMember && item.MaterialId > 0)
            {
                idArr.Add(item.MaterialId);
                cntArr.Add(item.ScheduleQty);
            }

            var headCnt = setPayload?.HeadCnt
                ?? item.HeadMaterialCnt
                ?? (item.IsSetMember ? (double?)null : item.ScheduleQty)
                ?? item.ScheduleQty;

            var payload = new JObject
            {
                ["ds_id"] = dsId,
                ["client_material_id"] = item.ClientMaterialId,
                ["cnt_material_cnt"] = headCnt,
                ["cnt_material_set_id"] = item.MaterialSetId,
                ["cnt_action_type"] = null,
                ["cnt_tk_change_id"] = item.TkChangeId,
                ["cnt_material_id_arr"] = idArr,
                ["cnt_material_cnt_arr"] = cntArr
            };

            var payloadJson = payload.ToString(Formatting.None);
            ExportRoomsApplication._logger?.Information(
                "DS TK qty REQUEST POST {Url} cm={ClientMaterialId} material={MaterialId} room={Room} tk={TkQty} schedule={ScheduleQty} unit={Unit} set={MaterialSetId} body={Body}",
                url,
                item.ClientMaterialId,
                item.MaterialId,
                item.RoomDisplay,
                item.TkQty,
                item.ScheduleQty,
                item.QtyUnitDisplay,
                item.MaterialSetId,
                payloadJson);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await ApplyHttp.SendAsync(httpRequest).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var bodyForLog = TruncateForLog(body, 6000);

            ExportRoomsApplication._logger?.Information(
                "DS TK qty RESPONSE HTTP {StatusCode} cm={ClientMaterialId} bytes={Bytes} body={Body}",
                (int)response.StatusCode,
                item.ClientMaterialId,
                body?.Length ?? 0,
                bodyForLog);

            EnsureSuccess(response, body, "изменения объёма в ДС");

            if (string.IsNullOrWhiteSpace(body))
            {
                ExportRoomsApplication._logger?.Warning(
                    "DS TK qty RESPONSE empty body cm={ClientMaterialId}",
                    item.ClientMaterialId);
                return;
            }

            try
            {
                var root = JObject.Parse(body);
                if (root["status"]?.Value<bool>() == false)
                    throw new InvalidOperationException(ReadError(root) ?? "Ошибка изменения объёма");

                var echo = TrySummarizeMaterialEcho(root, item.ClientMaterialId);
                if (!string.IsNullOrWhiteSpace(echo))
                {
                    ExportRoomsApplication._logger?.Information(
                        "DS TK qty RESPONSE echo cm={ClientMaterialId}: {Echo}",
                        item.ClientMaterialId,
                        echo);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "DS TK qty RESPONSE parse warning cm={ClientMaterialId}",
                    item.ClientMaterialId);
            }
        }

        static string TrySummarizeMaterialEcho(JObject root, int clientMaterialId)
        {
            if (root == null || clientMaterialId <= 0)
                return null;

            JArray rows = null;
            if (root["data"] is JArray direct)
                rows = direct;
            else if (root["data"] is JObject dataObj && dataObj["data"] is JArray nested)
                rows = nested;

            if (rows == null)
                return null;

            foreach (var token in rows.OfType<JObject>())
            {
                var cm = ReadInt(token["client_material_id"]);
                if (cm != clientMaterialId)
                    continue;

                var parts = new List<string>
                {
                    $"material_cnt={ReadString(token["material_cnt"]) ?? "null"}",
                    $"material_new_cnt={ReadString(token["material_new_cnt"]) ?? "null"}",
                    $"action_type={ReadString(token["action_type"]) ?? "null"}",
                    $"tk_change_id={ReadString(token["tk_change_id"]) ?? "null"}",
                    $"is_material_cnt_input={ReadString(token["is_material_cnt_input"]) ?? "null"}"
                };
                return string.Join(", ", parts);
            }

            return $"client_material_id={clientMaterialId} not found in response data";
        }

        static string TruncateForLog(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            if (value.Length <= maxChars)
                return value;
            return value.Substring(0, maxChars) + $"…(+{value.Length - maxChars} chars)";
        }

        static List<DsTkChangeItem> ParseDsList(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return new List<DsTkChangeItem>();

            JToken rootToken;
            try
            {
                rootToken = JToken.Parse(responseBody);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Некорректный JSON списка ДС: " + ex.Message);
            }

            if (rootToken is not JObject root)
                return new List<DsTkChangeItem>();

            if (root["status"]?.Value<bool>() == false)
                throw new InvalidOperationException(ReadError(root) ?? "Ошибка списка ДС");

            var items = new List<DsTkChangeItem>();

            // data: [ { grant_code, data: [ ds rows ] } ]
            if (root["data"] is JArray blocks)
            {
                foreach (var block in blocks.OfType<JObject>())
                {
                    var rows = block["data"] as JArray;
                    if (rows != null)
                    {
                        foreach (var row in rows.OfType<JObject>())
                        {
                            var item = ParseItem(row);
                            if (item != null && item.DsId > 0)
                                items.Add(item);
                        }
                        continue;
                    }

                    var direct = ParseItem(block);
                    if (direct != null && direct.DsId > 0)
                        items.Add(direct);
                }
            }
            else if (root["data"] is JObject dataObj)
            {
                // Иногда data — объект с вложенным списком.
                foreach (var prop in dataObj.Properties())
                {
                    if (prop.Value is not JArray arr)
                        continue;
                    foreach (var row in arr.OfType<JObject>())
                    {
                        var item = ParseItem(row);
                        if (item != null && item.DsId > 0)
                            items.Add(item);
                    }
                }
            }

            return items
                .GroupBy(i => i.DsId)
                .Select(g => g.First())
                .ToList();
        }

        static DsTkChangeItem ParseItem(JObject obj)
        {
            if (obj == null)
                return null;

            var dsId = ReadInt(obj["ds_id"]);
            if (dsId is null or <= 0)
                return null;

            return new DsTkChangeItem
            {
                DsId = dsId.Value,
                DsDate = ReadString(obj["ds_date"]),
                DsTypeName = ReadString(obj["ds_type_name"]),
                DsTypeCode = ReadString(obj["ds_type_code"]),
                IsAccept = ReadInt(obj["is_accept"]),
                CardId = ReadInt(obj["card_id"])
            };
        }

        static Models.AuthSession RequireSession()
        {
            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");
            return session;
        }

        static void EnsureRequest(int clientRequestId)
        {
            if (clientRequestId <= 0)
                throw new InvalidOperationException("Не указан ID заявки");
        }

        static void EnsureSuccess(HttpResponseMessage response, string body, string action)
        {
            if (response.IsSuccessStatusCode)
                return;

            var message = TryReadErrorMessage(body) ?? $"Ошибка {action} ({(int)response.StatusCode})";
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                message = "Сессия истекла. Выйдите и войдите снова.";
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                message = "Нет прав на работу с ДС (нужны права MySpace на доп. соглашения).";
            throw new InvalidOperationException(message);
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;
            try
            {
                var root = JObject.Parse(responseBody);
                return ReadError(root);
            }
            catch
            {
                return null;
            }
        }

        static string ReadError(JObject root)
        {
            var err = ReadString(root?["error"]);
            if (!string.IsNullOrWhiteSpace(err))
                return err;
            return ReadString(root?["detail"]) ?? ReadString(root?["message"]);
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
                && int.TryParse(token.Value<string>()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            return null;
        }
    }
}
