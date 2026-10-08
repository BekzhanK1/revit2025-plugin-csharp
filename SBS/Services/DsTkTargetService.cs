using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using SmartRemont.ExportRooms.DTO;

namespace SmartRemont.ExportRooms.Services
{
    public enum DsTkTargetStatus
    {
        /// <summary>Объём из модели отличается от ДС — уйдёт в ДС.</summary>
        Change,
        /// <summary>Объём из модели совпадает с ДС.</summary>
        Same,
        /// <summary>Конструктив не берётся из модели (мебель, кондиционер, доплаты…) — не трогаем.</summary>
        NotFromModel,
        /// <summary>Из Revit отправить нельзя, правится в MySpace. Отправку остальных не останавливает.</summary>
        Skipped,
        /// <summary>Посчитать нельзя — отправка остановлена, пока не исправят модель.</summary>
        Blocked
    }

    public sealed class DsTkTargetLine
    {
        public int MaterialId { get; init; }
        public bool IsSetMember { get; init; }
        public string MaterialName { get; init; }
        public string MyspaceUnit { get; init; }
        public string RevitUnit { get; init; }
        public double? CurrentQty { get; init; }
        /// <summary>null — модель про этот материал ничего не говорит, остаётся как в ДС.</summary>
        public double? TargetQty { get; init; }
        public string Note { get; init; }

        public bool IsChanged => TargetQty != null && !DsTkTargetService.QtyEquals(CurrentQty, TargetQty);
    }

    public sealed class DsTkTargetPosition
    {
        public int ClientMaterialId { get; init; }
        public int? MaterialSetId { get; init; }
        public int? WorkSetId { get; init; }
        public string RoomName { get; init; }
        public string WorkSetName { get; init; }
        public string MaterialName { get; init; }
        public string SourceTitle { get; init; }
        public DsTkTargetStatus Status { get; init; }
        public string Reason { get; init; }
        /// <summary>Ведомость, из-за строк без помещения которой позиция заблокирована.</summary>
        public string BlockedBySourceCode { get; init; }
        public DsTkTargetLine Head { get; init; }
        public List<DsTkTargetLine> Members { get; init; } = new();

        public IEnumerable<DsTkTargetLine> ChangedLines =>
            new[] { Head }.Concat(Members).Where(l => l != null && l.IsChanged);
    }

    public sealed class DsTkTargetIssue
    {
        public string RoomName { get; init; }
        public string MaterialName { get; init; }
        public string Text { get; init; }
        public string SourceCode { get; init; }

        public string Display =>
            $"{(string.IsNullOrWhiteSpace(RoomName) ? "—" : RoomName)}: {MaterialName} — {Text}";
    }

    public sealed class DsTkTargetResult
    {
        public List<DsTkTargetPosition> Positions { get; init; } = new();
        /// <summary>В модели есть, в ДС этой комнаты такого материала нет: замена/добавление — в MySpace.</summary>
        public List<DsTkTargetIssue> ExtraInModel { get; init; } = new();
        /// <summary>Строки нужных ведомостей без помещения — их объём никуда не попал.</summary>
        public List<DsTkTargetIssue> Unassigned { get; init; } = new();

        public IReadOnlyList<DsTkTargetPosition> ToSend =>
            Positions.Where(p => p.Status == DsTkTargetStatus.Change).ToList();

        public IReadOnlyList<DsTkTargetPosition> Blocked =>
            Positions.Where(p => p.Status == DsTkTargetStatus.Blocked).ToList();

        public IReadOnlyList<DsTkTargetPosition> Skipped =>
            Positions.Where(p => p.Status == DsTkTargetStatus.Skipped).ToList();

        public IReadOnlyList<DsTkTargetPosition> NotFromModel =>
            Positions.Where(p => p.Status == DsTkTargetStatus.NotFromModel).ToList();

        public int ChangedLineCount => ToSend.Sum(p => p.ChangedLines.Count());
    }

