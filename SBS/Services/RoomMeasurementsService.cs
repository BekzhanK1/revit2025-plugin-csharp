using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using static SmartRemont.ExportRooms.Services.RoomMeasurementsScheduleMapping;

namespace SmartRemont.ExportRooms.Services
{
    public static class RoomMeasurementsService
    {
        static readonly Regex NumberRegex = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        public static RoomMeasurementsSnapshot Collect(Document doc)
        {
            var schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(IsReadableSchedule)
                .GroupBy(s => NormalizeName(s.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var snapshot = new RoomMeasurementsSnapshot();
            var byKey = new Dictionary<string, ExtractResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in All)
            {
                var schedule = FindScheduleExact(schedules, entry.ScheduleNamesExact);
                ExtractResult extracted;
                string valueColUsed = null;
                string roomColUsed = null;

                if (schedule == null)
                {
                    extracted = new ExtractResult { ScheduleName = null };
                }
                else if (!TryReadTable(schedule, out var headers, out var rowCount))
                {
                    extracted = new ExtractResult { ScheduleName = schedule.Name };
                }
                else
                {
                    switch (entry.Mode)
                    {
                        case ParseMode.FlatByRoomColumn:
                            extracted = ExtractFlatByRoom(
                                schedule, headers, rowCount,
                                entry.RoomColumnsExact, entry.ValueColumnsExact,
                                entry.RoomBaseNamesFilter,
                                out roomColUsed, out valueColUsed);
                            break;
                        case ParseMode.GroupedByRoomHeader:
                            extracted = ExtractGroupedByRoom(
                                schedule, headers, rowCount,
                                entry.RoomColumnsExact, entry.ValueColumnsExact,
                                entry.RoomBaseNamesFilter,
                                out roomColUsed, out valueColUsed);
                            break;
                        case ParseMode.DoorsByRoom:
                            extracted = ExtractDoorsByRoom(
                                schedule, headers, rowCount,
                                entry.RoomColumnsExact, entry.ValueColumnsExact,
                                entry.RoomBaseNamesFilter,
                                doubleLeafOnly: entry.ParamCode == "DOUBLE_DOOR",
                                out roomColUsed, out valueColUsed);
                            break;
                        case ParseMode.SingleValueToFixedRoom:
                            extracted = ExtractSingleValueToFixedRoom(
                                schedule, headers, rowCount,
                                entry.ValueColumnsExact, entry.FixedRoomName,
                                out roomColUsed, out valueColUsed);
                            break;
                        default:
                            extracted = new ExtractResult { ScheduleName = schedule.Name };
                            break;
                    }
                }

                byKey[entry.ParamCode] = extracted;

                snapshot.Sources.Add(new RoomMeasurementSourceInfo
                {
                    param_code = entry.ParamCode,
                    param_name = entry.ParamName,
                    schedule_name_expected = string.Join(" | ", entry.ScheduleNamesExact),
                    schedule_name_found = schedule?.Name ?? "—",
                    Found = schedule != null && extracted.HasData,
                    Message = BuildSourceMessage(schedule, entry, valueColUsed, roomColUsed, extracted)
                });
            }

            var roomNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in byKey.Values)
            {
                foreach (var key in r.ByRoomOrEmpty.Keys) roomNames.Add(key);
                foreach (var key in r.ByRoomIntOrEmpty.Keys) roomNames.Add(key);
            }

            snapshot.Rooms = roomNames
                .OrderBy(SortRoomKey)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(name => new RoomMeasurementsRoomRow
                {
                    RoomName = name,
                    Parameters = All
                        .Where(entry => ParamAppliesToRoom(entry, name))
                        .Select(entry => new RoomMeasurementParamItem
                        {
                            param_code = entry.ParamCode,
                            param_name = entry.ParamName,
                            param_value = GetParamValue(byKey, entry, name)
                        })
                        .ToList()
                })
                .ToList();

            return snapshot;
        }

