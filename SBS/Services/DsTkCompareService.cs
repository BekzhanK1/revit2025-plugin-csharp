using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public enum DsTkCompareStatus
    {
        /// <summary>Есть в договоре (ТК) и в проекте Revit.</summary>
        Match,
        /// <summary>Есть в договоре, нет в проекте (должен быть rfa/surface).</summary>
        MissingInRevit,
        /// <summary>Есть в договоре, в модели не ожидается (no_model / none / набор).</summary>
        NotExpectedInModel,
        /// <summary>Есть в проекте Revit, нет в договоре (ТК) — лишнее.</summary>
        ExtraInRevit
    }

    public sealed class DsTkCompareRow
    {
        public string RoomName { get; init; }
        public int MaterialId { get; init; }
        public int? ClientMaterialId { get; init; }
        public int? MaterialSetId { get; init; }
        public bool IsMaterialCntInput { get; init; }
        public string MaterialName { get; init; }
        public string WorkSetName { get; init; }
        public string RevitName { get; init; }
        public string Category { get; init; }
        public string KindDisplay { get; init; }
        public string RevitFileType { get; init; }
        public string SourceLevel { get; init; }
        public int Quantity { get; init; }
        public double? TkQty { get; init; }
        public double? ScheduleQty { get; init; }
        public string QtyUnit { get; init; }
        public string QtyStatusKey { get; init; }
        public string QtyStatusDisplay { get; init; }
        /// <summary>true — колонка эталона взята из привязанной ДС, не из исходного ТК.</summary>
        public bool QtyBaselineFromDs { get; init; }
        public DsTkCompareStatus Status { get; init; }

        public string MaterialIdDisplay => MaterialId > 0
            ? MaterialId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "—";

        public string TkQtyDisplay => FormatOptionalQty(TkQty);
        public string ScheduleQtyDisplay => FormatOptionalQty(ScheduleQty);

        static string FormatOptionalQty(double? value) =>
            value == null
                ? string.Empty
                : value.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        public string StatusKey => Status switch
        {
            DsTkCompareStatus.Match => "match",
            DsTkCompareStatus.MissingInRevit => "missing_revit",
            DsTkCompareStatus.NotExpectedInModel => "not_expected",
            DsTkCompareStatus.ExtraInRevit => "extra",
            _ => "neutral"
        };

        public string StatusDisplay => Status switch
        {
            DsTkCompareStatus.Match => "Совпадает",
            DsTkCompareStatus.MissingInRevit => "Нет в проекте",
            DsTkCompareStatus.NotExpectedInModel => "Не ожидается в модели",
            DsTkCompareStatus.ExtraInRevit => "Лишнее в проекте",
            _ => "—"
        };

        public bool IsProblem =>
            Status is DsTkCompareStatus.MissingInRevit or DsTkCompareStatus.ExtraInRevit
            // qty_tk_only — норма (не все позиции договора есть в ведомостях).
            || QtyStatusKey is "qty_mismatch" or "qty_schedule_only";
    }

    public sealed class DsTkCompareRoom
    {
        public string RoomName { get; init; }
        public List<DsTkCompareRow> Rows { get; init; } = new();

        public string SummaryBadge
        {
            get
            {
                var match = Rows.Count(r => r.Status == DsTkCompareStatus.Match);
                var missing = Rows.Count(r => r.Status == DsTkCompareStatus.MissingInRevit);
                var notExpected = Rows.Count(r => r.Status == DsTkCompareStatus.NotExpectedInModel);
                var extra = Rows.Count(r => r.Status == DsTkCompareStatus.ExtraInRevit);
                var qtyMismatch = Rows.Count(r => r.QtyStatusKey == "qty_mismatch");
                var badge =
                    $"{Rows.Count} поз. · ✓{match} · нет в проекте {missing} · лишнее {extra} · не ожидается {notExpected}";
                if (qtyMismatch > 0)
                    badge += $" · qty≠ {qtyMismatch}";
                return badge;
            }
        }
    }

    public sealed class DsTkCompareResult
    {
        public List<DsTkCompareRoom> Rooms { get; init; } = new();
        public int MatchCount { get; init; }
        public int MissingInRevitCount { get; init; }
        public int NotExpectedInModelCount { get; init; }
        public int ExtraInRevitCount { get; init; }
        public int QtyMismatchCount { get; init; }
        public int TotalRows => MatchCount + MissingInRevitCount + NotExpectedInModelCount + ExtraInRevitCount;
        public string Note { get; init; }
        public bool QtyBaselineFromDs { get; init; }
        public List<TkQtyScheduleSourceInfo> ScheduleSources { get; init; } = new();
    }

    /// <summary>
    /// Эталон presence — договор (ТК). Объёмы: эталон (ТК или qty из привязанной ДС) ↔ ведомости.
    /// </summary>
    public static class DsTkCompareService
    {
        /// <summary>Абсолютный допуск (шт / м² / м).</summary>
        const double QtyAbsTolerance = 0.05d;
        /// <summary>Относительный допуск для больших площадей/длин.</summary>
        const double QtyRelTolerance = 0.01d;

        public static DsTkCompareResult Compare(
            RoomSrIdSnapshot revit,
            ClientMaterialTkSnapshot tk,
            IReadOnlyDictionary<int, RevitMaterialRowDto> materialMeta = null,
            TkQtyScheduleSnapshot scheduleQty = null,
            bool qtyBaselineFromDs = false)
        {
            materialMeta ??= new Dictionary<int, RevitMaterialRowDto>();
            var baselineName = qtyBaselineFromDs ? "ДС" : "договору";
            var baselineShort = qtyBaselineFromDs ? "ДС" : "ТК";

            var revitByRoom = BuildRevitByRoom(revit);
            var tkByRoom = BuildTkByRoom(tk);
            var scheduleByRoom = scheduleQty?.SumByRoomAndMaterial
                ?? new Dictionary<string, Dictionary<int, double>>(StringComparer.OrdinalIgnoreCase);

            // Эталон — комнаты договора (ТК); проект может добавить «лишние» комнаты.
            var roomKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in tkByRoom.Keys)
                roomKeys.Add(key);
            foreach (var key in revitByRoom.Keys)
                roomKeys.Add(key);

            var rooms = new List<DsTkCompareRoom>();
            var match = 0;
            var missing = 0;
            var notExpected = 0;
            var extra = 0;
            var qtyMismatch = 0;

            foreach (var roomKey in roomKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                revitByRoom.TryGetValue(roomKey, out var revitItems);
                tkByRoom.TryGetValue(roomKey, out var tkItems);
                scheduleByRoom.TryGetValue(roomKey, out var scheduleByMat);
                revitItems ??= new List<RoomSrIdItem>();
                tkItems ??= new List<ClientMaterialRowDto>();
                scheduleByMat ??= new Dictionary<int, double>();

                var roomName = ResolveRoomName(roomKey, revit, tk);
                var rows = new List<DsTkCompareRow>();

                var revitById = revitItems
                    .GroupBy(i => i.SrId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var tkById = tkItems
                    .Where(r => r.MaterialId is > 0)
                    .GroupBy(r => r.MaterialId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Сначала позиции договора, затем лишнее в проекте.
                var allIds = tkById.Keys
                    .Concat(revitById.Keys.Where(id => !tkById.ContainsKey(id)))
                    .Distinct()
                    .ToList();

                foreach (var materialId in allIds)
                {
                    revitById.TryGetValue(materialId, out var revitGroup);
                    tkById.TryGetValue(materialId, out var tkGroup);

                    var hasRevit = revitGroup is { Count: > 0 };
                    var hasTk = tkGroup is { Count: > 0 };
                    var tkQty = hasTk ? SumTkQty(tkGroup) : null;
                    var hasPositiveTkQty = tkQty is > 0;

                    materialMeta.TryGetValue(materialId, out var meta);
                    var fileType = meta?.RevitFileType;

                    DsTkCompareStatus status;
                    if (hasRevit && hasTk)
                        status = DsTkCompareStatus.Match;
                    else if (hasTk && !hasRevit && (!hasPositiveTkQty || IsNotExpectedInModel(fileType)))
                        status = DsTkCompareStatus.NotExpectedInModel;
                    else if (hasTk && !hasRevit)
                        status = DsTkCompareStatus.MissingInRevit;
                    else if (hasRevit)
                        status = DsTkCompareStatus.ExtraInRevit;
                    else
                        continue;

                    // Строки ТК с cnt=0 и без Revit — не «дыра», а пустая позиция договора.
                    if (status == DsTkCompareStatus.MissingInRevit && hasTk && !hasPositiveTkQty)
                        status = DsTkCompareStatus.NotExpectedInModel;

                    scheduleByMat.TryGetValue(materialId, out var scheduleQtyValue);
                    var hasScheduleQty = scheduleByMat.ContainsKey(materialId);
                    var (qtyKey, qtyDisplay) = ResolveQtyStatus(
                        tkQty,
                        hasScheduleQty ? scheduleQtyValue : null,
                        hasScheduleQty,
                        baselineName,
                        baselineShort);

                    var tkRow = PreferTkRowForApply(tkGroup);
                    var revitItem = revitGroup?.FirstOrDefault();
                    var qtyUnit = ResolveQtyUnit(materialId, roomKey, scheduleQty);

                    var row = new DsTkCompareRow
                    {
                        RoomName = roomName,
                        MaterialId = materialId,
                        ClientMaterialId = tkRow?.ClientMaterialId,
                        MaterialSetId = tkRow?.MaterialSetId,
                        IsMaterialCntInput = tkRow?.IsMaterialCntInput == true,
                        MaterialName = Prefer(
                            Strip(tkRow?.MaterialName),
                            Strip(meta?.MaterialName),
                            PreferScheduleName(materialId, roomKey, scheduleQty),
                            revitItem?.Name,
                            $"material_id={materialId}"),
                        WorkSetName = Prefer(Strip(tkRow?.WorkSetName), "—"),
                        RevitName = revitItem?.Name ?? "—",
                        Category = revitItem?.Category ?? "—",
                        KindDisplay = FormatKind(fileType),
                        RevitFileType = string.IsNullOrWhiteSpace(fileType) ? null : fileType.Trim(),
                        SourceLevel = revitItem?.SourceLevel ?? "—",
                        Quantity = revitGroup?.Sum(i => i.Quantity) ?? 0,
                        TkQty = tkQty,
                        ScheduleQty = hasScheduleQty ? scheduleQtyValue : null,
                        QtyUnit = qtyUnit,
                        QtyStatusKey = qtyKey,
                        QtyStatusDisplay = FormatQtyStatusDisplay(qtyDisplay, qtyUnit),
                        QtyBaselineFromDs = qtyBaselineFromDs,
                        Status = status
                    };

                    rows.Add(row);
                    CountStatus(status, ref match, ref missing, ref notExpected, ref extra);
                    if (qtyKey == "qty_mismatch")
                        qtyMismatch++;
                }

                // Наборы без material_id — в модели по SR_ID не ожидаются.
                foreach (var setRow in tkItems.Where(r =>
                             (r.MaterialId is null or <= 0) && r.MaterialSetId is > 0))
                {
                    rows.Add(new DsTkCompareRow
                    {
                        RoomName = roomName,
                        MaterialId = 0,
                        MaterialName = Prefer(Strip(setRow.SetName), Strip(setRow.MaterialName), $"set:{setRow.MaterialSetId}"),
                        WorkSetName = Prefer(Strip(setRow.WorkSetName), "—"),
                        RevitName = "—",
                        Category = "—",
                        KindDisplay = "набор",
                        RevitFileType = "set",
                        SourceLevel = "—",
                        Quantity = 0,
                        TkQty = setRow.MaterialCnt,
                        Status = DsTkCompareStatus.NotExpectedInModel
                    });
                    notExpected++;
                }

                if (rows.Count == 0)
                    continue;

                rooms.Add(new DsTkCompareRoom
                {
                    RoomName = roomName,
                    Rows = rows
                        .OrderBy(r => StatusSortOrder(r.Status))
                        .ThenBy(r => r.MaterialName, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                });
            }

            var scheduleLineCount = scheduleQty?.Lines?.Count ?? 0;
            var note = tk == null || !tk.HasData
                ? (tk?.EmptyMessage ?? "ТК (договор) не загружен.")
                : $"Эталон presence — договор (ТК). Совпадает: {match}, нет в проекте: {missing}, лишнее в проекте: {extra}, не ожидается в модели: {notExpected}."
                  + (scheduleLineCount > 0
                      ? (qtyBaselineFromDs
                          ? $" Объёмы: эталон = ДС ↔ ведомости ({scheduleLineCount} строк), ≠ ДС: {qtyMismatch}."
                          : $" Объёмы ведомостей: {scheduleLineCount} строк, ≠ договору: {qtyMismatch}.")
                      : string.Empty);

            return new DsTkCompareResult
            {
                Rooms = OrderRoomsByTk(rooms, tk),
                MatchCount = match,
                MissingInRevitCount = missing,
                NotExpectedInModelCount = notExpected,
                ExtraInRevitCount = extra,
                QtyMismatchCount = qtyMismatch,
                Note = note,
                QtyBaselineFromDs = qtyBaselineFromDs,
                ScheduleSources = scheduleQty?.Sources ?? new List<TkQtyScheduleSourceInfo>()
            };
        }

        static ClientMaterialRowDto PreferTkRowForApply(List<ClientMaterialRowDto> tkGroup)
        {
            if (tkGroup == null || tkGroup.Count == 0)
                return null;

            return tkGroup
                .Where(r => r?.ClientMaterialId is > 0)
                .OrderByDescending(r => r.MaterialCnt ?? -1d)
                .ThenByDescending(r => r.ClientMaterialId)
                .FirstOrDefault()
                ?? tkGroup.FirstOrDefault();
        }

        static double? SumTkQty(List<ClientMaterialRowDto> rows)
        {
            if (rows == null || rows.Count == 0)
                return null;

            double sum = 0;
            var any = false;
            foreach (var row in rows)
            {
                if (row.MaterialCnt == null)
                    continue;
                any = true;
                sum += row.MaterialCnt.Value;
            }

            return any ? sum : null;
        }

        static (string Key, string Display) ResolveQtyStatus(
            double? tkQty,
            double? scheduleQty,
            bool hasSchedule,
            string baselineName,
            string baselineShort)
        {
            if (!hasSchedule && tkQty == null)
                return (null, null);

            // Есть в эталоне, нет строки в ведомости — обычно ок.
            if (!hasSchedule)
                return ("qty_tk_only", $"объём только в {baselineShort}");

            if (tkQty == null)
                return ("qty_schedule_only", "лишний объём в проекте");

            if (QtyEquals(tkQty.Value, scheduleQty!.Value))
                return ("qty_match", $"объём = {baselineName}");

            return ("qty_mismatch",
                $"объём ≠ {baselineName}: {baselineShort} {FormatQty(tkQty)} ≠ вед. {FormatQty(scheduleQty)}");
        }

        static bool QtyEquals(double a, double b)
        {
            var abs = Math.Abs(a - b);
            if (abs <= QtyAbsTolerance)
                return true;
            var scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale > 0 && abs <= scale * QtyRelTolerance;
        }

        static string FormatQty(double? value) =>
            value == null
                ? "—"
                : value.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        static string FormatQtyStatusDisplay(string display, string unit)
        {
            if (string.IsNullOrWhiteSpace(display))
                return display;
            if (string.IsNullOrWhiteSpace(unit) || unit == "—")
                return display;
            if (display.Contains(unit, StringComparison.Ordinal))
                return display;
            return $"{display} ({unit})";
        }

        static string ResolveQtyUnit(int materialId, string roomKey, TkQtyScheduleSnapshot scheduleQty)
        {
            var line = scheduleQty?.Lines?.FirstOrDefault(l =>
                l.MaterialId == materialId
                && string.Equals(
                    DsAreaCompareService.GetRoomCompareKey(l.RoomName ?? string.Empty),
                    roomKey,
                    StringComparison.OrdinalIgnoreCase));
            var unit = line?.Unit;
            return string.IsNullOrWhiteSpace(unit) || unit == "—" ? null : unit.Trim();
        }

        static string PreferScheduleName(int materialId, string roomKey, TkQtyScheduleSnapshot scheduleQty)
        {
            var line = scheduleQty?.Lines?.FirstOrDefault(l =>
                l.MaterialId == materialId
                && !string.IsNullOrWhiteSpace(l.MaterialName)
                && string.Equals(
                    DsAreaCompareService.GetRoomCompareKey(l.RoomName ?? string.Empty),
                    roomKey,
                    StringComparison.OrdinalIgnoreCase));
            return line?.MaterialName;
        }

        static void CountStatus(
            DsTkCompareStatus status,
            ref int match,
            ref int missing,
            ref int notExpected,
            ref int extra)
        {
            switch (status)
            {
                case DsTkCompareStatus.Match:
                    match++;
                    break;
                case DsTkCompareStatus.MissingInRevit:
                    missing++;
                    break;
                case DsTkCompareStatus.NotExpectedInModel:
                    notExpected++;
                    break;
                case DsTkCompareStatus.ExtraInRevit:
                    extra++;
                    break;
            }
        }

        static int StatusSortOrder(DsTkCompareStatus status) => status switch
        {
            DsTkCompareStatus.MissingInRevit => 0,
            DsTkCompareStatus.ExtraInRevit => 1,
            DsTkCompareStatus.NotExpectedInModel => 2,
            DsTkCompareStatus.Match => 3,
            _ => 4
        };

        /// <summary>Комнаты договора первыми (порядок появления в ТК), затем только-проект.</summary>
        static List<DsTkCompareRoom> OrderRoomsByTk(List<DsTkCompareRoom> rooms, ClientMaterialTkSnapshot tk)
        {
            var tkOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var i = 0;
            foreach (var row in tk?.Rows ?? Enumerable.Empty<ClientMaterialRowDto>())
            {
                var key = DsAreaCompareService.GetRoomCompareKey(row.RoomName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(key) || tkOrder.ContainsKey(key))
                    continue;
                tkOrder[key] = i++;
            }

            return rooms
                .OrderBy(r =>
                {
                    var key = DsAreaCompareService.GetRoomCompareKey(r.RoomName ?? string.Empty);
                    return tkOrder.TryGetValue(key, out var ord) ? ord : int.MaxValue;
                })
                .ThenBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static Dictionary<string, List<RoomSrIdItem>> BuildRevitByRoom(RoomSrIdSnapshot revit)
        {
            var map = new Dictionary<string, List<RoomSrIdItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var room in revit?.Rooms ?? Enumerable.Empty<RoomSrIdRoomRow>())
            {
                var key = DsAreaCompareService.GetRoomCompareKey(room.RoomName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<RoomSrIdItem>();
                    map[key] = list;
                }

                list.AddRange(room.Items ?? Enumerable.Empty<RoomSrIdItem>());
            }

            return map;
        }

        static Dictionary<string, List<ClientMaterialRowDto>> BuildTkByRoom(ClientMaterialTkSnapshot tk)
        {
            var map = new Dictionary<string, List<ClientMaterialRowDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in tk?.Rows ?? Enumerable.Empty<ClientMaterialRowDto>())
            {
                var key = DsAreaCompareService.GetRoomCompareKey(row.RoomName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(key))
                    key = "_unknown_";

                if (!map.TryGetValue(key, out var list))
                {
                    list = new List<ClientMaterialRowDto>();
                    map[key] = list;
                }

                list.Add(row);
            }

            return map;
        }

        static string ResolveRoomName(string roomKey, RoomSrIdSnapshot revit, ClientMaterialTkSnapshot tk)
        {
            // Имя комнаты — сначала из договора.
            var fromTk = tk?.Rows?
                .FirstOrDefault(r => string.Equals(
                    DsAreaCompareService.GetRoomCompareKey(r.RoomName ?? string.Empty),
                    roomKey,
                    StringComparison.OrdinalIgnoreCase))
                ?.RoomName;

            if (!string.IsNullOrWhiteSpace(fromTk))
                return fromTk.Trim();

            var fromRevit = revit?.Rooms?
                .FirstOrDefault(r => string.Equals(
                    DsAreaCompareService.GetRoomCompareKey(r.RoomName ?? string.Empty),
                    roomKey,
                    StringComparison.OrdinalIgnoreCase))
                ?.RoomName;

            return string.IsNullOrWhiteSpace(fromRevit) ? roomKey : fromRevit.Trim();
        }

        /// <summary>
        /// no_model / none — в Revit не кладём; отсутствие SR_ID не проблема.
        /// </summary>
        public static bool IsNotExpectedInModel(string revitFileType)
        {
            if (string.IsNullOrWhiteSpace(revitFileType))
                return false;

            return revitFileType.Trim().ToLowerInvariant() switch
            {
                "no_model" => true,
                "none" => true,
                _ => false
            };
        }

        static string FormatKind(string revitFileType)
        {
            if (string.IsNullOrWhiteSpace(revitFileType))
                return "—";

            return revitFileType.Trim().ToLowerInvariant() switch
            {
                "surface" => "поверхность",
                "rfa" => "RFA",
                "no_model" => "без модели",
                "none" => "нет файла",
                _ => revitFileType.Trim()
            };
        }

        static string Strip(string value) => TkMaterialCompareService.StripHtml(value);

        static string Prefer(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value) && value != "—")
                    return value.Trim();
            }

            return "—";
        }
    }
}