    /// <summary>
    /// Что должно стоять в ДС ТК по модели. Берутся только строки с полем ввода в MySpace
    /// (is_material_cnt_input) — то, что сейчас вводят руками. Работы, деньги и строки по
    /// формулам MySpace считает сам.
    /// Строка ДС ↔ ведомость — по конструктиву (TkQtyScheduleMapping.WorkSetIds), поэтому одна
    /// и та же плитка на полу и на стенах берётся из разных ведомостей.
    /// </summary>
    public static class DsTkTargetService
    {
        const double QtyEps = 0.005d;

        /// <summary>Что в ведомостях не так по источнику и комнате — из-за этого объём не посчитать.</summary>
        sealed class ModelProblems
        {
            /// <summary>source → строки без помещения или с помещением, которого нет в модели.</summary>
            public Dictionary<string, List<string>> BadRooms { get; } = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>(source, room) → строки с количеством, но без ID.</summary>
            public Dictionary<(string, string), List<string>> MissingId { get; } = new();
            /// <summary>(source, room) → материалы модели, которых нет в ДС этой комнаты.</summary>
            public Dictionary<(string, string), List<string>> Foreign { get; } = new();

            public static void Add<TKey>(Dictionary<TKey, List<string>> map, TKey key, string text)
            {
                if (!map.TryGetValue(key, out var list))
                    map[key] = list = new List<string>();
                if (!list.Contains(text))
                    list.Add(text);
            }
        }

        public static bool QtyEquals(double? a, double? b)
        {
            if (a == null || b == null)
                return a == null && b == null;
            return Math.Abs(a.Value - b.Value) <= QtyEps;
        }

        public static DsTkTargetResult Build(
            IReadOnlyList<DsTkChangeService.DsTkMaterialRow> dsRows,
            TkQtyScheduleSnapshot schedule,
            IEnumerable<string> modelRoomNames)
        {
            var result = new DsTkTargetResult();
            dsRows ??= Array.Empty<DsTkChangeService.DsTkMaterialRow>();
            schedule ??= new TkQtyScheduleSnapshot();

            var modelRooms = new HashSet<string>(
                (modelRoomNames ?? Enumerable.Empty<string>())
                    .Select(n => DsAreaCompareService.GetRoomCompareKey(n ?? string.Empty))
                    .Where(k => !string.IsNullOrWhiteSpace(k)),
                StringComparer.OrdinalIgnoreCase);

            var readable = new HashSet<string>(
                schedule.Sources.Where(s => s.Readable).Select(s => s.Code),
                StringComparer.OrdinalIgnoreCase);

            // (source, room, material) → сумма по ведомости.
            var qtyByKey = new Dictionary<(string, string, int), double>();
            foreach (var line in schedule.Lines)
            {
                if (line.MaterialId <= 0 || string.IsNullOrWhiteSpace(line.RoomName))
                    continue;
                var roomKey = DsAreaCompareService.GetRoomCompareKey(line.RoomName);
                var code = line.SourceCode ?? string.Empty;
                var key = (code.ToUpperInvariant(), roomKey.ToUpperInvariant(), line.MaterialId);
                qtyByKey[key] = (qtyByKey.TryGetValue(key, out var prev) ? prev : 0d) + line.Quantity;
            }

            var rows = dsRows
                .Where(r => r != null && r.ClientMaterialId > 0)
                .GroupBy(r => r.ClientMaterialId)
                .Select(g => g.First())
                .ToList();

            var inputRows = rows
                .Where(r => r.IsMaterialCntInput && r.ActionType != 1)
                .ToList();

            var inDsByRoom = MaterialsInDsByRoom(rows);
            var problems = FindModelProblems(schedule, modelRooms, inDsByRoom);

            // Один материал одной ведомости в двух позициях комнаты — объём не разделить.
            var usage = new Dictionary<(string, string, int), HashSet<int>>();
            foreach (var row in inputRows)
            {
                var roomKey = RoomKey(row.RoomName);
                foreach (var entry in TkQtyScheduleMapping.ForWorkSet(row.WorkSetId ?? 0).Where(e => !e.IsDerived))
                {
                    foreach (var materialId in MaterialIdsOf(row))
                    {
                        var key = (entry.Code.ToUpperInvariant(), roomKey, materialId);
                        if (!usage.TryGetValue(key, out var set))
                            usage[key] = set = new HashSet<int>();
                        set.Add(row.ClientMaterialId);
                    }
                }
            }

            // «По числу» (фурнитура = двери): считаем только те строки ведомости, чей материал стоит
            // в ТК этой комнаты основным материалом своего конструктива (установка двери), а не
            // всё подряд — во входную дверь межкомнатная фурнитура не нужна.
            var countMaterials = new Dictionary<(string, string), HashSet<int>>();
            foreach (var row in inputRows.Where(r => r.MaterialId is > 0))
            {
                var roomKey = RoomKey(row.RoomName);
                foreach (var entry in TkQtyScheduleMapping.ForWorkSet(row.WorkSetId ?? 0).Where(e => !e.IsDerived))
                {
                    var key = (entry.Code.ToUpperInvariant(), roomKey);
                    if (!countMaterials.TryGetValue(key, out var set))
                        countMaterials[key] = set = new HashSet<int>();
                    set.Add(row.MaterialId!.Value);
                }
            }

            var totalByRoom = countMaterials.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Sum(id =>
                    qtyByKey.TryGetValue((kv.Key.Item1, kv.Key.Item2, id), out var qty) ? qty : 0d));

