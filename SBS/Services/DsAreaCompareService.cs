using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public enum DsAreaCompareStatus
    {
        Match,
        Mismatch,
        SystemOnly,
        RevitOnly,
        BothEmpty
    }

    public static class DsAreaCompareService
    {
        const double ToleranceM2 = 0.015d;

        public static DsAreaCompareStatus CompareValues(double? systemValue, double? revitValue)
        {
            var systemHas = systemValue.HasValue;
            var revitHas = revitValue.HasValue;

            if (!systemHas && !revitHas)
                return DsAreaCompareStatus.BothEmpty;

            if (systemHas && !revitHas)
                return DsAreaCompareStatus.SystemOnly;

            if (!systemHas && revitHas)
                return DsAreaCompareStatus.RevitOnly;

            return Math.Abs(systemValue!.Value - revitValue!.Value) < ToleranceM2
                ? DsAreaCompareStatus.Match
                : DsAreaCompareStatus.Mismatch;
        }

        public static DsAreaCompareStatus CompareWallHeights(double? systemHeight, double? revitHeight) =>
            CompareValues(systemHeight, revitHeight);

        public static Dictionary<string, double?> BuildSystemAreaByKey(IEnumerable<DsRoomChangeRoomDto> rooms)
        {
            var map = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
            if (rooms == null)
                return map;

            foreach (var room in rooms)
            {
                if (room == null || string.IsNullOrWhiteSpace(room.RoomName))
                    continue;

                var key = GetRoomCompareKey(room.RoomName.Trim());
                if (!room.RoomArea.HasValue || room.RoomArea.Value <= 0d)
                    continue;

                map[key] = Math.Round(room.RoomArea.Value, 2);
            }

            return map;
        }

        public static string GetRoomCompareKey(string roomName) =>
            RoomNameMatcher.GetBaseName(roomName);
    }
}
