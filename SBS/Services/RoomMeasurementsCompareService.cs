using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public enum RoomMeasurementCompareStatus
    {
        Match,
        Mismatch,
        ScheduleOnly,
        CodeOnly,
        BothEmpty
    }

    public class RoomMeasurementCompareParamItem
    {
        public string param_code { get; set; }
        public string param_name { get; set; }
        public double? schedule_value { get; set; }
        public double? code_value { get; set; }
        public RoomMeasurementCompareStatus Status { get; set; }
    }

    public class RoomMeasurementsCompareRoomRow
    {
        public string RoomName { get; set; }
        public List<RoomMeasurementCompareParamItem> Parameters { get; set; } = new();
        public bool HasDifference { get; set; }
    }

    public class RoomMeasurementsCompareSnapshot
    {
        public List<RoomMeasurementsCompareRoomRow> Rooms { get; set; } = new();
        public int MatchCount { get; set; }
        public int MismatchCount { get; set; }
        public int ScheduleOnlyCount { get; set; }
        public int CodeOnlyCount { get; set; }
    }

    public static class RoomMeasurementsCompareService
    {
        static readonly HashSet<string> IntegerParamCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "DOOR_CNT",
            "DOUBLE_DOOR"
        };

        public static RoomMeasurementsCompareSnapshot Compare(Autodesk.Revit.DB.Document doc)
        {
            var scheduleSnapshot = RoomMeasurementsService.Collect(doc);
            var codeSnapshot = RoomMeasurementsFromCodeService.Collect(doc);
            return Compare(scheduleSnapshot, codeSnapshot);
        }

        public static RoomMeasurementsCompareSnapshot Compare(
            RoomMeasurementsSnapshot scheduleSnapshot,
            RoomMeasurementsSnapshot codeSnapshot)
        {
            var result = new RoomMeasurementsCompareSnapshot();
            var roomKeys = BuildUnifiedRoomKeys(
                scheduleSnapshot?.Rooms,
                codeSnapshot?.Rooms,
                out var scheduleKeyByRoom,
                out var codeKeyByRoom,
                out var displayByKey);

            foreach (var roomKey in roomKeys)
            {
                var displayName = displayByKey[roomKey];
                var parameters = new List<RoomMeasurementCompareParamItem>();

                foreach (var entry in RoomMeasurementsElementMapping.All)
                {
                    if (!ParamAppliesToRoom(entry.ParamCode, displayName))
                        continue;

                    var scheduleValue = FindValueForKey(
                        scheduleSnapshot?.Rooms,
                        scheduleKeyByRoom,
                        roomKey,
                        entry.ParamCode);

                    var codeValue = FindValueForKey(
                        codeSnapshot?.Rooms,
                        codeKeyByRoom,
                        roomKey,
                        entry.ParamCode);

                    var status = CompareValues(
                        scheduleValue,
                        codeValue,
                        IntegerParamCodes.Contains(entry.ParamCode));

                    if (status == RoomMeasurementCompareStatus.BothEmpty)
                        continue;

                    parameters.Add(new RoomMeasurementCompareParamItem
                    {
                        param_code = entry.ParamCode,
                        param_name = entry.ParamName,
                        schedule_value = scheduleValue,
                        code_value = codeValue,
                        Status = status
                    });

                    IncrementSummary(result, status);
                }

                if (parameters.Count == 0)
                    continue;

                result.Rooms.Add(new RoomMeasurementsCompareRoomRow
                {
                    RoomName = displayName,
                    Parameters = parameters,
                    HasDifference = parameters.Any(p =>
                        p.Status is RoomMeasurementCompareStatus.Mismatch
                            or RoomMeasurementCompareStatus.ScheduleOnly
                            or RoomMeasurementCompareStatus.CodeOnly)
                });
            }

            return result;
        }

        static void IncrementSummary(
            RoomMeasurementsCompareSnapshot result,
            RoomMeasurementCompareStatus status)
        {
            switch (status)
            {
                case RoomMeasurementCompareStatus.Match:
                    result.MatchCount++;
                    break;
                case RoomMeasurementCompareStatus.Mismatch:
                    result.MismatchCount++;
                    break;
                case RoomMeasurementCompareStatus.ScheduleOnly:
                    result.ScheduleOnlyCount++;
                    break;
                case RoomMeasurementCompareStatus.CodeOnly:
                    result.CodeOnlyCount++;
                    break;
            }
        }

        static double? FindValueForKey(
            IList<RoomMeasurementsRoomRow> rooms,
            Dictionary<string, string> keyByRoom,
            string roomKey,
            string paramCode)
        {
            if (rooms == null || keyByRoom == null)
                return null;

            foreach (var room in rooms)
            {
                if (room == null || string.IsNullOrWhiteSpace(room.RoomName))
                    continue;

                if (!keyByRoom.TryGetValue(room.RoomName, out var key)
                    || !string.Equals(key, roomKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                return room.Parameters?
                    .FirstOrDefault(p => string.Equals(p.param_code, paramCode, StringComparison.OrdinalIgnoreCase))
                    ?.param_value;
            }

            return null;
        }

        static RoomMeasurementCompareStatus CompareValues(
            double? scheduleValue,
            double? codeValue,
            bool isInteger)
        {
            var scheduleHas = scheduleValue.HasValue;
            var codeHas = codeValue.HasValue;

            if (!scheduleHas && !codeHas)
                return RoomMeasurementCompareStatus.BothEmpty;

            if (scheduleHas && !codeHas)
                return RoomMeasurementCompareStatus.ScheduleOnly;

            if (!scheduleHas && codeHas)
                return RoomMeasurementCompareStatus.CodeOnly;

            if (isInteger)
            {
                return Math.Round(scheduleValue!.Value) == Math.Round(codeValue!.Value)
                    ? RoomMeasurementCompareStatus.Match
                    : RoomMeasurementCompareStatus.Mismatch;
            }

            return Math.Abs(scheduleValue!.Value - codeValue!.Value) < 0.015d
                ? RoomMeasurementCompareStatus.Match
                : RoomMeasurementCompareStatus.Mismatch;
        }

        static bool ParamAppliesToRoom(string paramCode, string roomDisplayName)
        {
            switch (paramCode)
            {
                case "DOUBLE_DOOR":
                    return RoomNameMatcher.MatchesBaseName(roomDisplayName, "Гостиная");
                case "PLITKA_AREA":
                    return RoomNameMatcher.MatchesAnyBaseName(
                        roomDisplayName,
                        new[] { "Прихожая", "Кухня" });
                case "APRON_AREA":
                    return RoomNameMatcher.MatchesBaseName(roomDisplayName, "Кухня");
                default:
                    return true;
            }
        }

        static List<string> BuildUnifiedRoomKeys(
            IList<RoomMeasurementsRoomRow> scheduleRooms,
            IList<RoomMeasurementsRoomRow> codeRooms,
            out Dictionary<string, string> scheduleKeyByRoom,
            out Dictionary<string, string> codeKeyByRoom,
            out Dictionary<string, string> displayByKey)
        {
            scheduleKeyByRoom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            codeKeyByRoom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            displayByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var codeRoomList = (codeRooms ?? Array.Empty<RoomMeasurementsRoomRow>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RoomName))
                .ToList();

            foreach (var room in codeRoomList)
            {
                var key = DsAreaCompareService.GetRoomCompareKey(room.RoomName);
                codeKeyByRoom[room.RoomName] = key;
                displayByKey[key] = room.RoomName;
            }

            var scheduleRoomList = (scheduleRooms ?? Array.Empty<RoomMeasurementsRoomRow>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RoomName))
                .ToList();

            var codeByExactName = codeRoomList.ToDictionary(
                r => r.RoomName.Trim(),
                r => r.RoomName,
                StringComparer.OrdinalIgnoreCase);

            var codeByCompareKey = codeRoomList
                .GroupBy(r => DsAreaCompareService.GetRoomCompareKey(r.RoomName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().RoomName, StringComparer.OrdinalIgnoreCase);

            var codeByBaseName = codeRoomList
                .GroupBy(r => RoomNameMatcher.GetBaseName(r.RoomName), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var room in scheduleRoomList)
            {
                var scheduleName = room.RoomName.Trim();
                string unifiedKey;

                if (codeByExactName.TryGetValue(scheduleName, out var exactCodeName))
                {
                    unifiedKey = codeKeyByRoom[exactCodeName];
                }
                else
                {
                    var compareKey = DsAreaCompareService.GetRoomCompareKey(scheduleName);
                    if (codeByCompareKey.TryGetValue(compareKey, out var codeNameByKey))
                    {
                        unifiedKey = codeKeyByRoom[codeNameByKey];
                    }
                    else
                    {
                        var baseName = RoomNameMatcher.GetBaseName(scheduleName);
                        if (codeByBaseName.TryGetValue(baseName, out var candidates) && candidates.Count == 1)
                            unifiedKey = codeKeyByRoom[candidates[0].RoomName];
                        else
                            unifiedKey = compareKey;
                    }
                }

                scheduleKeyByRoom[scheduleName] = unifiedKey;

                if (!displayByKey.ContainsKey(unifiedKey))
                    displayByKey[unifiedKey] = scheduleName;
            }

            var displayNames = displayByKey;
            return displayNames.Keys
                .OrderBy(k => displayNames[k], StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