            foreach (var row in inputRows
                         .OrderBy(r => r.RoomName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(r => r.WorkSetName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                result.Positions.Add(BuildPosition(row, modelRooms, readable, qtyByKey, totalByRoom, usage, problems));
            }

            CollectModelOnly(result, schedule, inDsByRoom);
            return result;
        }

        static DsTkTargetPosition BuildPosition(
            DsTkChangeService.DsTkMaterialRow row,
            HashSet<string> modelRooms,
            HashSet<string> readable,
            Dictionary<(string, string, int), double> qtyByKey,
            Dictionary<(string, string), double> totalByRoom,
            Dictionary<(string, string, int), HashSet<int>> usage,
            ModelProblems problems)
        {
            var entries = TkQtyScheduleMapping.ForWorkSet(row.WorkSetId ?? 0);
            var roomKey = RoomKey(row.RoomName);
            var sourceTitle = string.Join(" + ", entries.Select(e => e.Title));

            DsTkTargetPosition Make(DsTkTargetStatus status, string reason, DsTkTargetLine headLine = null,
                List<DsTkTargetLine> memberLines = null, string blockedBySourceCode = null) => new()
            {
                BlockedBySourceCode = blockedBySourceCode,
                ClientMaterialId = row.ClientMaterialId,
                MaterialSetId = row.MaterialSetId,
                WorkSetId = row.WorkSetId,
                RoomName = row.RoomName,
                WorkSetName = row.WorkSetName,
                MaterialName = row.MaterialName,
                SourceTitle = sourceTitle,
                Status = status,
                Reason = reason,
                Head = headLine ?? new DsTkTargetLine
                {
                    MaterialId = row.MaterialId ?? 0,
                    MaterialName = row.MaterialName,
                    MyspaceUnit = row.UnitName,
                    CurrentQty = row.MaterialCnt
                },
                Members = memberLines ?? new List<DsTkTargetLine>()
            };

            if (entries.Count == 0)
                return Make(DsTkTargetStatus.NotFromModel, "конструктив не берётся из модели — объём как в ДС");

            if (string.IsNullOrWhiteSpace(roomKey) || !modelRooms.Contains(roomKey))
                return Make(DsTkTargetStatus.Blocked, "комнаты нет в модели Revit (проверьте имя помещения)");

            // Источники, по которым реально считаем: у «фурнитуры» — ведомость дверей.
            var scheduleCodes = new List<string>();
            string countFromCode = null;
            foreach (var entry in entries)
            {
                var code = entry.IsDerived ? entry.CountFromCode.Trim() : entry.Code;
                if (!readable.Contains(code))
                {
                    var title = entry.IsDerived
                        ? TkQtyScheduleMapping.All.FirstOrDefault(e =>
                              string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase))?.Title ?? code
                        : entry.Title;
                    return Make(DsTkTargetStatus.Blocked, $"ведомость «{title}» не найдена или не читается");
                }

                if (entry.IsDerived)
                    countFromCode = code;
                else
                    scheduleCodes.Add(code);
            }

            var usedCodes = scheduleCodes
                .Concat(countFromCode != null ? new[] { countFromCode } : Array.Empty<string>())
                .ToList();
            foreach (var code in usedCodes)
            {
                if (problems.BadRooms.TryGetValue(code, out var bad))
                {
                    return Make(DsTkTargetStatus.Blocked,
                        $"в ведомости «{TitleOf(code)}» есть строки без помещения или с помещением, которого нет в модели: "
                        + $"{Short(bad)} — исправьте ведомость",
                        blockedBySourceCode: code);
                }

                if (problems.MissingId.TryGetValue((code.ToUpperInvariant(), roomKey), out var noId))
                {
                    return Make(DsTkTargetStatus.Blocked,
                        $"в ведомости «{TitleOf(code)}» в этой комнате есть строки без ID: {Short(noId)} — добавьте ID в модели");
                }
            }

            var zeroMissing = entries.Any(e => e.ZeroMissingSetItems);
            var revitUnit = countFromCode != null ? "шт" : entries.First(e => !e.IsDerived).QuantityUnit;

            foreach (var materialId in MaterialIdsOf(row))
            {
                foreach (var code in scheduleCodes)
                {
                    if (usage.TryGetValue((code.ToUpperInvariant(), roomKey, materialId), out var cms) && cms.Count > 1)
                    {
                        return Make(DsTkTargetStatus.Blocked,
                            $"материал {materialId} стоит в {cms.Count} позициях этой комнаты, "
                            + "объём из ведомости не разделить — правьте в MySpace");
                    }
                }
            }

            double? Target(int materialId, out bool hit)
            {
                hit = false;
                if (countFromCode != null)
                {
                    hit = true;
                    return Round(totalByRoom.TryGetValue(
                        (countFromCode.ToUpperInvariant(), roomKey), out var count) ? count : 0d);
                }

                double sum = 0;
                foreach (var code in scheduleCodes)
                {
                    if (qtyByKey.TryGetValue((code.ToUpperInvariant(), roomKey, materialId), out var qty))
                    {
                        hit = true;
                        sum += qty;
                    }
                }

                return Round(sum);
            }

            var headTarget = Target(row.MaterialId ?? 0, out var headFound);
            var head = new DsTkTargetLine
            {
                MaterialId = row.MaterialId ?? 0,
                MaterialName = row.MaterialName,
                MyspaceUnit = row.UnitName,
                RevitUnit = revitUnit,
                CurrentQty = row.MaterialCnt,
                TargetQty = headTarget,
                Note = countFromCode != null
                    ? "по числу дверей комнаты в ТК"
                    : headFound ? null : "нет в модели → 0"
            };

            // Объём, который уйдёт в 0 только потому, что материала ДС нет в ведомости.
            var zeroedByMissing = row.MaterialCnt is > 0d
                                  && headTarget is not null && QtyEquals(headTarget, 0d)
                                  && (countFromCode != null || !headFound);

            var members = new List<DsTkTargetLine>();
            foreach (var item in row.SetItems ?? new List<ClientMaterialSetItemDto>())
            {
                if (item?.MaterialId is not > 0)
                    continue;

                var target = Target(item.MaterialId.Value, out var found);
                string note = null;
                if (countFromCode != null)
                    note = "по числу дверей комнаты в ТК";
                else if (!found && zeroMissing)
                {
                    note = "нет в ведомости → 0";
                    if (item.MaterialCnt is > 0d)
                        zeroedByMissing = true;
                }
                else if (!found)
                {
                    target = null;
                    note = "нет в ведомости — как в ДС";
                }

                members.Add(new DsTkTargetLine
                {
                    MaterialId = item.MaterialId.Value,
                    IsSetMember = true,
                    MaterialName = item.MaterialName,
                    MyspaceUnit = item.UnitName,
                    RevitUnit = revitUnit,
                    CurrentQty = item.MaterialCnt,
                    TargetQty = target,
                    Note = note
                });
            }

            // В модели у этой комнаты другой материал того же источника, а материала ДС нет:
            // скорее всего неверный ID в модели. Ноль в ДС тут хуже, чем остановка.
            if (zeroedByMissing)
            {
                var foreign = usedCodes
                    .SelectMany(c => problems.Foreign.TryGetValue((c.ToUpperInvariant(), roomKey), out var list)
                        ? (IEnumerable<string>)list
                        : Enumerable.Empty<string>())
                    .Distinct()
                    .ToList();
                if (foreign.Count > 0)
                {
                    return Make(DsTkTargetStatus.Blocked,
                        $"материала ДС нет в модели, а в этой комнате в модели другой материал: {Short(foreign)} — "
                        + "проверьте ID в модели или замените материал в MySpace",
                        head, members);
                }
            }

            var changed = new[] { head }.Concat(members).Where(l => l.IsChanged).ToList();
            if (changed.Count == 0)
                return Make(DsTkTargetStatus.Same, null, head, members);

            if (row.IsAtomMeasure)
                return Make(DsTkTargetStatus.Skipped, "штучный материал — объём правится только в MySpace", head, members);

            // Шапку шлём всегда, когда позиция меняется, — её единицу сервер проверяет тоже.
            foreach (var line in changed.Prepend(head).Distinct())
            {
                var unitRevit = QtyUnits.Normalize(line.RevitUnit);
                var unitMyspace = QtyUnits.Normalize(line.MyspaceUnit);
                if (unitMyspace == null)
                    return Make(DsTkTargetStatus.Blocked, $"у материала {line.MaterialId} нет единицы в MySpace", head, members);
                if (!string.Equals(unitRevit, unitMyspace, StringComparison.Ordinal))
                {
                    return Make(DsTkTargetStatus.Blocked,
                        $"материал {line.MaterialId}: единица ведомости «{line.RevitUnit}» ≠ MySpace «{line.MyspaceUnit}»",
                        head, members);
                }
            }

            return Make(DsTkTargetStatus.Change, null, head, members);
        }

