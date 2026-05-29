using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public class RoomAreaItem
    {
        public string RoomNumber { get; set; }
        public string RoomName { get; set; }
        public double AreaM2 { get; set; }
    }

    public static class RoomAreaService
    {
        public static string GetPreferredPhaseName(Document doc)
        {
            var phases = GetPhases(doc);
            var preferred = phases.FirstOrDefault(p =>
                p.Name.Equals("После монтажных работ", StringComparison.OrdinalIgnoreCase));
            return (preferred ?? phases.FirstOrDefault())?.Name ?? "—";
        }

        public static IReadOnlyList<RoomAreaItem> CollectRooms(Document doc)
        {
            if (doc == null)
                return Array.Empty<RoomAreaItem>();

            var phases = GetPhases(doc);
            var phase = phases.FirstOrDefault(p =>
                p.Name.Equals("После монтажных работ", StringComparison.OrdinalIgnoreCase))
                ?? phases.FirstOrDefault();

            if (phase == null)
                return Array.Empty<RoomAreaItem>();

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r != null &&
                            r.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() == phase.Id &&
                            r.Area > 0)
                .Select(r =>
                {
                    var name = (r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "").Trim();
                    var num = (r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "").Trim();
                    var roomName = !string.IsNullOrWhiteSpace(name) ? name : num;

                    var areaM2 = UnitUtils.ConvertFromInternalUnits(
                        r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0,
                        UnitTypeId.SquareMeters);

                    return new RoomAreaItem
                    {
                        RoomNumber = num,
                        RoomName = roomName,
                        AreaM2 = Math.Round(areaM2, 2)
                    };
                })
                .OrderBy(r => ParseRoomNumberSortKey(r.RoomNumber))
                .ThenBy(r => r.RoomName)
                .ToList();
        }

        static int ParseRoomNumberSortKey(string roomNumber)
        {
            if (string.IsNullOrWhiteSpace(roomNumber))
                return int.MaxValue;
            return int.TryParse(roomNumber, out var n) ? n : int.MaxValue - 1;
        }

        static List<Phase> GetPhases(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .OrderBy(p => p.Name)
                .ToList();
    }
}
