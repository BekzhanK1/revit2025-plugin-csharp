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
        public double WallHeightM { get; set; }
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
                    var wallHeightM = GetCeilingHeightM(r, doc);

                    return new RoomAreaItem
                    {
                        RoomNumber = num,
                        RoomName = roomName,
                        AreaM2 = Math.Round(areaM2, 2),
                        WallHeightM = wallHeightM
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

        /// <summary>
        /// Высота потолка: сначала по ограждающим стенам помещения (типично для одноэтажной квартиры),
        /// иначе — по уровням/границам самого Room.
        /// </summary>
        static double GetCeilingHeightM(Room room, Document doc)
        {
            if (room == null || doc == null)
                return 0d;

            var fromWalls = TryGetCeilingHeightFromBoundaryWallsM(room, doc);
            if (fromWalls > 0d)
                return Math.Round(fromWalls, 2);

            return Math.Round(GetCeilingHeightFromRoomLimitsM(room, doc), 2);
        }

        static double TryGetCeilingHeightFromBoundaryWallsM(Room room, Document doc)
        {
            IList<IList<BoundarySegment>> loops;
            try
            {
                var options = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };
                loops = room.GetBoundarySegments(options);
            }
            catch
            {
                return 0d;
            }

            if (loops == null || loops.Count == 0)
                return 0d;

            var heights = new List<double>();
            var seenWallIds = new HashSet<long>();

            foreach (var loop in loops)
            {
                if (loop == null)
                    continue;

                foreach (var segment in loop)
                {
                    if (segment?.ElementId == null || segment.ElementId == ElementId.InvalidElementId)
                        continue;

                    var wall = doc.GetElement(segment.ElementId) as Wall;
                    if (wall == null || !seenWallIds.Add(wall.Id.Value))
                        continue;

                    var heightM = GetWallInstanceHeightM(wall, doc);
                    if (heightM > 0.01d && heightM < 50d)
                        heights.Add(heightM);
                }
            }

            return PickRepresentativeHeightM(heights);
        }

        static double GetWallInstanceHeightM(Wall wall, Document doc)
        {
            var baseLevelId = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId()
                ?? wall.LevelId;
            var baseLevel = doc.GetElement(baseLevelId) as Level;
            if (baseLevel == null)
                return 0d;

            var baseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0d;
            var bottom = baseLevel.Elevation + baseOffset;

            var topLevelId = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId()
                ?? ElementId.InvalidElementId;
            if (topLevelId != ElementId.InvalidElementId)
            {
                var topLevel = doc.GetElement(topLevelId) as Level;
                if (topLevel != null)
                {
                    var topOffset = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.AsDouble() ?? 0d;
                    var heightInternal = topLevel.Elevation + topOffset - bottom;
                    if (heightInternal > 0d)
                        return UnitUtils.ConvertFromInternalUnits(heightInternal, UnitTypeId.Meters);
                }
            }

            var userHeight = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0d;
            if (userHeight > 0d)
                return UnitUtils.ConvertFromInternalUnits(userHeight, UnitTypeId.Meters);

            return 0d;
        }

        static double GetCeilingHeightFromRoomLimitsM(Room room, Document doc)
        {
            var fromLimits = TryGetHeightFromLevelLimits(room, doc);
            if (fromLimits > 0d)
                return fromLimits;

            try
            {
                var unboundedInternal = room.UnboundedHeight;
                if (unboundedInternal > 0d)
                {
                    var unboundedM = UnitUtils.ConvertFromInternalUnits(unboundedInternal, UnitTypeId.Meters);
                    if (unboundedM > 0.01d && unboundedM < 50d)
                        return unboundedM;
                }
            }
            catch
            {
                // ignore
            }

            var heightInt = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT)?.AsDouble() ?? 0d;
            if (heightInt <= 0d)
                return 0d;

            var heightM = UnitUtils.ConvertFromInternalUnits(heightInt, UnitTypeId.Meters);
            if (heightM <= 0.01d || heightM >= 50d)
                return 0d;

            return heightM;
        }

        static double PickRepresentativeHeightM(List<double> heightsM)
        {
            if (heightsM == null || heightsM.Count == 0)
                return 0d;

            return heightsM
                .GroupBy(h => Math.Round(h, 2))
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .Select(g => g.Key)
                .First();
        }

        static double TryGetHeightFromLevelLimits(Room room, Document doc)
        {
            var baseLevel = doc.GetElement(room.LevelId) as Level;
            if (baseLevel == null)
                return 0d;

            var upperLevelId = room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL)?.AsElementId()
                ?? ElementId.InvalidElementId;
            if (upperLevelId == ElementId.InvalidElementId)
                return 0d;

            var upperLevel = doc.GetElement(upperLevelId) as Level;
            if (upperLevel == null)
                return 0d;

            var baseOffset = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET)?.AsDouble() ?? 0d;
            var upperOffset = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET)?.AsDouble() ?? 0d;

            var bottom = baseLevel.Elevation + baseOffset;
            var top = upperLevel.Elevation + upperOffset;
            var heightInternal = top - bottom;
            if (heightInternal <= 0d)
                return 0d;

            return UnitUtils.ConvertFromInternalUnits(heightInternal, UnitTypeId.Meters);
        }

        static List<Phase> GetPhases(Document doc) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .OrderBy(p => p.Name)
                .ToList();
    }
}