        static string BuildSourceMessage(
            ViewSchedule schedule,
            Entry entry,
            string valueCol,
            string roomCol,
            ExtractResult extracted)
        {
            if (schedule == null)
                return $"Не найдена ведомость «{string.Join("» или «", entry.ScheduleNamesExact)}» (точное имя без < >)";

            if (string.IsNullOrWhiteSpace(valueCol))
                return $"Ведомость «{schedule.Name}»: нет колонки [{string.Join(" | ", entry.ValueColumnsExact)}]";

            var cols = $"кол. «{valueCol}»";
            if (!string.IsNullOrWhiteSpace(roomCol))
                cols += $", комната «{roomCol}»";
            else if (entry.Mode == ParseMode.GroupedByRoomHeader)
                cols += ", комната — строка-заголовок группы";
            else if (entry.Mode == ParseMode.SingleValueToFixedRoom)
                cols += $", помещение «{entry.FixedRoomName}» (без колонки комнаты в ведомости)";
            else if (entry.ParamCode == "DOUBLE_DOOR")
                cols += ", двуств.: ширина полотна > 1000 мм (или «Дв.» в типе)";

            if (entry.RoomBaseNamesFilter != null && entry.RoomBaseNamesFilter.Count > 0)
                cols += $", только помещения: {string.Join(", ", entry.RoomBaseNamesFilter)} (базовое имя)";

            if (!extracted.HasData)
                return $"«{schedule.Name}»: {cols} — строк с данными нет";

            return $"«{schedule.Name}»: {cols}";
        }

        static bool ParamAppliesToRoom(Entry entry, string roomName)
        {
            if (entry.RoomBaseNamesFilter != null && entry.RoomBaseNamesFilter.Count > 0)
                return RoomNameMatcher.MatchesAnyBaseName(roomName, entry.RoomBaseNamesFilter);

            if (!string.IsNullOrWhiteSpace(entry.FixedRoomName))
                return RoomNameMatcher.MatchesBaseName(roomName, entry.FixedRoomName);

            return true;
        }

        static double? GetParamValue(Dictionary<string, ExtractResult> byKey, Entry entry, string room)
        {
            if (!ParamAppliesToRoom(entry, room))
                return null;

            if (!byKey.TryGetValue(entry.ParamCode, out var r))
                return null;

            if (r.ByRoomIntOrEmpty.TryGetValue(room, out var i))
                return i;
            if (r.ByRoomOrEmpty.TryGetValue(room, out var d))
                return d;

            if (!string.IsNullOrWhiteSpace(entry.FixedRoomName)
                && RoomNameMatcher.MatchesBaseName(room, entry.FixedRoomName)
                && r.ByRoomOrEmpty.TryGetValue(entry.FixedRoomName.Trim(), out d))
                return d;

            return null;
        }

        static int SortRoomKey(string name)
        {
            var m = NumberRegex.Match(name ?? string.Empty);
            return m.Success && int.TryParse(m.Value, out var n) ? n : int.MaxValue;
        }

        static ViewSchedule FindScheduleExact(
            Dictionary<string, ViewSchedule> schedulesByNormalizedName,
            IReadOnlyList<string> exactNames)
        {
            if (exactNames == null)
                return null;

            foreach (var name in exactNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (schedulesByNormalizedName.TryGetValue(NormalizeName(name), out var schedule))
                    return schedule;
            }

            return null;
        }

        /// <summary>Имя ведомости: trim, снять &lt; &gt;, точки и запятые сохраняются.</summary>
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

        struct ExtractResult
        {
            public string ScheduleName;
            public Dictionary<string, double> ByRoom;
            public Dictionary<string, int> ByRoomInt;

            public Dictionary<string, double> ByRoomOrEmpty => ByRoom ?? new Dictionary<string, double>();
            public Dictionary<string, int> ByRoomIntOrEmpty => ByRoomInt ?? new Dictionary<string, int>();
            public bool HasData => (ByRoom?.Count ?? 0) > 0 || (ByRoomInt?.Count ?? 0) > 0;
        }