        /// <summary>Строки нужных ведомостей, которым нет пары в ДС, и строки без помещения.</summary>
        static void CollectModelOnly(
            DsTkTargetResult result,
            TkQtyScheduleSnapshot schedule,
            Dictionary<string, HashSet<int>> inDsByRoom)
        {
            var mappedCodes = MappedCodes();

            foreach (var skippedRow in schedule.SkippedRows.Where(r =>
                         mappedCodes.Contains(r.SourceCode ?? string.Empty) && string.IsNullOrWhiteSpace(r.RoomName)))
            {
                result.Unassigned.Add(new DsTkTargetIssue
                {
                    MaterialName = skippedRow.Text,
                    Text = $"строка ведомости «{skippedRow.ScheduleName}» без ID и без помещения",
                    SourceCode = skippedRow.SourceCode
                });
            }

            var seen = new HashSet<(string, int)>();
            foreach (var line in schedule.Lines)
            {
                if (line.MaterialId <= 0 || !mappedCodes.Contains(line.SourceCode ?? string.Empty))
                    continue;

                if (string.IsNullOrWhiteSpace(line.RoomName))
                {
                    result.Unassigned.Add(new DsTkTargetIssue
                    {
                        MaterialName = Name(line),
                        Text = $"строка ведомости «{line.ScheduleName}» без помещения",
                        SourceCode = line.SourceCode
                    });
                    continue;
                }

                var roomKey = RoomKey(line.RoomName);
                if (inDsByRoom.TryGetValue(roomKey, out var ids) && ids.Contains(line.MaterialId))
                    continue;
                if (!seen.Add((roomKey, line.MaterialId)))
                    continue;

                result.ExtraInModel.Add(new DsTkTargetIssue
                {
                    RoomName = line.RoomName,
                    MaterialName = Name(line),
                    Text = "в модели есть, в ТК этой комнаты нет — заменить или добавить в MySpace"
                });
            }
        }

