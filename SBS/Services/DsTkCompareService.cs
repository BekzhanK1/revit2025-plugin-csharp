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
        public bool IsSetMember { get; init; }
        public int? TkChangeId { get; init; }
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
        /// <summary>Единица позиции в MySpace (unit_name).</summary>
        public string MyspaceUnit { get; init; }
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
            Status is DsTkCompareStatus.MissingInRevit or DsTkCompareStatus.ExtraInRevit;

        /// <summary>Можно отправить в ДС (как поле ввода в MySpace).</summary>
        public bool IsEditableQtyMismatch => QtyStatusKey == "qty_mismatch";

        /// <summary>Сильный % у позиции без ввода в MySpace — сигнал, что проект неверный.</summary>
        public bool IsProjectQtyAlert => QtyStatusKey == "qty_project_alert";

        /// <summary>Объём расходится, но из Revit его отправить нельзя — причина в QtyStatusDisplay.</summary>
        public bool IsQtyBlocked => QtyStatusKey == "qty_blocked";
    }

    public sealed class DsTkCompareRoom
    {
        public string RoomName { get; init; }
        public List<DsTkCompareRow> Rows { get; init; } = new();

        public string SummaryBadge
        {
            get
            {
                var missing = Rows.Count(r => r.Status == DsTkCompareStatus.MissingInRevit);
                var extra = Rows.Count(r => r.Status == DsTkCompareStatus.ExtraInRevit);
                if (missing == 0 && extra == 0)
                    return "состав совпадает";
                var parts = new List<string>();
                if (missing > 0)
                    parts.Add($"нет в проекте {missing}");
                if (extra > 0)
                    parts.Add($"лишнее {extra}");
                return string.Join(" · ", parts);
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
        public int QtyProjectAlertCount { get; init; }
        public int QtyBlockedCount { get; init; }
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
        /// <summary>Относительный допуск для «почти равно».</summary>
        const double QtyRelTolerance = 0.01d;
        /// <summary>
        /// Порог «проект сильно не сходится» для позиций без ввода в MySpace.
        /// </summary>
        public const double QtyProjectAlertRelThreshold = 0.10d;

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
            var qtyProjectAlert = 0;
            var qtyBlocked = 0;

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

                    // Строки ведомости без комнаты сюда не попадают: угадывать комнату не будем.
                    scheduleByMat.TryGetValue(materialId, out var scheduleQtyValue);
                    var hasScheduleQty = scheduleByMat.ContainsKey(materialId);
                    var tkRow = PreferTkRowForApply(tkGroup);
                    var canEditQty = tkRow?.IsMaterialCntInput == true;
                    var (qtyKey, qtyDisplay) = ResolveQtyStatus(
                        tkQty,
                        hasScheduleQty ? scheduleQtyValue : null,
                        hasScheduleQty,
                        canEditQty,
                        baselineName,
                        baselineShort);

                    var revitItem = revitGroup?.FirstOrDefault();
                    var qtyUnit = ResolveQtyUnit(materialId, roomKey, scheduleQty);

                    if (qtyKey == "qty_mismatch")
                    {
                        var blockReason = ResolveQtyBlockReason(tkGroup, tkRow, qtyUnit, qtyBaselineFromDs);
                        if (blockReason != null)
                        {
                            qtyKey = "qty_blocked";
                            qtyDisplay = $"{qtyDisplay} — не отправляется: {blockReason}";
                        }
                    }

                    var row = new DsTkCompareRow
                    {
                        RoomName = roomName,
                        MaterialId = materialId,
                        ClientMaterialId = tkRow?.ClientMaterialId,
                        MaterialSetId = tkRow?.MaterialSetId,
                        IsSetMember = tkRow?.IsSetMember == true,
                        TkChangeId = tkRow?.TkChangeId,
                        IsMaterialCntInput = canEditQty,
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
                        MyspaceUnit = string.IsNullOrWhiteSpace(tkRow?.UnitName) ? null : tkRow.UnitName.Trim(),
                        QtyStatusKey = qtyKey,
                        QtyStatusDisplay = FormatQtyStatusDisplay(qtyDisplay, qtyUnit),
                        QtyBaselineFromDs = qtyBaselineFromDs,
                        Status = status
                    };

                    rows.Add(row);
                    CountStatus(status, ref match, ref missing, ref notExpected, ref extra);
                    if (qtyKey == "qty_mismatch")
                        qtyMismatch++;
                    else if (qtyKey == "qty_project_alert")
                        qtyProjectAlert++;
                    else if (qtyKey == "qty_blocked")
                        qtyBlocked++;
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
                          ? $" Объёмы vs ДС: к отправке {qtyMismatch}, нельзя отправить {qtyBlocked}, алерт проекта (≥{QtyProjectAlertRelThreshold:P0}) {qtyProjectAlert}."
                          : $" Объёмы vs договор: к отправке {qtyMismatch}, нельзя отправить {qtyBlocked}, алерт проекта (≥{QtyProjectAlertRelThreshold:P0}) {qtyProjectAlert}.")
                      : string.Empty);

            return new DsTkCompareResult
            {
                Rooms = OrderRoomsByTk(rooms, tk),
                MatchCount = match,
                MissingInRevitCount = missing,
                NotExpectedInModelCount = notExpected,
                ExtraInRevitCount = extra,
                QtyMismatchCount = qtyMismatch,
                QtyProjectAlertCount = qtyProjectAlert,
                QtyBlockedCount = qtyBlocked,
                Note = note,
                QtyBaselineFromDs = qtyBaselineFromDs,
                ScheduleSources = scheduleQty?.Sources ?? new List<TkQtyScheduleSourceInfo>()
            };
        }

        /// <summary>
        /// Колонку объёма берём из расчёта ДС по модели (DsTkTargetService): там строка ДС
        /// сопоставлена со своей ведомостью по конструктиву. Строки без поля ввода остаются как были.
        /// </summary>
        public static DsTkCompareResult ApplyTarget(DsTkCompareResult compare, DsTkTargetResult target)
        {
            if (compare?.Rooms == null || target == null)
                return compare;

            // (комната, material_id) → строки расчёта. Несколько позиций — берём сумму, статус по худшей.
            var byKey = new Dictionary<(string, int), List<(DsTkTargetPosition Position, DsTkTargetLine Line)>>();
            foreach (var position in target.Positions)
            {
                var roomKey = DsAreaCompareService.GetRoomCompareKey(position.RoomName ?? string.Empty);
                foreach (var line in new[] { position.Head }.Concat(position.Members))
                {
                    if (line == null || line.MaterialId <= 0)
                        continue;
                    var key = (roomKey.ToUpperInvariant(), line.MaterialId);
                    if (!byKey.TryGetValue(key, out var list))
                        byKey[key] = list = new List<(DsTkTargetPosition, DsTkTargetLine)>();
                    list.Add((position, line));
                }
            }

            var send = 0;
            var blocked = 0;
            var rooms = new List<DsTkCompareRoom>();
            foreach (var room in compare.Rooms)
            {
                var roomKey = DsAreaCompareService.GetRoomCompareKey(room.RoomName ?? string.Empty).ToUpperInvariant();
                var rows = new List<DsTkCompareRow>();
                foreach (var row in room.Rows)
                {
                    if (row.MaterialId <= 0 || !byKey.TryGetValue((roomKey, row.MaterialId), out var hits))
                    {
                        rows.Add(row);
                        continue;
                    }

                    var current = hits.Any(h => h.Line.CurrentQty != null)
                        ? hits.Sum(h => h.Line.CurrentQty ?? 0d)
                        : (double?)null;
                    var targetQty = hits.Any(h => h.Line.TargetQty != null)
                        ? hits.Sum(h => h.Line.TargetQty ?? h.Line.CurrentQty ?? 0d)
                        : (double?)null;
                    var changed = hits.Any(h => h.Line.IsChanged);
                    var worst = hits.Select(h => h.Position)
                        .OrderBy(p => p.Status switch
                        {
                            DsTkTargetStatus.Blocked => 0,
                            DsTkTargetStatus.Skipped => 1,
                            DsTkTargetStatus.Change => 2,
                            DsTkTargetStatus.Same => 3,
                            _ => 4
                        })
                        .First();
                    var unit = hits.Select(h => h.Line.RevitUnit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                        ?? row.QtyUnit;
                    var note = hits.Select(h => h.Line.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

                    string key;
                    string display;
                    switch (worst.Status)
                    {
                        case DsTkTargetStatus.Blocked when changed || worst.Head?.TargetQty == null:
                            key = "qty_blocked";
                            display = "не отправляется: " + worst.Reason;
                            blocked++;
                            break;
                        case DsTkTargetStatus.Skipped when changed:
                            key = "qty_blocked";
                            display = $"ДС {FormatQty(current)} ≠ модель {FormatQty(targetQty)} — {worst.Reason}";
                            blocked++;
                            break;
                        case DsTkTargetStatus.NotFromModel:
                            key = "qty_tk_only";
                            display = worst.Reason;
                            break;
                        default:
                            if (changed)
                            {
                                key = "qty_mismatch";
                                display = $"уйдёт в ДС: {FormatQty(current)} → {FormatQty(targetQty)}"
                                          + (note == null ? string.Empty : $" ({note})");
                                send++;
                            }
                            else
                            {
                                key = "qty_match";
                                display = targetQty == null ? (note ?? "как в ДС") : "объём = ДС";
                            }
                            break;
                    }

                    rows.Add(new DsTkCompareRow
                    {
                        RoomName = row.RoomName,
                        MaterialId = row.MaterialId,
                        ClientMaterialId = row.ClientMaterialId,
                        MaterialSetId = row.MaterialSetId,
                        IsSetMember = row.IsSetMember,
                        TkChangeId = row.TkChangeId,
                        IsMaterialCntInput = true,
                        MaterialName = row.MaterialName,
                        WorkSetName = row.WorkSetName,
                        RevitName = row.RevitName,
                        Category = row.Category,
                        KindDisplay = row.KindDisplay,
                        RevitFileType = row.RevitFileType,
                        SourceLevel = row.SourceLevel,
                        Quantity = row.Quantity,
                        TkQty = current,
                        ScheduleQty = targetQty,
                        QtyUnit = unit,
                        MyspaceUnit = row.MyspaceUnit,
                        QtyStatusKey = key,
                        QtyStatusDisplay = FormatQtyStatusDisplay(display, unit),
                        QtyBaselineFromDs = true,
                        Status = row.Status
                    });
                }

                rooms.Add(new DsTkCompareRoom { RoomName = room.RoomName, Rows = rows });
            }

            return new DsTkCompareResult
            {
                Rooms = rooms,
                MatchCount = compare.MatchCount,
                MissingInRevitCount = compare.MissingInRevitCount,
                NotExpectedInModelCount = compare.NotExpectedInModelCount,
                ExtraInRevitCount = compare.ExtraInRevitCount,
                QtyMismatchCount = send,
                QtyProjectAlertCount = compare.QtyProjectAlertCount,
                QtyBlockedCount = blocked,
                Note = compare.Note,
                QtyBaselineFromDs = true,
                ScheduleSources = compare.ScheduleSources
            };
        }

        static ClientMaterialRowDto PreferTkRowForApply(List<ClientMaterialRowDto> tkGroup)
        {
            if (tkGroup == null || tkGroup.Count == 0)
                return null;

            return tkGroup
                .Where(r => r?.ClientMaterialId is > 0)
                .OrderByDescending(r => r.IsSetMember)
                .ThenByDescending(r => r.MaterialCnt ?? -1d)
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
            bool canEditInMyspace,
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

            var rel = RelativeDiff(tkQty.Value, scheduleQty.Value);
            var relPct = (rel * 100d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            var core = $"{baselineShort} {FormatQty(tkQty)} ≠ вед. {FormatQty(scheduleQty)} ({relPct}%)";

            // Только то, что можно править в MySpace — кандидат на отправку в ДС.
            if (canEditInMyspace)
                return ("qty_mismatch", $"объём ≠ {baselineName}: {core}");

            // Без ввода в MySpace: сильный % → алерт «проект неверный», слабый → тихо.
            if (rel >= QtyProjectAlertRelThreshold)
                return ("qty_project_alert",
                    $"проект сильно ≠ {baselineName}: {core} — без поля ввода");

            return ("qty_soft_diff",
                $"небольшое расхождение: {core}");
        }

        static double RelativeDiff(double a, double b)
        {
            var scale = Math.Max(Math.Abs(a), Math.Abs(b));
            if (scale <= 1e-9)
                return Math.Abs(a - b) <= QtyAbsTolerance ? 0d : 1d;
            return Math.Abs(a - b) / scale;
        }

        static bool QtyEquals(double a, double b)
        {
            var abs = Math.Abs(a - b);
            if (abs <= QtyAbsTolerance)
                return true;
            var scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale > 0 && abs <= scale * QtyRelTolerance;
        }

        /// <summary>
        /// Почему расхождение объёма нельзя отправить из Revit; null — можно.
        /// Те же правила проверяет сервер (/revit/plugin/ds/tk-change/apply/).
        /// </summary>
        static string ResolveQtyBlockReason(
            List<ClientMaterialRowDto> tkGroup,
            ClientMaterialRowDto tkRow,
            string scheduleUnit,
            bool qtyBaselineFromDs)
        {
            var positions = tkGroup?
                .Where(r => r?.ClientMaterialId is > 0)
                .Select(r => r.ClientMaterialId.Value)
                .Distinct()
                .Count() ?? 0;
            if (positions > 1)
                return $"материал стоит в {positions} позициях договора этой комнаты, объём правьте в MySpace";

            if (tkRow?.IsAtomMeasure == true)
                return "штучный материал, объём правьте в MySpace";

            var revitUnit = QtyUnits.Normalize(scheduleUnit);
            if (revitUnit == null)
                return "в ведомости Revit не задана единица";

            // Единицу MySpace отдаёт чтение ДС; без привязанной ДС отправка всё равно выключена.
            if (!qtyBaselineFromDs && string.IsNullOrWhiteSpace(tkRow?.UnitName))
                return null;

            var myspaceUnit = QtyUnits.Normalize(tkRow?.UnitName);
            if (myspaceUnit == null)
                return "у материала нет единицы в MySpace";
            if (!string.Equals(revitUnit, myspaceUnit, StringComparison.Ordinal))
                return $"единица ведомости «{scheduleUnit.Trim()}» не совпадает с MySpace «{tkRow.UnitName.Trim()}»";

            return null;
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
            var line = FindScheduleLine(materialId, roomKey, scheduleQty);
            var unit = line?.Unit;
            return string.IsNullOrWhiteSpace(unit) || unit == "—" ? null : unit.Trim();
        }

        static string PreferScheduleName(int materialId, string roomKey, TkQtyScheduleSnapshot scheduleQty)
        {
            var line = FindScheduleLine(materialId, roomKey, scheduleQty, requireName: true);
            return line?.MaterialName;
        }

        static TkQtyScheduleLine FindScheduleLine(
            int materialId,
            string roomKey,
            TkQtyScheduleSnapshot scheduleQty,
            bool requireName = false)
        {
            var lines = scheduleQty?.Lines;
            if (lines == null || lines.Count == 0)
                return null;

            bool MatchesRoom(TkQtyScheduleLine l) =>
                l.MaterialId == materialId
                && (!requireName || !string.IsNullOrWhiteSpace(l.MaterialName))
                && string.Equals(
                    DsAreaCompareService.GetRoomCompareKey(l.RoomName ?? string.Empty),
                    roomKey,
                    StringComparison.OrdinalIgnoreCase);

            var roomMatch = lines.FirstOrDefault(MatchesRoom);
            if (roomMatch != null)
                return roomMatch;

            return lines.FirstOrDefault(l =>
                l.MaterialId == materialId
                && string.IsNullOrWhiteSpace(l.RoomName)
                && (!requireName || !string.IsNullOrWhiteSpace(l.MaterialName)));
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

    /// <summary>
    /// Единицы для сравнения ведомости Revit и MySpace. Синонимы совпадают
    /// с _UNIT_ALIASES в office_api revit_ds_tk_apply_services.py.
    /// </summary>
    public static class QtyUnits
    {
        static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
        {
            ["м2"] = "м2", ["м²"] = "м2", ["квм"] = "м2", ["m2"] = "м2",
            ["м"] = "м", ["пм"] = "м", ["мп"] = "м", ["погм"] = "м", ["m"] = "м",
            ["м3"] = "м3", ["м³"] = "м3", ["кубм"] = "м3",
            ["шт"] = "шт", ["штук"] = "шт", ["штука"] = "шт",
            ["л"] = "л", ["литр"] = "л",
            ["кг"] = "кг"
        };

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() is "—" or "-")
                return null;
            var raw = value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace(".", string.Empty);
            if (raw.Length == 0)
                return null;
            return Aliases.TryGetValue(raw, out var unit) ? unit : raw;
        }
    }
}