        static ExtractResult ExtractGroupedByRoom(
            ViewSchedule schedule,
            Dictionary<string, int> headers,
            int rowCount,
            IReadOnlyList<string> roomHeaders,
            IReadOnlyList<string> valueHeaders,
            IReadOnlyList<string> roomBaseNamesFilter,
            out string roomColUsed,
            out string valueColUsed)
        {
            var result = new ExtractResult
            {
                ScheduleName = schedule.Name,
                ByRoom = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            };

            var colValue = ResolveColumnExact(headers, valueHeaders, out valueColUsed);
            var colRoom = ResolveColumnExact(headers, roomHeaders, out roomColUsed);
            if (colValue == null)
                return result;

            string currentRoom = null;
            for (var r = 1; r < rowCount; r++)
            {
                if (IsGrandTotalRow(schedule, r, headers))
                    continue;

                var roomCell = colRoom != null ? GetCell(schedule, r, colRoom.Value) : string.Empty;
                var value = ParseNullableDouble(GetCell(schedule, r, colValue.Value));

                if (!string.IsNullOrWhiteSpace(roomCell) && value == null)
                {
                    currentRoom = roomCell.Trim();
                    continue;
                }

                if (value == null)
                {
                    var headerRoom = DetectGroupRoomName(schedule, r, headers, colValue.Value);
                    if (!string.IsNullOrWhiteSpace(headerRoom))
                        currentRoom = headerRoom;
                    continue;
                }

                var room = !string.IsNullOrWhiteSpace(roomCell) ? roomCell.Trim() : currentRoom;
                if (string.IsNullOrWhiteSpace(room))
                    continue;

                currentRoom = room;
                AddToMap(result.ByRoom, room, value.Value, roomBaseNamesFilter);
            }

            return result;
        }

        static ExtractResult ExtractSingleValueToFixedRoom(
            ViewSchedule schedule,
            Dictionary<string, int> headers,
            int rowCount,
            IReadOnlyList<string> valueHeaders,
            string fixedRoomName,
            out string roomColUsed,
            out string valueColUsed)
        {
            roomColUsed = null;
            valueColUsed = null;

            var result = new ExtractResult
            {
                ScheduleName = schedule.Name,
                ByRoom = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            };

            if (string.IsNullOrWhiteSpace(fixedRoomName))
                return result;

            var colValue = ResolveColumnExact(headers, valueHeaders, out valueColUsed);
            if (colValue == null)
                return result;

            double sum = 0;
            var hasValue = false;
            for (var r = 1; r < rowCount; r++)
            {
                if (IsGrandTotalRow(schedule, r, headers))
                    continue;

                var value = ParseNullableDouble(GetCell(schedule, r, colValue.Value));
                if (value == null)
                    continue;

                sum += value.Value;
                hasValue = true;
            }

            if (hasValue)
                result.ByRoom[fixedRoomName.Trim()] = sum;

            roomColUsed = fixedRoomName;
            return result;
        }

        static ExtractResult ExtractFlatByRoom(
            ViewSchedule schedule,
            Dictionary<string, int> headers,
            int rowCount,
            IReadOnlyList<string> roomHeaders,
            IReadOnlyList<string> valueHeaders,
            IReadOnlyList<string> roomBaseNamesFilter,
            out string roomColUsed,
            out string valueColUsed)
        {
            var result = new ExtractResult
            {
                ScheduleName = schedule.Name,
                ByRoom = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            };

            var colRoom = ResolveColumnExact(headers, roomHeaders, out roomColUsed);
            var colValue = ResolveColumnExact(headers, valueHeaders, out valueColUsed);
            if (colRoom == null || colValue == null)
                return result;

            for (var r = 1; r < rowCount; r++)
            {
                if (IsGrandTotalRow(schedule, r, headers))
                    continue;

                var room = (GetCell(schedule, r, colRoom.Value) ?? string.Empty).Trim();
                var value = ParseNullableDouble(GetCell(schedule, r, colValue.Value));
                if (string.IsNullOrWhiteSpace(room) || value == null)
                    continue;

                AddToMap(result.ByRoom, room, value.Value, roomBaseNamesFilter);
            }

            return result;
        }