        /// <summary>Источники, привязанные к конструктивам ДС (у фурнитуры — ведомость дверей).</summary>
        static HashSet<string> MappedCodes() => new(
            TkQtyScheduleMapping.All
                .Where(e => e.Enabled && e.WorkSetIds is { Count: > 0 })
                .Select(e => e.IsDerived ? e.CountFromCode.Trim() : e.Code),
            StringComparer.OrdinalIgnoreCase);

        static Dictionary<string, HashSet<int>> MaterialsInDsByRoom(IEnumerable<DsTkChangeService.DsTkMaterialRow> rows)
        {
            var map = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Where(r => r.ActionType != 1))
            {
                var roomKey = RoomKey(row.RoomName);
                if (!map.TryGetValue(roomKey, out var set))
                    map[roomKey] = set = new HashSet<int>();
                foreach (var id in MaterialIdsOf(row))
                    set.Add(id);
            }

            return map;
        }

        static ModelProblems FindModelProblems(
            TkQtyScheduleSnapshot schedule,
            HashSet<string> modelRooms,
            Dictionary<string, HashSet<int>> inDsByRoom)
        {
            var problems = new ModelProblems();
            var mappedCodes = MappedCodes();

            foreach (var line in schedule.Lines)
            {
                var code = line.SourceCode ?? string.Empty;
                if (line.MaterialId <= 0 || !mappedCodes.Contains(code))
                    continue;

                var roomKey = RoomKey(line.RoomName);
                if (string.IsNullOrWhiteSpace(line.RoomName) || !modelRooms.Contains(roomKey))
                {
                    var where = string.IsNullOrWhiteSpace(line.RoomName) ? "без помещения" : $"«{line.RoomName}»";
                    ModelProblems.Add(problems.BadRooms, code, $"{Name(line)} ({where})");
                    continue;
                }

                if (!inDsByRoom.TryGetValue(roomKey, out var ids) || !ids.Contains(line.MaterialId))
                    ModelProblems.Add(problems.Foreign, (code.ToUpperInvariant(), roomKey), Name(line));
            }

            foreach (var row in schedule.SkippedRows)
            {
                var code = row.SourceCode ?? string.Empty;
                if (!mappedCodes.Contains(code))
                    continue;

                if (string.IsNullOrWhiteSpace(row.RoomName) || !modelRooms.Contains(RoomKey(row.RoomName)))
                {
                    var where = string.IsNullOrWhiteSpace(row.RoomName) ? "без помещения" : $"«{row.RoomName}»";
                    ModelProblems.Add(problems.BadRooms, code, $"«{row.Text}» без ID ({where})");
                    continue;
                }

                ModelProblems.Add(problems.MissingId, (code.ToUpperInvariant(), RoomKey(row.RoomName)), $"«{row.Text}»");
            }

            return problems;
        }

