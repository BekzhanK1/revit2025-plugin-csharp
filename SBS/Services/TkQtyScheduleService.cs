using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class TkQtyScheduleLine
    {
        public string SourceCode { get; init; }
        public string ScheduleName { get; init; }
        public string RoomName { get; init; }
        public int MaterialId { get; init; }
        public string MaterialName { get; init; }
        public double Quantity { get; init; }
        public string Unit { get; init; }
    }

    public sealed class TkQtyScheduleSourceInfo
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string ScheduleNameExpected { get; set; }
        public string ScheduleNameFound { get; set; }
        public bool Found { get; set; }
        public int LineCount { get; set; }
        public string Message { get; set; }
    }

    public sealed class TkQtyScheduleSnapshot
    {
        public List<TkQtyScheduleLine> Lines { get; set; } = new();
        public List<TkQtyScheduleSourceInfo> Sources { get; set; } = new();

        /// <summary>Сумма qty по roomKey → materialId.</summary>
        public Dictionary<string, Dictionary<int, double>> SumByRoomAndMaterial { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Qty из ведомостей без помещения (нет группировки / колонки комнаты).
        /// </summary>
        public Dictionary<int, double> SumByMaterialUngrouped { get; set; } = new();
    }

    /// <summary>
    /// Читает ведомости по <see cref="TkQtyScheduleMapping"/> → строки material_id + qty по комнатам.
    /// </summary>
    public static class TkQtyScheduleService
    {
        static readonly Regex NumberRegex = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);
        static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        public static TkQtyScheduleSnapshot Collect(Document doc)
        {
            var snapshot = new TkQtyScheduleSnapshot();
            if (doc == null)
                return snapshot;

            // Подхватываем правки конфига без перезапуска Revit.
            TkQtyScheduleMapping.Reload();

            var schedulesByName = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(IsReadableSchedule)
                .GroupBy(s => NormalizeName(s.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in TkQtyScheduleMapping.All.Where(e => e.Enabled))
            {
                var expected = string.Join(" | ", entry.ScheduleNamesExact ?? new List<string>());
                var source = new TkQtyScheduleSourceInfo
                {
                    Code = entry.Code,
                    Title = entry.Title,
                    ScheduleNameExpected = expected
                };

                if (!TryFindSchedules(schedulesByName, entry.ScheduleNamesExact, out var schedules))
                {
                    source.Message = $"Ведомость не найдена: {expected}";
                    snapshot.Sources.Add(source);
                    continue;
                }

                if (!TryPickReadableSchedule(
                        schedules,
                        entry,
                        out var schedule,
                        out var headers,
                        out var rowCount,
                        out var colId,
                        out var colName,
                        out var colQty,
                        out var qtyHeader,
                        out var colRoom,
                        out var pickError))
                {
                    source.Found = true;
                    source.ScheduleNameFound = schedules[0].Name;
                    source.Message = pickError;
                    snapshot.Sources.Add(source);
                    continue;
                }

                source.ScheduleNameFound = schedule.Name;
                source.Found = true;

                // Если qty-колонка в мм, а scale=1 — применяем 0.001; если уже «м» / «шт» — не трогаем scale из конфига.
                var effectiveScale = ResolveEffectiveScale(entry.QuantityScale, qtyHeader, entry.QuantityUnit);

                List<TkQtyScheduleLine> lines;
                switch (entry.Mode)
                {
                    case TkQtyScheduleMapping.ParseMode.FlatByRoomColumn:
                        if (colRoom == null)
                        {
                            source.Message = "Нет колонки помещения";
                            snapshot.Sources.Add(source);
                            continue;
                        }

                        lines = ParseFlat(schedule, rowCount, headers, entry, colId.Value, colName, colQty, colRoom.Value, effectiveScale);
                        break;

                    default:
                        lines = ParseGrouped(schedule, rowCount, headers, entry, colId.Value, colName, colQty, colRoom, effectiveScale);
                        break;
                }

                source.LineCount = lines.Count;
                source.Message = lines.Count > 0
                    ? null
                    : "Нет строк с ID материала (проверьте группировку / колонку ID)";
                snapshot.Sources.Add(source);
                snapshot.Lines.AddRange(lines);
            }

            snapshot.SumByRoomAndMaterial = BuildSums(snapshot.Lines);
            snapshot.SumByMaterialUngrouped = BuildUngroupedSums(snapshot.Lines);
            return snapshot;
        }

        static double ResolveEffectiveScale(double configuredScale, string qtyHeader, string unit)
        {
            var scale = configuredScale <= 0 ? 1d : configuredScale;
            var header = qtyHeader ?? string.Empty;

            // Фактически «шт» — не применяем мм→м.
            if (header.IndexOf("шт", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1d;

            // Колонка в мм, ожидание «м» — мм→м даже если в конфиге забыли scale.
            if (header.IndexOf("мм", StringComparison.OrdinalIgnoreCase) >= 0
                && string.Equals(unit, "м", StringComparison.OrdinalIgnoreCase)
                && scale == 1d)
                return 0.001d;

            return scale;
        }

        static Dictionary<string, Dictionary<int, double>> BuildSums(IEnumerable<TkQtyScheduleLine> lines)
        {
            var map = new Dictionary<string, Dictionary<int, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines ?? Enumerable.Empty<TkQtyScheduleLine>())
            {
                if (line.MaterialId <= 0 || string.IsNullOrWhiteSpace(line.RoomName))
                    continue;

                var roomKey = DsAreaCompareService.GetRoomCompareKey(line.RoomName);
                if (string.IsNullOrWhiteSpace(roomKey))
                    continue;

                if (!map.TryGetValue(roomKey, out var byMat))
                {
                    byMat = new Dictionary<int, double>();
                    map[roomKey] = byMat;
                }

                byMat.TryGetValue(line.MaterialId, out var prev);
                byMat[line.MaterialId] = prev + line.Quantity;
            }

            return map;
        }

        static Dictionary<int, double> BuildUngroupedSums(IEnumerable<TkQtyScheduleLine> lines)
        {
            var map = new Dictionary<int, double>();
            foreach (var line in lines ?? Enumerable.Empty<TkQtyScheduleLine>())
            {
                if (line.MaterialId <= 0 || !string.IsNullOrWhiteSpace(line.RoomName))
                    continue;

                map.TryGetValue(line.MaterialId, out var prev);
                map[line.MaterialId] = prev + line.Quantity;
            }

            return map;
        }

        static List<TkQtyScheduleLine> ParseFlat(
            ViewSchedule schedule,
            int rowCount,
            Dictionary<string, int> headers,
            TkQtyScheduleMapping.Entry entry,
            int colId,
            int? colName,
            int? colQty,
            int colRoom,
            double scale)
        {
            var lines = new List<TkQtyScheduleLine>();
            for (var r = 1; r < rowCount; r++)
            {
                if (IsNoiseRow(schedule, r, headers))
                    continue;

                var room = GetCell(schedule, r, colRoom).Trim();
                if (string.IsNullOrWhiteSpace(room) || IsNoiseLabel(room))
                    continue;

                if (!TryParseMaterialId(GetCell(schedule, r, colId), out var materialId))
                    continue;

                var qty = ResolveQuantity(schedule, r, colQty, scale);
                var name = colName is int cn ? GetCell(schedule, r, cn).Trim() : null;

                lines.Add(new TkQtyScheduleLine
                {
                    SourceCode = entry.Code,
                    ScheduleName = schedule.Name,
                    RoomName = room,
                    MaterialId = materialId,
                    MaterialName = string.IsNullOrWhiteSpace(name) ? null : name,
                    Quantity = qty,
                    Unit = entry.QuantityUnit
                });
            }

            return lines;
        }

        static List<TkQtyScheduleLine> ParseGrouped(
            ViewSchedule schedule,
            int rowCount,
            Dictionary<string, int> headers,
            TkQtyScheduleMapping.Entry entry,
            int colId,
            int? colName,
            int? colQty,
            int? colRoom,
            double scale)
        {
            var lines = new List<TkQtyScheduleLine>();
            string currentRoom = null;

            for (var r = 1; r < rowCount; r++)
            {
                if (IsNoiseRow(schedule, r, headers))
                    continue;

                var idRaw = GetCell(schedule, r, colId).Trim();
                var roomFromCol = colRoom is int cr ? GetCell(schedule, r, cr).Trim() : null;
                var hasMaterialId = TryParseMaterialId(idRaw, out var materialId);
                var qty = ResolveQuantity(schedule, r, colQty, scale);
                var name = colName is int cn ? GetCell(schedule, r, cn).Trim() : null;

                // Строка-заголовок группы: комната в Room-колонке или нечисловой ID (как LED).
                if (!hasMaterialId)
                {
                    var roomHeader = !string.IsNullOrWhiteSpace(roomFromCol)
                        ? roomFromCol
                        : (!string.IsNullOrWhiteSpace(idRaw) && !IsNoiseLabel(idRaw) ? idRaw : null);

                    if (!string.IsNullOrWhiteSpace(roomHeader) && !IsNoiseLabel(roomHeader))
                        currentRoom = roomHeader.Trim();
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(roomFromCol) && !IsNoiseLabel(roomFromCol))
                    currentRoom = roomFromCol.Trim();

                lines.Add(new TkQtyScheduleLine
                {
                    SourceCode = entry.Code,
                    ScheduleName = schedule.Name,
                    RoomName = string.IsNullOrWhiteSpace(currentRoom) ? null : currentRoom,
                    MaterialId = materialId,
                    MaterialName = string.IsNullOrWhiteSpace(name) ? null : name,
                    Quantity = qty,
                    Unit = entry.QuantityUnit
                });
            }

            return lines;
        }

        static double ResolveQuantity(ViewSchedule schedule, int row, int? colQty, double scale)
        {
            if (colQty == null)
                return 1d * scale;

            var raw = GetCell(schedule, row, colQty.Value);
            var parsed = ParseNullableDouble(raw);
            if (parsed == null)
                return 1d * scale;

            return parsed.Value * (scale <= 0 ? 1d : scale);
        }

        static bool TryFindSchedules(
            Dictionary<string, ViewSchedule> byName,
            IReadOnlyList<string> names,
            out List<ViewSchedule> schedules)
        {
            schedules = new List<ViewSchedule>();
            if (names == null)
                return false;

            var seen = new HashSet<long>();
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (!byName.TryGetValue(NormalizeName(name), out var schedule) || schedule == null)
                    continue;
                if (!seen.Add(schedule.Id.Value))
                    continue;
                schedules.Add(schedule);
            }

            return schedules.Count > 0;
        }

        static bool TryPickReadableSchedule(
            IReadOnlyList<ViewSchedule> schedules,
            TkQtyScheduleMapping.Entry entry,
            out ViewSchedule schedule,
            out Dictionary<string, int> headers,
            out int rowCount,
            out int? colId,
            out int? colName,
            out int? colQty,
            out string qtyHeader,
            out int? colRoom,
            out string error)
        {
            schedule = null;
            headers = null;
            rowCount = 0;
            colId = null;
            colName = null;
            colQty = null;
            qtyHeader = null;
            colRoom = null;
            error = "Не удалось прочитать таблицу";

            var sawMissingId = false;
            foreach (var candidate in schedules)
            {
                if (!TryReadTable(candidate, out var candidateHeaders, out var candidateRows))
                    continue;

                var id = ResolveColumnExact(candidateHeaders, entry.MaterialIdColumnsExact, out _);
                if (id == null)
                {
                    sawMissingId = true;
                    continue;
                }

                schedule = candidate;
                headers = candidateHeaders;
                rowCount = candidateRows;
                colId = id;
                colName = ResolveColumnExact(candidateHeaders, entry.MaterialNameColumnsExact, out _);
                colQty = ResolveColumnExact(candidateHeaders, entry.QuantityColumnsExact, out qtyHeader);
                colRoom = ResolveColumnExact(candidateHeaders, entry.RoomColumnsExact, out _);
                error = null;
                return true;
            }

            error = sawMissingId
                ? "Нет колонки ID материала (добавьте «ID материала» в ведомость или отключите источник в конфиге)"
                : "Не удалось прочитать таблицу";
            return false;
        }

        static bool TryParseMaterialId(string raw, out int materialId)
        {
            materialId = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var trimmed = raw.Trim();
            // Целое ID целиком (или с суффиксом «шт»), без дробных «12.5».
            var m = NumberRegex.Match(trimmed);
            if (!m.Success)
                return false;

            if (m.Value.IndexOfAny(new[] { '.', ',' }) >= 0)
                return false;

            // Избегаем ложных ID из текста вроде «1 пост» / «2-кл» в колонке ID при сбое маппинга.
            if (m.Index > 0)
            {
                var before = trimmed.Substring(0, m.Index);
                if (before.Any(char.IsLetter))
                    return false;
            }

            return int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out materialId)
                   && materialId > 0;
        }

        static bool IsReadableSchedule(ViewSchedule schedule)
        {
            if (schedule == null || schedule.IsTemplate)
                return false;
            try
            {
                return !schedule.Definition.IsKeySchedule;
            }
            catch
            {
                return true;
            }
        }

        static bool TryReadTable(
            ViewSchedule schedule,
            out Dictionary<string, int> headers,
            out int rowCount)
        {
            headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            rowCount = 0;

            TableData td;
            try { td = schedule.GetTableData(); }
            catch { return false; }

            var body = td?.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows <= 0 || body.NumberOfColumns <= 0)
                return false;

            rowCount = body.NumberOfRows;
            for (var c = 0; c < body.NumberOfColumns; c++)
            {
                var header = NormalizeHeader(schedule.GetCellText(SectionType.Body, 0, c) ?? string.Empty);
                if (string.IsNullOrWhiteSpace(header)) continue;
                if (!headers.ContainsKey(header))
                    headers[header] = c;
            }

            return headers.Count > 0;
        }

        static int? ResolveColumnExact(
            Dictionary<string, int> headers,
            IReadOnlyList<string> names,
            out string matchedName)
        {
            matchedName = null;
            if (headers == null || names == null)
                return null;

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var key = NormalizeHeader(name);
                if (headers.TryGetValue(key, out var idx))
                {
                    matchedName = name;
                    return idx;
                }
            }

            return null;
        }

        static string GetCell(ViewSchedule schedule, int row, int col) =>
            schedule.GetCellText(SectionType.Body, row, col) ?? string.Empty;

        static bool IsNoiseRow(ViewSchedule schedule, int row, Dictionary<string, int> headers)
        {
            foreach (var col in headers.Values)
            {
                if (IsNoiseLabel(GetCell(schedule, row, col).Trim()))
                    return true;
            }

            return false;
        }

        static bool IsNoiseLabel(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // «Общий итог», «Итого», «Итог:», иногда «Total»
            if (text.IndexOf("общий итог", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (text.Equals("итог", StringComparison.OrdinalIgnoreCase)
                || text.Equals("итого", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("итог", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("итого", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text.Equals("total", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("total", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        static double? ParseNullableDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = NumberRegex.Match(s);
            if (!m.Success) return null;
            var token = m.Value.Replace(',', '.');
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            return name.Trim().Replace("<", string.Empty).Replace(">", string.Empty).Trim();
        }

        static string NormalizeHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return string.Empty;
            return WhitespaceRegex.Replace(header.Trim(), " ");
        }
    }
}