        static ExtractResult ExtractDoorsByRoom(
            ViewSchedule schedule,
            Dictionary<string, int> headers,
            int rowCount,
            IReadOnlyList<string> roomHeaders,
            IReadOnlyList<string> valueHeaders,
            IReadOnlyList<string> roomBaseNamesFilter,
            bool doubleLeafOnly,
            out string roomColUsed,
            out string valueColUsed)
        {
            var result = new ExtractResult
            {
                ScheduleName = schedule.Name,
                ByRoomInt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            };

            var colRoom = ResolveColumnExact(headers, roomHeaders, out roomColUsed);
            var colQty = ResolveColumnExact(headers, valueHeaders, out valueColUsed);
            var colType = ResolveColumnExact(headers, new[] { "Тип" }, out _);
            var colWidth = ResolveColumnExact(headers,
                new[] { "Ширина полотна, м", "Ширина полотна", "Ширина" }, out _);
            if (colRoom == null)
                return result;

            for (var r = 1; r < rowCount; r++)
            {
                if (IsGrandTotalRow(schedule, r, headers))
                    continue;

                var room = (GetCell(schedule, r, colRoom.Value) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(room))
                    continue;

                if (!RoomNameMatcher.IsAllowedRoom(room, roomBaseNamesFilter))
                    continue;

                var type = colType != null ? GetCell(schedule, r, colType.Value) : string.Empty;
                var widthText = colWidth != null ? GetCell(schedule, r, colWidth.Value) : string.Empty;
                if (doubleLeafOnly && !IsDoubleLeafDoor(type, widthText))
                    continue;

                var qty = 1;
                if (colQty != null)
                {
                    var parsed = ParseNullableDouble(GetCell(schedule, r, colQty.Value));
                    if (parsed.HasValue && parsed.Value > 0)
                        qty = (int)Math.Round(parsed.Value);
                }

                if (!result.ByRoomInt.ContainsKey(room))
                    result.ByRoomInt[room] = 0;
                result.ByRoomInt[room] += qty;
            }

            return result;
        }

        const double DoubleDoorWidthThresholdMm = 1000d;

        static bool IsDoubleLeafDoor(string type, string widthCellText)
        {
            var widthMm = ParseDoorWidthMillimeters(widthCellText);
            if (widthMm.HasValue)
                return widthMm.Value > DoubleDoorWidthThresholdMm;

            if (string.IsNullOrWhiteSpace(type))
                return false;

            var t = type.Trim();
            return t.IndexOf("дв.", StringComparison.OrdinalIgnoreCase) >= 0
                   || t.IndexOf("дв ", StringComparison.OrdinalIgnoreCase) >= 0
                   || t.IndexOf("двуств", StringComparison.OrdinalIgnoreCase) >= 0
                   || t.IndexOf("2-ств", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>В ведомости ширина часто в мм (800), заголовок может быть «Ширина полотна, м».</summary>
        static double? ParseDoorWidthMillimeters(string cellText)
        {
            var v = ParseNullableDouble(cellText);
            if (!v.HasValue)
                return null;

            if (v.Value > 50d)
                return v.Value;

            if (v.Value > 0d && v.Value <= 10d)
                return v.Value * 1000d;

            return v.Value;
        }

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

        static void AddToMap(
            Dictionary<string, double> map,
            string room,
            double value,
            IReadOnlyList<string> roomBaseNamesFilter = null)
        {
            if (!RoomNameMatcher.IsAllowedRoom(room, roomBaseNamesFilter))
                return;

            if (!map.ContainsKey(room))
                map[room] = 0;
            map[room] += value;
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

        /// <summary>Только точное совпадение заголовка (OrdinalIgnoreCase), без Contains.</summary>
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