        static string TitleOf(string code) =>
            TkQtyScheduleMapping.All.FirstOrDefault(e =>
                string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase))?.Title ?? code;

        static string Short(IReadOnlyCollection<string> items) =>
            string.Join(", ", items.Take(3)) + (items.Count > 3 ? $" и ещё {items.Count - 3}" : string.Empty);

        static IEnumerable<int> MaterialIdsOf(DsTkChangeService.DsTkMaterialRow row)
        {
            if (row.MaterialId is > 0)
                yield return row.MaterialId.Value;
            foreach (var item in row.SetItems ?? new List<ClientMaterialSetItemDto>())
            {
                if (item?.MaterialId is > 0 && item.MaterialId != row.MaterialId)
                    yield return item.MaterialId.Value;
            }
        }

        static string RoomKey(string roomName) =>
            DsAreaCompareService.GetRoomCompareKey(roomName ?? string.Empty).ToUpperInvariant();

        static string Name(TkQtyScheduleLine line) =>
            string.IsNullOrWhiteSpace(line.MaterialName)
                ? line.MaterialId.ToString(CultureInfo.InvariantCulture)
                : $"{line.MaterialId} / {line.MaterialName}";

        static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Тело items для /revit/plugin/ds/tk-change/apply/: шапка — всегда, материалы набора —
        /// только изменённые. Остальной состав набора сервер берёт из ДС сам.
        /// </summary>
        public static JArray BuildApplyItems(IEnumerable<DsTkTargetPosition> positions)
        {
            var items = new JArray();
            foreach (var position in positions ?? Enumerable.Empty<DsTkTargetPosition>())
            {
                var head = position.Head;
                var item = new JObject
                {
                    ["client_material_id"] = position.ClientMaterialId,
                    ["material_cnt"] = head.TargetQty ?? head.CurrentQty,
                    ["expected_material_cnt"] = head.CurrentQty,
                    ["unit"] = head.RevitUnit
                };

                var members = position.Members.Where(m => m.IsChanged).ToList();
                if (members.Count > 0)
                {
                    item["set_items"] = new JArray(members.Select(m => new JObject
                    {
                        ["material_id"] = m.MaterialId,
                        ["material_cnt"] = m.TargetQty,
                        ["expected_material_cnt"] = m.CurrentQty,
                        ["unit"] = m.RevitUnit
                    }));
                }

                items.Add(item);
            }

            return items;
        }

        /// <summary>Весь расчёт для разбора: каждая строка ДС с вводом, было/станет и почему.</summary>
        public static JObject BuildDebugJson(DsTkTargetResult result)
        {
            static JObject Line(DsTkTargetLine line)
            {
                var o = new JObject
                {
                    ["material_id"] = line.MaterialId,
                    ["material_name"] = line.MaterialName,
                    ["ds_qty"] = line.CurrentQty,
                    ["target_qty"] = line.TargetQty,
                    ["changed"] = line.IsChanged,
                    ["unit_myspace"] = line.MyspaceUnit,
                    ["unit_revit"] = line.RevitUnit
                };
                if (!string.IsNullOrWhiteSpace(line.Note))
                    o["note"] = line.Note;
                return o;
            }

            static JObject Issue(DsTkTargetIssue issue) => new()
            {
                ["room_name"] = issue.RoomName,
                ["material_name"] = issue.MaterialName,
                ["text"] = issue.Text
            };

            result ??= new DsTkTargetResult();
            return new JObject
            {
                ["to_send"] = result.ToSend.Count,
                ["changed_lines"] = result.ChangedLineCount,
                ["blocked"] = result.Blocked.Count,
                ["skipped"] = result.Skipped.Count,
                ["not_from_model"] = result.NotFromModel.Count,
                ["positions"] = new JArray(result.Positions.Select(p =>
                {
                    var o = new JObject
                    {
                        ["status"] = p.Status.ToString(),
                        ["room_name"] = p.RoomName,
                        ["work_set_id"] = p.WorkSetId,
                        ["work_set_name"] = p.WorkSetName,
                        ["client_material_id"] = p.ClientMaterialId,
                        ["material_set_id"] = p.MaterialSetId,
                        ["source"] = p.SourceTitle
                    };
                    if (!string.IsNullOrWhiteSpace(p.Reason))
                        o["reason"] = p.Reason;
                    if (p.Head != null)
                        o["head"] = Line(p.Head);
                    if (p.Members.Count > 0)
                        o["set_items"] = new JArray(p.Members.Select(Line));
                    return o;
                })),
                ["extra_in_model"] = new JArray(result.ExtraInModel.Select(Issue)),
                ["unassigned"] = new JArray(result.Unassigned.Select(Issue))
            };
        }
    }
}
