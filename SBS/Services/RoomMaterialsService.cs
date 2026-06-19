using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public static class RoomMaterialsService
    {
        const string ProductCodeParamName = "ADSK_Код изделия";
        const string ClassificationCodeParamName = "Код по классификатору";
        const string ErboEomCodeParamName = "ERBO_ЭОМ_Наименование";
        const string ErboRoomParamName = "ERBO_Помещения";

        /// <summary>Площадные элементы — каждый сегмент отдельной строкой.</summary>
        static readonly HashSet<long> ContinuousCategoryIds = new()
        {
            (long)BuiltInCategory.OST_Floors,
            (long)BuiltInCategory.OST_Ceilings,
            (long)BuiltInCategory.OST_Walls,
            (long)BuiltInCategory.OST_Roofs
        };

        static readonly HashSet<long> ExcludedCategoryIds = new()
        {
            (long)BuiltInCategory.OST_Rooms,
            (long)BuiltInCategory.OST_Areas,
            (long)BuiltInCategory.OST_Mass,
            (long)BuiltInCategory.OST_Levels,
            (long)BuiltInCategory.OST_Grids,
            (long)BuiltInCategory.OST_Views,
            (long)BuiltInCategory.OST_Sheets,
            (long)BuiltInCategory.OST_Schedules,
            (long)BuiltInCategory.OST_Cameras,
            (long)BuiltInCategory.OST_Sections,
            (long)BuiltInCategory.OST_ElevationMarks,
            (long)BuiltInCategory.OST_ReferencePoints,
            (long)BuiltInCategory.OST_Materials,
            (long)BuiltInCategory.OST_RvtLinks
        };

        public static RoomMaterialsSnapshot Collect(Document doc)
        {
            var snapshot = new RoomMaterialsSnapshot();
            if (doc == null)
                return snapshot;

            var phase = RoomAreaService.GetPreferredPhase(doc);
            if (phase == null)
                return snapshot;

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r != null &&
                            r.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() == phase.Id &&
                            r.Area > 0)
                .OrderBy(r => RoomAreaService.GetRoomSortKey(r))
                .ThenBy(r => RoomAreaService.GetRoomDisplayName(r), StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rooms.Count == 0)
                return snapshot;

            var itemsByRoomId = rooms.ToDictionary(r => r.Id.Value, _ => new List<RoomMaterialItem>());
            var roomIds = itemsByRoomId.Keys.ToHashSet();
            var assignedElementIds = new HashSet<long>();
            var elementsWithCode = 0;
            var skippedNoCode = 0;
            var skippedCategory = 0;
            var onlyAdsk = 0;
            var onlyClassifier = 0;
            var onlyErboEom = 0;
            var bothCodes = 0;
            var conflictingCodes = 0;

            foreach (var element in new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements())
            {
                if (IsExcludedCategory(element.Category))
                {
                    skippedCategory++;
                    continue;
                }

                if (!HasProductCode(element, doc))
                {
                    skippedNoCode++;
                    continue;
                }

                elementsWithCode++;
                TrackCodeStats(element, doc, ref onlyAdsk, ref onlyClassifier, ref onlyErboEom, ref bothCodes, ref conflictingCodes);

                var targetRooms = ResolveRoomsForElement(element, doc, phase, rooms, roomIds);
                if (targetRooms.Count == 0)
                    continue;

                var item = BuildMaterialItem(element, doc);
                foreach (var room in targetRooms)
                {
                    if (!itemsByRoomId.TryGetValue(room.Id.Value, out var list))
                        continue;

                    list.Add(item);
                    assignedElementIds.Add(element.Id.Value);
                }
            }

            foreach (var room in rooms)
            {
                itemsByRoomId.TryGetValue(room.Id.Value, out var items);
                if (items == null || items.Count == 0)
                    continue;

                snapshot.Rooms.Add(new RoomMaterialsRoomRow
                {
                    RoomName = RoomAreaService.GetRoomDisplayName(room),
                    Items = ConsolidateItems(items)
                });
            }

            MergePaintSchedule(doc, snapshot);

            snapshot.TotalElements = elementsWithCode;
            snapshot.ElementsWithCode = elementsWithCode;
            snapshot.UnassignedElements = elementsWithCode - assignedElementIds.Count;
            snapshot.SkippedWithoutCode = skippedNoCode;
            snapshot.SkippedExcludedCategory = skippedCategory;
            snapshot.OnlyAdskCodeCount = onlyAdsk;
            snapshot.OnlyClassifierCodeCount = onlyClassifier;
            snapshot.OnlyErboEomCodeCount = onlyErboEom;
            snapshot.BothCodesCount = bothCodes;
            snapshot.ConflictingCodesCount = conflictingCodes;

            snapshot.Rooms = snapshot.Rooms
                .Where(r => r.Items.Count > 0 || r.PaintItems.Count > 0)
                .OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return snapshot;
        }

        static void MergePaintSchedule(Document doc, RoomMaterialsSnapshot snapshot)
        {
            var paint = RoomPaintScheduleService.Collect(doc);
            snapshot.PaintSource = paint.Source;

            foreach (var paintRoom in paint.Rooms)
            {
                var target = snapshot.Rooms.FirstOrDefault(r =>
                    RoomNameMatcher.MatchesBaseName(r.RoomName, paintRoom.RoomName)
                    || string.Equals(r.RoomName, paintRoom.RoomName, StringComparison.OrdinalIgnoreCase));

                if (target != null)
                {
                    target.PaintItems = paintRoom.PaintItems;
                    continue;
                }

                snapshot.Rooms.Add(new RoomMaterialsRoomRow
                {
                    RoomName = paintRoom.RoomName,
                    PaintItems = paintRoom.PaintItems
                });
            }
        }

        static List<Room> ResolveRoomsForElement(
            Element element,
            Document doc,
            Phase phase,
            IList<Room> rooms,
            HashSet<long> roomIds)
        {
            var result = new List<Room>();
            if (element == null)
                return result;

            switch (element.Category?.Id.Value)
            {
                case (int)BuiltInCategory.OST_Doors when element is FamilyInstance door:
                    TryAddRoom(result, GetDoorRoom(door, phase, from: true), roomIds);
                    TryAddRoom(result, GetDoorRoom(door, phase, from: false), roomIds);
                    break;

                case (int)BuiltInCategory.OST_Windows when element is FamilyInstance window:
                    TryAddRoom(result, FindRoomForWindow(window, doc, phase, rooms), roomIds);
                    break;

                case (int)BuiltInCategory.OST_Walls:
                    var wallRoom = FindRoomByErboParameter(element, doc, rooms)
                                   ?? FindRoomByPoint(element, doc, phase, rooms, sampleBelowCeiling: false);
                    TryAddRoom(result, wallRoom, roomIds);
                    break;

                case (int)BuiltInCategory.OST_Floors:
                    TryAddRoom(result, FindRoomByPoint(element, doc, phase, rooms, sampleBelowCeiling: false, offsetAbove: true), roomIds);
                    break;

                case (int)BuiltInCategory.OST_Ceilings:
                    TryAddRoom(result, FindRoomByPoint(element, doc, phase, rooms, sampleBelowCeiling: true), roomIds);
                    break;

                default:
                    if (element is FamilyInstance instance)
                        TryAddRoom(result, GetFamilyInstanceRoom(instance, phase), roomIds);

                    TryAddRoom(result, FindRoomByErboParameter(element, doc, rooms), roomIds);
                    if (result.Count == 0)
                        TryAddRoom(result, FindRoomByLocation(element, doc, phase, rooms), roomIds);
                    if (result.Count == 0)
                        TryAddRoom(result, FindRoomByPoint(element, doc, phase, rooms, sampleBelowCeiling: false), roomIds);
                    break;
            }

            return result;
        }

        static void TryAddRoom(List<Room> list, Room room, HashSet<long> roomIds)
        {
            if (room == null || !roomIds.Contains(room.Id.Value))
                return;

            if (list.Any(r => r.Id == room.Id))
                return;

            list.Add(room);
        }

        static RoomMaterialItem BuildMaterialItem(Element element, Document doc)
        {
            var adsk = ResolveParameterAt(element, doc, ProductCodeParamName);
            var classifier = ResolveParameterAt(element, doc, ClassificationCodeParamName);
            var erboEom = ResolveParameterAt(element, doc, ErboEomCodeParamName);

            return new RoomMaterialItem
            {
                Name = GetElementDisplayName(element, doc),
                Category = element.Category?.Name ?? "—",
                CategoryId = element.Category?.Id.Value,
                AdskProductCode = DisplayOrDash(adsk?.Value),
                ClassificationCode = DisplayOrDash(classifier?.Value),
                ErboEomCode = DisplayOrDash(erboEom?.Value),
                CodeSourceNote = BuildCodeSourceNote(adsk, classifier, erboEom),
                Quantity = 1
            };
        }

        static List<RoomMaterialItem> ConsolidateItems(IList<RoomMaterialItem> items)
        {
            if (items == null || items.Count == 0)
                return new List<RoomMaterialItem>();

            var groupable = new List<RoomMaterialItem>();
            var continuous = new List<RoomMaterialItem>();

            foreach (var item in items)
            {
                if (IsContinuousCategory(item.CategoryId))
                    continuous.Add(item);
                else
                    groupable.Add(item);
            }

            var grouped = groupable
                .GroupBy(i => string.Join("\u001F",
                    i.Name ?? string.Empty,
                    i.AdskProductCode ?? string.Empty,
                    i.ClassificationCode ?? string.Empty,
                    i.ErboEomCode ?? string.Empty))
                .Select(g =>
                {
                    var first = g.First();
                    return new RoomMaterialItem
                    {
                        Name = first.Name,
                        Category = first.Category,
                        CategoryId = first.CategoryId,
                        AdskProductCode = first.AdskProductCode,
                        ClassificationCode = first.ClassificationCode,
                        ErboEomCode = first.ErboEomCode,
                        CodeSourceNote = first.CodeSourceNote,
                        Quantity = g.Count()
                    };
                });

            return continuous
                .Concat(grouped)
                .OrderBy(i => i.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(i => i.Quantity)
                .ThenBy(i => i.AdskProductCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.ClassificationCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static bool IsContinuousCategory(long? categoryId) =>
            categoryId.HasValue && ContinuousCategoryIds.Contains(categoryId.Value);

        sealed class ParameterValueAt
        {
            public string Value { get; init; }
            public string Level { get; init; }
        }

        static ParameterValueAt ResolveParameterAt(Element element, Document doc, string parameterName)
        {
            var instanceValue = GetParameterString(element, parameterName);
            if (!string.IsNullOrWhiteSpace(instanceValue))
            {
                return new ParameterValueAt
                {
                    Value = instanceValue.Trim(),
                    Level = "экземпляр"
                };
            }

            var type = doc?.GetElement(element?.GetTypeId()) as ElementType;
            var typeValue = GetParameterString(type, parameterName);
            if (!string.IsNullOrWhiteSpace(typeValue))
            {
                return new ParameterValueAt
                {
                    Value = typeValue.Trim(),
                    Level = "тип"
                };
            }

            return null;
        }

        static string BuildCodeSourceNote(
            ParameterValueAt adsk,
            ParameterValueAt classifier,
            ParameterValueAt erboEom)
        {
            var parts = new List<string>();
            if (adsk != null)
                parts.Add($"ADSK · {adsk.Level}");
            if (classifier != null)
                parts.Add($"Классификатор · {classifier.Level}");
            if (erboEom != null)
                parts.Add($"ЭОМ · {erboEom.Level}");

            if (parts.Count == 0)
                return "—";

            var note = string.Join("; ", parts);
            var values = new[] { adsk?.Value, classifier?.Value, erboEom?.Value }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (values.Count > 1)
                note += " · разные значения";

            return note;
        }

        static void TrackCodeStats(
            Element element,
            Document doc,
            ref int onlyAdsk,
            ref int onlyClassifier,
            ref int onlyErboEom,
            ref int bothCodes,
            ref int conflictingCodes)
        {
            var adsk = ResolveParameterAt(element, doc, ProductCodeParamName);
            var classifier = ResolveParameterAt(element, doc, ClassificationCodeParamName);
            var erboEom = ResolveParameterAt(element, doc, ErboEomCodeParamName);
            var hasAdsk = adsk != null;
            var hasClassifier = classifier != null;
            var hasErboEom = erboEom != null;
            var sourceCount = (hasAdsk ? 1 : 0) + (hasClassifier ? 1 : 0) + (hasErboEom ? 1 : 0);

            if (sourceCount >= 2)
            {
                bothCodes++;
                var values = new[] { adsk?.Value, classifier?.Value, erboEom?.Value }
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (values.Count > 1)
                    conflictingCodes++;
            }
            else if (hasAdsk)
            {
                onlyAdsk++;
            }
            else if (hasClassifier)
            {
                onlyClassifier++;
            }
            else if (hasErboEom)
            {
                onlyErboEom++;
            }
        }

        static Room FindRoomByErboParameter(Element element, Document doc, IList<Room> rooms)
        {
            var erboRoom = GetParameterString(element, ErboRoomParamName);
            if (string.IsNullOrWhiteSpace(erboRoom) && doc != null)
            {
                var type = doc.GetElement(element.GetTypeId()) as ElementType;
                erboRoom = GetParameterString(type, ErboRoomParamName);
            }

            if (string.IsNullOrWhiteSpace(erboRoom))
                return null;

            return rooms.FirstOrDefault(r =>
                RoomNameMatcher.MatchesBaseName(RoomAreaService.GetRoomDisplayName(r), erboRoom)
                || string.Equals(RoomAreaService.GetRoomDisplayName(r), erboRoom, StringComparison.OrdinalIgnoreCase));
        }

        static Room FindRoomByLocation(Element element, Document doc, Phase phase, IList<Room> rooms)
        {
            XYZ point = null;
            switch (element.Location)
            {
                case LocationPoint lp:
                    point = lp.Point;
                    break;
                case LocationCurve lc:
                    point = lc.Curve?.Evaluate(0.5, true);
                    break;
            }

            if (point == null)
                return null;

            try
            {
                var roomAtPoint = doc.GetRoomAtPoint(point, phase);
                if (roomAtPoint != null && rooms.Any(r => r.Id == roomAtPoint.Id))
                    return roomAtPoint;
            }
            catch
            {
                // ignore
            }

            return rooms.FirstOrDefault(r => IsPointInRoomSafe(r, point));
        }

        static Room FindRoomByPoint(
            Element element,
            Document doc,
            Phase phase,
            IList<Room> rooms,
            bool sampleBelowCeiling,
            bool offsetAbove = false)
        {
            if (element == null || doc == null)
                return null;

            try
            {
                var bb = element.get_BoundingBox(null);
                if (bb == null)
                    return null;

                var zOffset = UnitUtils.ConvertToInternalUnits(0.1, UnitTypeId.Meters);
                var z = offsetAbove
                    ? bb.Min.Z + zOffset
                    : sampleBelowCeiling
                        ? bb.Min.Z - zOffset
                        : (bb.Min.Z + bb.Max.Z) / 2d;

                var point = new XYZ(
                    (bb.Min.X + bb.Max.X) / 2d,
                    (bb.Min.Y + bb.Max.Y) / 2d,
                    z);

                var roomAtPoint = doc.GetRoomAtPoint(point, phase);
                if (roomAtPoint != null && rooms.Any(r => r.Id == roomAtPoint.Id))
                    return roomAtPoint;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        static Room FindRoomForWindow(
            FamilyInstance window,
            Document doc,
            Phase phase,
            IList<Room> rooms)
        {
            if (window == null)
                return null;

            var byParam = FindRoomByErboParameter(window, doc, rooms);
            if (byParam != null)
                return byParam;

            var point = (window.Location as LocationPoint)?.Point;
            if (point == null)
                return null;

            var offsetInternal = UnitUtils.ConvertToInternalUnits(0.4, UnitTypeId.Meters);
            var directions = new List<XYZ>();

            var facing = window.FacingOrientation;
            if (facing != null && facing.GetLength() > 1e-6)
            {
                directions.Add(facing.Normalize());
                directions.Add(facing.Normalize().Negate());
            }

            foreach (var dir in directions)
            {
                var testPoint = point + dir.Multiply(offsetInternal);
                var room = rooms.FirstOrDefault(r => IsPointInRoomSafe(r, testPoint));
                if (room != null)
                    return room;
            }

            try
            {
                var roomAtPoint = doc.GetRoomAtPoint(point, phase);
                if (roomAtPoint != null && rooms.Any(r => r.Id == roomAtPoint.Id))
                    return roomAtPoint;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        static Room GetFamilyInstanceRoom(FamilyInstance instance, Phase phase)
        {
            if (instance == null || phase == null)
                return null;

            try
            {
                return instance.get_Room(phase);
            }
            catch
            {
                return null;
            }
        }

        static bool IsExcludedCategory(Category category)
        {
            if (category == null)
                return true;

            if (category.CategoryType == CategoryType.AnalyticalModel)
                return true;

            return ExcludedCategoryIds.Contains(category.Id.Value);
        }

        static bool HasProductCode(Element element, Document doc) =>
            ResolveParameterAt(element, doc, ProductCodeParamName) != null
            || ResolveParameterAt(element, doc, ClassificationCodeParamName) != null
            || ResolveParameterAt(element, doc, ErboEomCodeParamName) != null;

        static Room GetDoorRoom(FamilyInstance door, Phase phase, bool from)
        {
            if (door == null || phase == null)
                return null;

            try
            {
                return from ? door.get_FromRoom(phase) : door.get_ToRoom(phase);
            }
            catch
            {
                return null;
            }
        }

        static bool IsPointInRoomSafe(Room room, XYZ point)
        {
            if (room == null || point == null)
                return false;

            try
            {
                return room.IsPointInRoom(point);
            }
            catch
            {
                return false;
            }
        }

        static string GetElementDisplayName(Element element, Document doc)
        {
            if (element == null)
                return "—";

            var type = doc?.GetElement(element.GetTypeId()) as ElementType;
            if (type != null)
            {
                var familyName = type.FamilyName?.Trim();
                var typeName = type.Name?.Trim();

                if (!string.IsNullOrWhiteSpace(familyName) && !string.IsNullOrWhiteSpace(typeName))
                    return $"{familyName}: {typeName}";

                if (!string.IsNullOrWhiteSpace(typeName))
                    return typeName;

                if (!string.IsNullOrWhiteSpace(familyName))
                    return familyName;
            }

            var name = element.Name?.Trim();
            return string.IsNullOrWhiteSpace(name) ? "—" : name;
        }

        static string GetParameterString(Element element, string parameterName)
        {
            var param = element?.LookupParameter(parameterName);
            if (param == null || !param.HasValue)
                return string.Empty;

            return param.StorageType == StorageType.String
                ? param.AsString()?.Trim() ?? string.Empty
                : param.AsValueString()?.Trim() ?? param.AsString()?.Trim() ?? string.Empty;
        }

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    }
}
