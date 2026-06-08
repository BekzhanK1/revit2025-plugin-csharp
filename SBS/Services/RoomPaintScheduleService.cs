using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    public static class RoomPaintScheduleService
    {
        public const string ScheduleNameExact = "Спецификация поклейка обоев с покраской";
        public const string LitersFormula = "Литр, л = Площадь × 0,2 мм";
        public const string LitersFormulaNote =
            "Расчётное поле ведомости Revit (тип «Объем»): площадь покраски × толщина слоя 0,2 мм.";

        static readonly Regex NumberRegex = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        static readonly string[] RoomColumns = { "Помещение", "Помещения" };
        static readonly string[] IdColumns = { "ID" };
        static readonly string[] TypeColumns = { "Тип" };
        static readonly string[] AreaColumns = { "Площадь, м²", "Площадь" };
        static readonly string[] LiterColumns = { "Литр, л", "Литр" };
        static readonly string[] MassColumns = { "Масса, кг", "Масса" };

        public static RoomPaintScheduleResult Collect(Document doc)
        {
            var result = new RoomPaintScheduleResult
            {
                Source = new RoomPaintSourceInfo
                {
                    ScheduleNameExpected = ScheduleNameExact,
                    LitersFormula = LitersFormula,
                    LitersFormulaNote = LitersFormulaNote
                }
            };

            if (doc == null)
            {
                result.Source.Message = "Документ не задан";
                return result;
            }

            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(IsReadableSchedule)
                .GroupBy(s => NormalizeName(s.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            if (!schedules.TryGetValue(NormalizeName(ScheduleNameExact), out var schedule))
            {
                result.Source.Message =
                    $"Ведомость «{ScheduleNameExact}» не найдена (точное имя без < >).";
                return result;
            }

            result.Source.ScheduleNameFound = schedule.Name;

            if (!TryReadTable(schedule, out var headers, out var rowCount))
            {
                result.Source.Message = $"Ведомость «{schedule.Name}»: не удалось прочитать таблицу.";
                return result;
            }

            var colRoom = ResolveColumnExact(headers, RoomColumns, out var roomColName);
            var colId = ResolveColumnExact(headers, IdColumns, out _);
            var colType = ResolveColumnExact(headers, TypeColumns, out _);
            var colArea = ResolveColumnExact(headers, AreaColumns, out var areaColName);
            var colLiter = ResolveColumnExact(headers, LiterColumns, out var literColName);
            var colMass = ResolveColumnExact(headers, MassColumns, out var massColName);

            if (colArea == null && colLiter == null)
            {
                result.Source.Message =
                    $"Ведомость «{schedule.Name}»: нет колонок «Площадь, м²» или «Литр, л».";
                return result;
            }

            var byRoom = new Dictionary<string, List<RoomPaintItem>>(StringComparer.OrdinalIgnoreCase);
            var details = new StringBuilder();
            string currentRoom = null;

            for (var r = 1; r < rowCount; r++)
            {
                if (IsGrandTotalRow(schedule, r, headers))
                    continue;

                var roomCell = colRoom != null ? GetCell(schedule, r, colRoom.Value).Trim() : string.Empty;
                var area = colArea != null ? ParseNullableDouble(GetCell(schedule, r, colArea.Value)) : null;
                var liters = colLiter != null ? ParseNullableDouble(GetCell(schedule, r, colLiter.Value)) : null;
                var mass = colMass != null ? ParseNullableDouble(GetCell(schedule, r, colMass.Value)) : null;

                if (!string.IsNullOrWhiteSpace(roomCell) && area == null && liters == null && mass == null)
                {
                    currentRoom = roomCell;
                    continue;
                }

                if (area == null && liters == null && mass == null)
                {
                    var headerRoom = DetectGroupRoomName(schedule, r, headers, colArea ?? colLiter ?? 0);
                    if (!string.IsNullOrWhiteSpace(headerRoom))
                        currentRoom = headerRoom;
                    continue;
                }

                var room = !string.IsNullOrWhiteSpace(roomCell) ? roomCell : currentRoom;
                if (string.IsNullOrWhiteSpace(room))
                    continue;

                currentRoom = room;

                var item = new RoomPaintItem
                {
                    ProductId = colId != null ? GetCell(schedule, r, colId.Value).Trim() : string.Empty,
                    IdSourceNote = colId != null ? "ведомость · колонка ID" : "—",
                    MaterialType = colType != null ? GetCell(schedule, r, colType.Value).Trim() : string.Empty,
                    AreaM2 = area,
                    Liters = liters,
                    MassKg = mass
                };

                if (!byRoom.TryGetValue(room, out var list))
                {
                    list = new List<RoomPaintItem>();
                    byRoom[room] = list;
                }

                list.Add(item);

                details.AppendLine(FormatDetailLine(room, item));
            }

            if (byRoom.Count == 0)
            {
                result.Source.Message = BuildColumnsMessage(schedule.Name, roomColName, areaColName, literColName, massColName)
                                      + " — строк с данными нет.";
                return result;
            }

            result.Rooms = byRoom
                .OrderBy(kv => SortRoomKey(kv.Key))
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new RoomMaterialsRoomRow
                {
                    RoomName = kv.Key,
                    PaintItems = kv.Value
                })
                .ToList();

            result.Source.Found = true;
            result.Source.Message = BuildColumnsMessage(schedule.Name, roomColName, areaColName, literColName, massColName);
            result.Source.DetailLines = details.ToString().TrimEnd();

            return result;
        }

        static string BuildColumnsMessage(
            string scheduleName,
            string roomCol,
            string areaCol,
            string literCol,
            string massCol)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(roomCol)) parts.Add($"«{roomCol}»");
            if (!string.IsNullOrWhiteSpace(areaCol)) parts.Add($"«{areaCol}»");
            if (!string.IsNullOrWhiteSpace(literCol)) parts.Add($"«{literCol}»");
            if (!string.IsNullOrWhiteSpace(massCol)) parts.Add($"«{massCol}»");
            return $"«{scheduleName}»: {string.Join(", ", parts)}";
        }

        static string FormatDetailLine(string room, RoomPaintItem item)
        {
            var area = item.AreaM2.HasValue ? $"{item.AreaM2.Value:0.##} м²" : "—";
            var liters = item.Liters.HasValue ? $"{item.Liters.Value:0.##} л" : "—";
            var mass = item.MassKg.HasValue ? $"{item.MassKg.Value:0.##} кг" : "—";
            var type = string.IsNullOrWhiteSpace(item.MaterialType) ? "краска" : item.MaterialType;
            var id = string.IsNullOrWhiteSpace(item.ProductId) ? "" : $" ID {item.ProductId}";
            return $"{room}: {type}{id} — {area} → {liters} ({mass})";
        }

        static int SortRoomKey(string name)
        {
            var m = NumberRegex.Match(name ?? string.Empty);
            return m.Success && int.TryParse(m.Value, out var n) ? n : int.MaxValue;
        }

        static string NormalizeName(string name) =>
            (name ?? string.Empty).Trim().Trim('<', '>').Trim();

        static bool IsReadableSchedule(ViewSchedule schedule)
        {
            if (schedule == null) return false;
            if (schedule.IsTemplate) return false;
            if (schedule.IsTitleblockRevisionSchedule) return false;
            if (schedule.IsInternalKeynoteSchedule) return false;
            return schedule.Definition != null;
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
                var header = (schedule.GetCellText(SectionType.Body, 0, c) ?? string.Empty).Trim();
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
            foreach (var name in names)
            {
                if (headers.TryGetValue(name, out var idx))
                {
                    matchedName = name;
                    return idx;
                }
            }

            return null;
        }

        static string GetCell(ViewSchedule schedule, int row, int col) =>
            schedule.GetCellText(SectionType.Body, row, col) ?? string.Empty;

        static bool IsGrandTotalRow(ViewSchedule schedule, int row, Dictionary<string, int> headers)
        {
            foreach (var col in headers.Values)
            {
                var text = (GetCell(schedule, row, col) ?? string.Empty).Trim();
                if (IsGrandTotalLabel(text))
                    return true;
            }

            return false;
        }

        static bool IsGrandTotalLabel(string text) =>
            !string.IsNullOrWhiteSpace(text) &&
            text.IndexOf("общий итог", StringComparison.OrdinalIgnoreCase) >= 0;

        static string DetectGroupRoomName(
            ViewSchedule schedule,
            int row,
            Dictionary<string, int> headers,
            int skipCol)
        {
            foreach (var col in headers.Values.Distinct())
            {
                if (col == skipCol) continue;
                var text = (GetCell(schedule, row, col) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (IsGrandTotalLabel(text)) continue;
                if (ParseNullableDouble(text) != null) continue;
                if (text.Equals("ID", StringComparison.OrdinalIgnoreCase)) continue;
                return text;
            }

            return null;
        }

        static double? ParseNullableDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var m = NumberRegex.Match(s);
            if (!m.Success) return null;
            var token = m.Value.Replace(',', '.');
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : null;
        }
    }
}
