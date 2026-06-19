using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    public static class RoomMeasurementsFromCodeService
    {
        const string ErboAreaParamName = "ERBO_Площадь";
        const double DoubleDoorWidthThresholdMm = 1000d;
        const string DoubleDoorRoomBaseName = "Гостиная";
        const string FloorModelParamName = "Модель";
        const string FloorTileModelValue = "Напольная плитка";
        const string ErboRoomParamName = "ERBO_Помещения";
        const string ApronRoomBaseName = "Кухня";
        const string AreaParamName = "Площадь";
        static readonly string[] PlitkaRoomBaseNames = { "Прихожая", "Кухня" };
        static readonly Regex NumberRegex = new(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        public static RoomMeasurementsSnapshot Collect(Document doc)
        {
            var snapshot = new RoomMeasurementsSnapshot();

            if (doc == null)
            {
                foreach (var entry in RoomMeasurementsElementMapping.All)
                    snapshot.Sources.Add(BuildSource(entry, null, false, "Документ не задан"));
                return snapshot;
            }

            var phase = RoomAreaService.GetPreferredPhase(doc);
            if (phase == null)
            {
                foreach (var entry in RoomMeasurementsElementMapping.All)
                    snapshot.Sources.Add(BuildSource(entry, null, false, "В проекте нет фаз"));
                return snapshot;
            }

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

            var openingsByRoomId = CollectOpeningAreasByRoom(doc, phase, rooms);
            var doorCountsByRoomId = CollectDoorCountsByRoom(doc, phase, rooms);
            var doubleDoorCountsByRoomId = CollectDoubleDoorCountsByRoom(doc, phase, rooms);
            var plitkaByRoomId = CollectPlitkaAreaByRoom(doc, phase, rooms);
            var apronSummary = CollectApronArea(doc);
            var floorDetails = new StringBuilder();
            var roofDetails = new StringBuilder();
            var doorDetails = new StringBuilder();
            var doubleDoorDetails = new StringBuilder();
            var plitkaDetails = new StringBuilder();
            var apronDetails = new StringBuilder();
            var wallDetails = new StringBuilder();
            var floorRoomsWithData = 0;
            var roofRoomsWithData = 0;
            var doorRoomsWithData = 0;
            var doubleDoorRoomsWithData = 0;
            var plitkaRoomsWithData = 0;
            var apronRoomsWithData = 0;
            var wallRoomsWithData = 0;

            var floorEntry = RoomMeasurementsElementMapping.PerimeterFloor;
            var roofEntry = RoomMeasurementsElementMapping.PerimeterRoof;
            var doorEntry = RoomMeasurementsElementMapping.DoorCnt;
            var doubleDoorEntry = RoomMeasurementsElementMapping.DoubleDoor;
            var plitkaEntry = RoomMeasurementsElementMapping.PlitkaArea;
            var apronEntry = RoomMeasurementsElementMapping.ApronArea;
            var wallEntry = RoomMeasurementsElementMapping.WallAreaMinus;

            foreach (var room in rooms)
            {
                var roomName = RoomAreaService.GetRoomDisplayName(room);
                var parameters = new List<RoomMeasurementParamItem>();

                var floorPerimeterM = CalcPerimeterFloorM(room, doc, phase, out var floorDetail);
                if (floorPerimeterM.HasValue)
                    floorRoomsWithData++;
                floorDetails.AppendLine(floorDetail);

                parameters.Add(new RoomMeasurementParamItem
                {
                    param_code = floorEntry.ParamCode,
                    param_name = floorEntry.ParamName,
                    param_value = floorPerimeterM
                });

                var roofPerimeterM = CalcPerimeterRoofM(room, out var roofDetail);
                if (roofPerimeterM.HasValue)
                    roofRoomsWithData++;
                roofDetails.AppendLine(roofDetail);

                parameters.Add(new RoomMeasurementParamItem
                {
                    param_code = roofEntry.ParamCode,
                    param_name = roofEntry.ParamName,
                    param_value = roofPerimeterM
                });

                doorCountsByRoomId.TryGetValue(room.Id.Value, out var doorCount);
                double? doorCnt = doorCount?.Total > 0 ? doorCount.Total : null;
                if (doorCnt.HasValue)
                    doorRoomsWithData++;
                doorDetails.AppendLine(FormatDoorCountDetail(roomName, doorCount));

                parameters.Add(new RoomMeasurementParamItem
                {
                    param_code = doorEntry.ParamCode,
                    param_name = doorEntry.ParamName,
                    param_value = doorCnt
                });

                if (RoomNameMatcher.MatchesBaseName(roomName, DoubleDoorRoomBaseName))
                {
                    doubleDoorCountsByRoomId.TryGetValue(room.Id.Value, out var doubleDoorCount);
                    var doubleDoorCnt = doubleDoorCount?.Total > 0 ? (double?)doubleDoorCount.Total : null;
                    if (doubleDoorCnt.HasValue)
                        doubleDoorRoomsWithData++;

                    var doubleDoorDetail = FormatDoubleDoorCountDetail(roomName, doubleDoorCount);
                    if (!string.IsNullOrWhiteSpace(doubleDoorDetail))
                        doubleDoorDetails.AppendLine(doubleDoorDetail);

                    parameters.Add(new RoomMeasurementParamItem
                    {
                        param_code = doubleDoorEntry.ParamCode,
                        param_name = doubleDoorEntry.ParamName,
                        param_value = doubleDoorCnt
                    });
                }

                if (RoomNameMatcher.MatchesAnyBaseName(roomName, PlitkaRoomBaseNames))
                {
                    plitkaByRoomId.TryGetValue(room.Id.Value, out var plitka);
                    var plitkaM2 = plitka?.TotalAreaM2 > 0d ? (double?)plitka.TotalAreaM2 : null;
                    if (plitkaM2.HasValue)
                        plitkaRoomsWithData++;

                    var plitkaDetail = FormatPlitkaAreaDetail(roomName, plitka);
                    if (!string.IsNullOrWhiteSpace(plitkaDetail))
                        plitkaDetails.AppendLine(plitkaDetail);

                    parameters.Add(new RoomMeasurementParamItem
                    {
                        param_code = plitkaEntry.ParamCode,
                        param_name = plitkaEntry.ParamName,
                        param_value = plitkaM2
                    });
                }

                if (RoomNameMatcher.MatchesBaseName(roomName, ApronRoomBaseName))
                {
                    var apronM2 = apronSummary.TotalAreaM2 > 0d ? (double?)apronSummary.TotalAreaM2 : null;
                    if (apronM2.HasValue)
                        apronRoomsWithData++;

                    var apronDetail = FormatApronAreaDetail(roomName, apronSummary);
                    if (!string.IsNullOrWhiteSpace(apronDetail))
                        apronDetails.AppendLine(apronDetail);

                    parameters.Add(new RoomMeasurementParamItem
                    {
                        param_code = apronEntry.ParamCode,
                        param_name = apronEntry.ParamName,
                        param_value = apronM2
                    });
                }

                var perimeterM = GetPerimeterM(room);
                var heightM = RoomAreaService.GetWallHeightM(room, doc);
                double? wallAreaM2 = null;

                if (perimeterM > 0d && heightM > 0d)
                {
                    var grossM2 = Math.Round(perimeterM * heightM, 2);
                    openingsByRoomId.TryGetValue(room.Id.Value, out var openings);
                    var openingsM2 = openings?.TotalAreaM2 ?? 0d;
                    wallAreaM2 = Math.Round(Math.Max(0d, grossM2 - openingsM2), 2);
                    wallRoomsWithData++;

                    if (openingsM2 > 0d && openings?.Items?.Count > 0)
                    {
                        var openingParts = string.Join(", ",
                            openings.Items.Select(o => $"{o.Label} {o.AreaM2:0.##} м²"));
                        wallDetails.AppendLine(
                            $"{roomName}: {perimeterM:0.##} м × {heightM:0.##} м = {grossM2:0.##} м² "
                            + $"− проёмы {openingsM2:0.##} м² ({openingParts}) = {wallAreaM2.Value:0.##} м²");
                    }
                    else
                    {
                        wallDetails.AppendLine(
                            $"{roomName}: {perimeterM:0.##} м × {heightM:0.##} м = {wallAreaM2.Value:0.##} м²");
                    }
                }
                else
                {
                    wallDetails.AppendLine(
                        $"{roomName}: нет данных (периметр {perimeterM:0.##} м, высота {heightM:0.##} м)");
                }

                parameters.Add(new RoomMeasurementParamItem
                {
                    param_code = wallEntry.ParamCode,
                    param_name = wallEntry.ParamName,
                    param_value = wallAreaM2
                });

                snapshot.Rooms.Add(new RoomMeasurementsRoomRow
                {
                    RoomName = roomName,
                    Parameters = parameters
                });
            }

            if (rooms.Count == 0)
            {
                floorDetails.AppendLine("Помещения с площадью > 0 в выбранной фазе не найдены.");
                roofDetails.AppendLine("Помещения с площадью > 0 в выбранной фазе не найдены.");
                doorDetails.AppendLine("Помещения с площадью > 0 в выбранной фазе не найдены.");
                doubleDoorDetails.AppendLine("Помещения с площадью > 0 в выбранной фазе не найдены.");
                plitkaDetails.AppendLine("Помещения с площадью > 0 в выбранной фазе не найдены.");
                apronDetails.AppendLine("Помещения с площадью > 0 в выбранной фазе не найдены.");
                wallDetails.AppendLine("Помещения с площадью > 0 в выбранной фазе не найдены.");
            }

            snapshot.Sources.Add(BuildSource(
                floorEntry,
                phase.Name,
                floorRoomsWithData > 0,
                floorDetails.ToString().TrimEnd()));

            snapshot.Sources.Add(BuildSource(
                roofEntry,
                phase.Name,
                roofRoomsWithData > 0,
                roofDetails.ToString().TrimEnd()));

            snapshot.Sources.Add(BuildSource(
                doorEntry,
                phase.Name,
                doorRoomsWithData > 0,
                doorDetails.ToString().TrimEnd()));

            snapshot.Sources.Add(BuildSource(
                doubleDoorEntry,
                phase.Name,
                doubleDoorRoomsWithData > 0,
                doubleDoorDetails.ToString().TrimEnd()));

            snapshot.Sources.Add(BuildSource(
                plitkaEntry,
                phase.Name,
                plitkaRoomsWithData > 0,
                plitkaDetails.ToString().TrimEnd()));

            snapshot.Sources.Add(BuildSource(
                apronEntry,
                phase.Name,
                apronRoomsWithData > 0,
                apronDetails.ToString().TrimEnd()));

            snapshot.Sources.Add(BuildSource(
                wallEntry,
                phase.Name,
                wallRoomsWithData > 0,
                wallDetails.ToString().TrimEnd()));

            return snapshot;
        }

        sealed class RoomDoorCountSummary
        {
            public int Total { get; set; }
            public List<string> Labels { get; } = new();
        }

        static Dictionary<long, RoomDoorCountSummary> CollectDoorCountsByRoom(
            Document doc,
            Phase phase,
            IList<Room> rooms)
        {
            var map = rooms.ToDictionary(r => r.Id.Value, _ => new RoomDoorCountSummary());
            if (rooms.Count == 0)
                return map;

            var roomIds = new HashSet<long>(rooms.Select(r => r.Id.Value));

            foreach (var door in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                var fromRoom = GetDoorRoom(door, phase, from: true);
                var toRoom = GetDoorRoom(door, phase, from: false);

                // Межкомнатные: обе стороны размещены в помещениях квартиры (входная с улицы — только ToRoom).
                if (fromRoom == null || toRoom == null)
                    continue;
                if (!roomIds.Contains(fromRoom.Id.Value) || !roomIds.Contains(toRoom.Id.Value))
                    continue;
                if (!IsCountableInteriorDoor(door))
                    continue;

                var label = BuildOpeningLabel("дверь", door);
                AddDoorCount(map, fromRoom, label);
                AddDoorCount(map, toRoom, label);
            }

            return map;
        }

        /// <summary>
        /// Проёмы без полотна (ADSK_Дверь_Проем и т.п.) — в категории «Двери», но не DOOR_CNT.
        /// </summary>
        static bool IsCountableInteriorDoor(FamilyInstance door)
        {
            if (door == null)
                return false;

            var symbol = door.Symbol;
            if (IsOpeningTypeName(symbol?.Name) || IsOpeningTypeName(symbol?.FamilyName))
                return false;

            return !IsOpeningTypeName(door.Name);
        }

        static bool IsOpeningTypeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.IndexOf("проем", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("проём", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void AddDoorCount(
            Dictionary<long, RoomDoorCountSummary> map,
            Room room,
            string label)
        {
            if (room == null || !map.TryGetValue(room.Id.Value, out var summary))
                return;

            summary.Total++;
            summary.Labels.Add(label);
        }

        static string FormatDoorCountDetail(string roomName, RoomDoorCountSummary summary)
        {
            if (summary == null || summary.Total <= 0)
                return $"{roomName}: межкомнатных дверей нет";

            var parts = string.Join(", ", summary.Labels);
            return $"{roomName}: {summary.Total} шт ({parts})";
        }

        static Dictionary<long, RoomDoorCountSummary> CollectDoubleDoorCountsByRoom(
            Document doc,
            Phase phase,
            IList<Room> rooms)
        {
            var map = rooms.ToDictionary(r => r.Id.Value, _ => new RoomDoorCountSummary());
            if (rooms.Count == 0)
                return map;

            var roomIds = new HashSet<long>(rooms.Select(r => r.Id.Value));

            foreach (var door in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                if (!IsCountableInteriorDoor(door) || !IsDoubleLeafDoor(door))
                    continue;

                var fromRoom = GetDoorRoom(door, phase, from: true);
                var toRoom = GetDoorRoom(door, phase, from: false);
                if (fromRoom == null || toRoom == null)
                    continue;
                if (!roomIds.Contains(fromRoom.Id.Value) || !roomIds.Contains(toRoom.Id.Value))
                    continue;

                var label = BuildOpeningLabel("дверь", door);
                AddDoorCount(map, fromRoom, label);
                AddDoorCount(map, toRoom, label);
            }

            return map;
        }

        static string FormatDoubleDoorCountDetail(string roomName, RoomDoorCountSummary summary)
        {
            if (!RoomNameMatcher.MatchesBaseName(roomName, DoubleDoorRoomBaseName))
                return null;

            if (summary == null || summary.Total <= 0)
                return $"{roomName}: двустворчатых дверей нет";

            var parts = string.Join(", ", summary.Labels);
            return $"{roomName}: {summary.Total} шт ({parts})";
        }

        sealed class RoomPlitkaSummary
        {
            public double TotalAreaM2 { get; set; }
            public List<string> Items { get; } = new();
        }

        static Dictionary<long, RoomPlitkaSummary> CollectPlitkaAreaByRoom(
            Document doc,
            Phase phase,
            IList<Room> rooms)
        {
            var map = rooms.ToDictionary(r => r.Id.Value, _ => new RoomPlitkaSummary());
            if (doc == null || phase == null || rooms.Count == 0)
                return map;

            foreach (var floor in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .Cast<Element>())
            {
                if (!IsFloorTileElement(floor))
                    continue;

                var room = FindRoomForFloorElement(floor, doc, phase, rooms);
                if (room == null)
                    continue;
                if (!RoomNameMatcher.MatchesAnyBaseName(RoomAreaService.GetRoomDisplayName(room), PlitkaRoomBaseNames))
                    continue;

                var areaM2 = GetHostAreaM2(floor);
                if (areaM2 <= 0d)
                    continue;

                if (!map.TryGetValue(room.Id.Value, out var summary))
                    continue;

                var typeName = (doc.GetElement(floor.GetTypeId()) as ElementType)?.Name ?? "пол";
                summary.TotalAreaM2 = Math.Round(summary.TotalAreaM2 + areaM2, 2);
                summary.Items.Add($"{typeName} {areaM2:0.##} м²");
            }

            return map;
        }

        static bool IsFloorTileElement(Element element)
        {
            if (element == null)
                return false;

            var model = element.LookupParameter(FloorModelParamName)?.AsString()?.Trim();
            if (string.IsNullOrWhiteSpace(model) && element.Document != null)
            {
                var type = element.Document.GetElement(element.GetTypeId()) as ElementType;
                model = type?.LookupParameter(FloorModelParamName)?.AsString()?.Trim();
            }

            return string.Equals(model, FloorTileModelValue, StringComparison.OrdinalIgnoreCase);
        }

        static Room FindRoomForFloorElement(
            Element floor,
            Document doc,
            Phase phase,
            IList<Room> rooms)
        {
            if (floor == null || doc == null)
                return null;

            try
            {
                var bb = floor.get_BoundingBox(null);
                if (bb == null)
                    return null;

                var pt = new XYZ(
                    (bb.Min.X + bb.Max.X) / 2d,
                    (bb.Min.Y + bb.Max.Y) / 2d,
                    bb.Min.Z + UnitUtils.ConvertToInternalUnits(0.1, UnitTypeId.Meters));

                var roomAtPoint = doc.GetRoomAtPoint(pt, phase);
                if (roomAtPoint != null && rooms.Any(r => r.Id == roomAtPoint.Id))
                    return roomAtPoint;
            }
            catch
            {
                // ignore
            }

            return null;
        }

        static double GetHostAreaM2(Element element)
        {
            var areaInt = element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0d;
            if (areaInt > 0d)
            {
                return Math.Round(
                    UnitUtils.ConvertFromInternalUnits(areaInt, UnitTypeId.SquareMeters),
                    2);
            }

            return GetParameterAreaM2(element, ErboAreaParamName);
        }

        static string FormatPlitkaAreaDetail(string roomName, RoomPlitkaSummary summary)
        {
            if (!RoomNameMatcher.MatchesAnyBaseName(roomName, PlitkaRoomBaseNames))
                return null;

            if (summary == null || summary.TotalAreaM2 <= 0d)
                return $"{roomName}: напольной плитки нет (Модель = {FloorTileModelValue})";

            var parts = string.Join(", ", summary.Items);
            return $"{roomName}: {summary.TotalAreaM2:0.##} м² ({parts})";
        }

        sealed class ApronAreaSummary
        {
            public double TotalAreaM2 { get; set; }
            public string Source { get; set; }
            public List<string> Items { get; } = new();
        }

        static ApronAreaSummary CollectApronArea(Document doc)
        {
            var summary = new ApronAreaSummary();
            if (doc == null)
                return summary;

            if (RoomMeasurementsService.TryGetApronAreaFromSchedule(doc, out var scheduleArea, out var scheduleName))
            {
                summary.TotalAreaM2 = Math.Round(scheduleArea, 2);
                summary.Source = "schedule";
                summary.Items.Add($"ведомость «{scheduleName}»");
                return summary;
            }

            foreach (var wall in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Cast<Element>())
            {
                if (!MatchesErboRoom(wall, ApronRoomBaseName))
                    continue;

                var areaM2 = GetParameterAreaM2(wall, ErboAreaParamName);
                if (areaM2 <= 0d)
                    continue;

                var typeName = (doc.GetElement(wall.GetTypeId()) as ElementType)?.Name ?? "стена";
                summary.TotalAreaM2 = Math.Round(summary.TotalAreaM2 + areaM2, 2);
                summary.Items.Add($"{typeName} {areaM2:0.##} м²");
            }

            if (summary.TotalAreaM2 > 0d)
                summary.Source = "walls_erbo";

            return summary;
        }

        static bool MatchesErboRoom(Element element, string roomBaseName)
        {
            var erboRoom = GetParameterString(element, ErboRoomParamName);
            if (string.IsNullOrWhiteSpace(erboRoom) && element?.Document != null)
            {
                var type = element.Document.GetElement(element.GetTypeId()) as ElementType;
                erboRoom = GetParameterString(type, ErboRoomParamName);
            }

            return RoomNameMatcher.MatchesBaseName(erboRoom, roomBaseName);
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

        static double GetElementAreaM2(Element element)
        {
            var fromHost = GetHostAreaM2(element);
            if (fromHost > 0d)
                return fromHost;

            var fromArea = GetParameterAreaM2(element, AreaParamName);
            if (fromArea > 0d)
                return fromArea;

            return GetParameterAreaM2(element, ErboAreaParamName);
        }

        static string FormatApronAreaDetail(string roomName, ApronAreaSummary summary)
        {
            if (!RoomNameMatcher.MatchesBaseName(roomName, ApronRoomBaseName))
                return null;

            if (summary == null || summary.TotalAreaM2 <= 0d)
                return $"{roomName}: фартука нет (ведомость «Спецификация фартука кухни» или {ErboRoomParamName} = {ApronRoomBaseName} + {ErboAreaParamName})";

            if (summary.Source == "schedule")
            {
                var scheduleNote = summary.Items.Count > 0 ? summary.Items[0] : "ведомость";
                return $"{roomName}: {summary.TotalAreaM2:0.##} м² ({scheduleNote})";
            }

            var parts = string.Join(", ", summary.Items);
            return $"{roomName}: {summary.TotalAreaM2:0.##} м² (стены {ErboRoomParamName}={ApronRoomBaseName}, сумма {ErboAreaParamName}: {parts})";
        }

        static bool IsDoubleLeafDoor(FamilyInstance door)
        {
            var widthMm = GetDoorWidthMillimeters(door);
            if (widthMm.HasValue && widthMm.Value > DoubleDoorWidthThresholdMm)
                return true;

            var type = door?.Symbol?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(type))
                return false;

            return type.IndexOf("дв.", StringComparison.OrdinalIgnoreCase) >= 0
                   || type.IndexOf("дв ", StringComparison.OrdinalIgnoreCase) >= 0
                   || type.IndexOf("двуств", StringComparison.OrdinalIgnoreCase) >= 0
                   || type.IndexOf("2-ств", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static double? GetDoorWidthMillimeters(FamilyInstance door)
        {
            var widthM = GetDoorWidthM(door);
            if (widthM <= 0d)
                return null;

            return widthM * 1000d;
        }

        static double? CalcPerimeterRoofM(Room room, out string detailLine)
        {
            var roomName = RoomAreaService.GetRoomDisplayName(room);
            var boundaryM = SumBoundaryPerimeterM(room, SpatialElementBoundaryLocation.Finish);
            var roomPerimeterM = GetPerimeterM(room);

            if (boundaryM > 0d)
            {
                var rounded = Math.Round(boundaryM, 2);
                detailLine = $"{roomName}: контур Finish {rounded:0.##} м (полный, без вычета дверей)";
                return rounded;
            }

            if (roomPerimeterM > 0d)
            {
                detailLine = $"{roomName}: ROOM_PERIMETER {roomPerimeterM:0.##} м (полный, без вычета дверей)";
                return roomPerimeterM;
            }

            detailLine = $"{roomName}: нет данных (граница и ROOM_PERIMETER пусты)";
            return null;
        }

        static double? CalcPerimeterFloorM(Room room, Document doc, Phase phase, out string detailLine)
        {
            var roomName = RoomAreaService.GetRoomDisplayName(room);
            var boundaryM = SumBoundaryPerimeterM(room, SpatialElementBoundaryLocation.Finish);
            var roomPerimeterM = GetPerimeterM(room);
            var grossM = boundaryM > 0d ? boundaryM : roomPerimeterM;

            if (grossM <= 0d)
            {
                detailLine = $"{roomName}: нет данных (граница и ROOM_PERIMETER пусты)";
                return null;
            }

            var doorDeductions = CollectDoorWidthsForRoom(doc, phase, room);
            var deductM = Math.Round(doorDeductions.Sum(d => d.WidthM), 2);
            var netFromGross = Math.Round(Math.Max(0d, grossM - deductM), 2);

            // В части моделей контур Finish уже без дверей (~как в ведомости плинтуса).
            // Если контур уже близок к «брутто − двери» — не вычитаем повторно.
            var netM = boundaryM > 0d && deductM > 0d && boundaryM <= netFromGross + 0.15d
                ? Math.Round(boundaryM, 2)
                : netFromGross;

            var grossSource = boundaryM > 0d ? "контур Finish" : "ROOM_PERIMETER";

            if (deductM > 0d && netM < grossM - 0.01d)
            {
                var parts = string.Join(", ", doorDeductions.Select(d => $"{d.Label} {d.WidthM:0.##} м"));
                detailLine =
                    $"{roomName}: {grossSource} {grossM:0.##} м − двери {deductM:0.##} м ({parts}) = {netM:0.##} м";
            }
            else if (deductM > 0d && Math.Abs(netM - boundaryM) < 0.01d)
            {
                detailLine = $"{roomName}: контур Finish {netM:0.##} м (двери уже в разрывах контура)";
            }
            else
            {
                detailLine = $"{roomName}: {grossSource} {netM:0.##} м (двери не найдены)";
            }

            return netM;
        }

        static double SumBoundaryPerimeterM(Room room, SpatialElementBoundaryLocation location)
        {
            if (room == null)
                return 0d;

            IList<IList<BoundarySegment>> loops;
            try
            {
                var options = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = location
                };
                loops = room.GetBoundarySegments(options);
            }
            catch
            {
                return 0d;
            }

            if (loops == null || loops.Count == 0)
                return 0d;

            var sumInternal = 0d;
            foreach (var loop in loops)
            {
                if (loop == null)
                    continue;

                foreach (var segment in loop)
                {
                    var length = segment?.GetCurve()?.Length ?? 0d;
                    if (length > 0d)
                        sumInternal += length;
                }
            }

            if (sumInternal <= 0d)
                return 0d;

            return UnitUtils.ConvertFromInternalUnits(sumInternal, UnitTypeId.Meters);
        }

        sealed class DoorWidthItem
        {
            public string Label { get; init; }
            public double WidthM { get; init; }
        }

        static List<DoorWidthItem> CollectDoorWidthsForRoom(Document doc, Phase phase, Room room)
        {
            var result = new List<DoorWidthItem>();
            if (doc == null || phase == null || room == null)
                return result;

            foreach (var door in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                var fromRoom = GetDoorRoom(door, phase, from: true);
                var toRoom = GetDoorRoom(door, phase, from: false);
                if (fromRoom?.Id != room.Id && toRoom?.Id != room.Id)
                    continue;

                var widthM = GetDoorWidthM(door);
                if (widthM <= 0d)
                    continue;

                result.Add(new DoorWidthItem
                {
                    Label = BuildOpeningLabel("дверь", door),
                    WidthM = Math.Round(widthM, 2)
                });
            }

            return result;
        }

        static double GetDoorWidthM(FamilyInstance door)
        {
            var width = door?.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble() ?? 0d;
            if (width <= 0d)
                return 0d;

            return UnitUtils.ConvertFromInternalUnits(width, UnitTypeId.Meters);
        }

        sealed class RoomOpeningsSummary
        {
            public double TotalAreaM2 { get; set; }
            public List<OpeningItem> Items { get; } = new();
        }

        sealed class OpeningItem
        {
            public string Label { get; init; }
            public double AreaM2 { get; init; }
        }

        static Dictionary<long, RoomOpeningsSummary> CollectOpeningAreasByRoom(
            Document doc,
            Phase phase,
            IList<Room> rooms)
        {
            var map = rooms.ToDictionary(r => r.Id.Value, _ => new RoomOpeningsSummary());
            if (rooms.Count == 0)
                return map;

            foreach (var door in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                var areaM2 = GetOpeningAreaM2(door);
                if (areaM2 <= 0d)
                    continue;

                var label = BuildOpeningLabel("дверь", door);
                AddOpeningToRoom(map, GetDoorRoom(door, phase, from: true), label, areaM2);
                AddOpeningToRoom(map, GetDoorRoom(door, phase, from: false), label, areaM2);
            }

            foreach (var window in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                var areaM2 = GetOpeningAreaM2(window);
                if (areaM2 <= 0d)
                    continue;

                var label = BuildOpeningLabel("окно", window);
                var room = FindRoomForWindow(window, doc, phase, rooms);
                AddOpeningToRoom(map, room, label, areaM2);
            }

            return map;
        }

        static void AddOpeningToRoom(
            Dictionary<long, RoomOpeningsSummary> map,
            Room room,
            string label,
            double areaM2)
        {
            if (room == null || areaM2 <= 0d)
                return;

            if (!map.TryGetValue(room.Id.Value, out var summary))
                return;

            summary.TotalAreaM2 = Math.Round(summary.TotalAreaM2 + areaM2, 2);
            summary.Items.Add(new OpeningItem { Label = label, AreaM2 = Math.Round(areaM2, 2) });
        }

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

        /// <summary>
        /// Окно привязывается к одному помещению: ERBO_Помещения или точка внутри комнаты
        /// (не по граничной стене — общая стена Гостиная/Балкон давала лишние окна).
        /// </summary>
        static Room FindRoomForWindow(
            FamilyInstance window,
            Document doc,
            Phase phase,
            IList<Room> rooms)
        {
            if (window == null || rooms == null || rooms.Count == 0)
                return null;

            var erboRoom = window.LookupParameter("ERBO_Помещения")?.AsString()?.Trim();
            if (!string.IsNullOrWhiteSpace(erboRoom))
            {
                var byParam = rooms.FirstOrDefault(r =>
                    RoomNameMatcher.MatchesBaseName(RoomAreaService.GetRoomDisplayName(r), erboRoom)
                    || string.Equals(RoomAreaService.GetRoomDisplayName(r), erboRoom, StringComparison.OrdinalIgnoreCase));
                if (byParam != null)
                    return byParam;
            }

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

        static string BuildOpeningLabel(string kind, FamilyInstance instance)
        {
            var mark = instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim();
            return string.IsNullOrWhiteSpace(mark) ? kind : $"{kind} {mark}";
        }

        static double GetOpeningAreaM2(FamilyInstance instance)
        {
            var fromErbo = GetParameterAreaM2(instance, ErboAreaParamName);
            if (fromErbo > 0d)
                return fromErbo;

            return GetFallbackOpeningAreaM2(instance);
        }

        static double GetParameterAreaM2(Element element, string parameterName)
        {
            var param = element?.LookupParameter(parameterName);
            if (param == null || !param.HasValue)
                return 0d;

            if (param.StorageType == StorageType.Double)
            {
                var value = param.AsDouble();
                if (value <= 0d)
                    return 0d;

                return Math.Round(
                    UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.SquareMeters),
                    2);
            }

            var parsed = ParseNullableDouble(param.AsValueString() ?? param.AsString());
            return parsed.HasValue ? Math.Round(parsed.Value, 2) : 0d;
        }

        static double GetFallbackOpeningAreaM2(FamilyInstance instance)
        {
            var widthM = GetInstanceLengthM(instance, BuiltInParameter.DOOR_WIDTH, BuiltInParameter.WINDOW_WIDTH);
            var heightM = GetInstanceLengthM(instance, BuiltInParameter.DOOR_HEIGHT, BuiltInParameter.WINDOW_HEIGHT);

            if (widthM <= 0d || heightM <= 0d)
                return 0d;

            return Math.Round(widthM * heightM, 2);
        }

        static double GetInstanceLengthM(
            FamilyInstance instance,
            BuiltInParameter primary,
            BuiltInParameter alternate)
        {
            var value = instance?.get_Parameter(primary)?.AsDouble() ?? 0d;
            if (value <= 0d)
                value = instance?.get_Parameter(alternate)?.AsDouble() ?? 0d;

            if (value <= 0d)
                return 0d;

            return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Meters);
        }

        static double? ParseNullableDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = NumberRegex.Match(text);
            if (!match.Success)
                return null;

            var token = match.Value.Replace(',', '.');
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        static RoomMeasurementSourceInfo BuildSource(
            RoomMeasurementsElementMapping.Entry entry,
            string phaseName,
            bool found,
            string message) =>
            new RoomMeasurementSourceInfo
            {
                param_code = entry.ParamCode,
                param_name = entry.ParamName,
                schedule_name_expected = entry.SourceDescription,
                schedule_name_found = string.IsNullOrWhiteSpace(phaseName) ? "—" : $"фаза «{phaseName}»",
                Found = found,
                Message = message
            };

        static double GetPerimeterM(Room room)
        {
            var perimeterInt = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER)?.AsDouble() ?? 0d;
            return Math.Round(
                UnitUtils.ConvertFromInternalUnits(perimeterInt, UnitTypeId.Meters),
                2);
        }
    }
}
