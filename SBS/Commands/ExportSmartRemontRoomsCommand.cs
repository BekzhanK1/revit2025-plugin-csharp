using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using SBS.DTO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSmartRemontRoomsCommand : BaseCommand
    {
        private const string ApartmentNumberParam = "ADSK_Номер квартиры";
        private const string FloorFinishParam = "Отделка пола";
        private const string WallFinishParam = "Отделка стен";
        private const string CeilingFinishParam = "Отделка потолка";
        private const string LevelParam = "Уровень";
        private const string IfcGuidParam = "IfcGUID";

        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                var targetPhase = new FilteredElementCollector(doc)
                    .OfClass(typeof(Phase))
                    .Cast<Phase>()
                    .FirstOrDefault(p => p.Name.Equals("После монтажных работ", StringComparison.OrdinalIgnoreCase));

                if (targetPhase == null)
                {
                    TaskDialog.Show("SmartRemont Rooms", "Ошибка: Фаза \"После монтажных работ\" не найдена в проекте.");
                    return Result.Failed;
                }

                // Collect rooms created in the "После монтажных работ" phase
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r != null && r.Area > 0 && r.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() == targetPhase.Id)
                    .ToList();

                if (!rooms.Any())
                {
                    TaskDialog.Show("SmartRemont Rooms", "Не найдено размещенных помещений в стадии \"После монтажных работ\" для выгрузки.");
                    return Result.Succeeded;
                }

                var roomDtos = rooms
                    .Select(MapRoomToDto)
                    .OrderBy(r => r.ApartmentNumber)
                    .ThenBy(r => r.Number)
                    .ThenBy(r => r.Name)
                    .ToList();

                var payload = new SmartRemontRoomsExportDto
                {
                    GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Rooms = roomDtos
                };

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName = $"SmartRemont_Rooms_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath = Path.Combine(desktopPath, fileName);

                File.WriteAllText(outputPath, JsonConvert.SerializeObject(payload, Formatting.Indented));

                TaskDialog.Show(
                    "SmartRemont Rooms",
                    $"Экспорт помещений завершен.\n\n" +
                    $"Помещений: {roomDtos.Count}\n" +
                    $"{outputPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, "Ошибка при экспорте помещений SmartRemont");
                message = ex.Message;
                TaskDialog.Show("SmartRemont Rooms", $"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        private static List<List<SmartRemontRoomPointDto>> GetRoomContours(Room room)
        {
            var allLoops = new List<List<SmartRemontRoomPointDto>>();
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var loops = room.GetBoundarySegments(options);
            if (loops == null) return allLoops;

            foreach (var loop in loops)
            {
                var polyline = new List<SmartRemontRoomPointDto>();
                foreach (var seg in loop)
                {
                    var curve = seg.GetCurve();
                    // Берем начальную точку каждого сегмента
                    polyline.Add(ToPoint(curve.GetEndPoint(0)));
                }
                allLoops.Add(polyline);
            }
            return allLoops;
        }

        private static SmartRemontRoomDto MapRoomToDto(Room room)
        {
            var doc = room.Document;

            var apartmentNumber = GetParameterString(room, ApartmentNumberParam);
            var floorFinish = GetParameterString(room, FloorFinishParam);
            var wallFinish = GetParameterString(room, WallFinishParam);
            var ceilingFinish = GetParameterString(room, CeilingFinishParam);
            var levelStr = GetParameterString(room, LevelParam);
            var ifcGuid = GetParameterString(room, IfcGuidParam);

            var nameParam = room.get_Parameter(BuiltInParameter.ROOM_NAME);
            var numberParam = room.get_Parameter(BuiltInParameter.ROOM_NUMBER);
            var areaParam = room.get_Parameter(BuiltInParameter.ROOM_AREA);
            var perimeterParam = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
            var heightParam = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
            var unboundedHeightParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);

            var areaInternal = areaParam?.AsDouble() ?? 0d;
            var perimeterInternal = perimeterParam?.AsDouble() ?? 0d;
            var heightInternal = heightParam?.AsDouble() ?? 0d;
            if (heightInternal <= 0 && unboundedHeightParam != null && unboundedHeightParam.HasValue)
                heightInternal = unboundedHeightParam.AsDouble();

            var levelName = string.Empty;
            if (room.LevelId != ElementId.InvalidElementId)
            {
                var level = doc.GetElement(room.LevelId) as Level;
                levelName = level?.Name ?? string.Empty;
            }

            return new SmartRemontRoomDto
            {
                RevitId = room.Id.Value,
                UniqueId = room.UniqueId ?? string.Empty,
                ApartmentNumber = string.IsNullOrWhiteSpace(apartmentNumber) ? string.Empty : apartmentNumber,
                Number = numberParam?.AsString() ?? string.Empty,
                Name = nameParam?.AsString() ?? string.Empty,
                LevelName = levelName,
                AreaM2 = Math.Round(UnitUtils.ConvertFromInternalUnits(areaInternal, UnitTypeId.SquareMeters), 2),
                PerimeterM = Math.Round(UnitUtils.ConvertFromInternalUnits(perimeterInternal, UnitTypeId.Meters), 2),
                HeightM = Math.Round(UnitUtils.ConvertFromInternalUnits(heightInternal, UnitTypeId.Meters), 2),
                FloorFinish = string.IsNullOrWhiteSpace(floorFinish) ? string.Empty : floorFinish,
                WallFinish = string.IsNullOrWhiteSpace(wallFinish) ? string.Empty : wallFinish,
                CeilingFinish = string.IsNullOrWhiteSpace(ceilingFinish) ? string.Empty : ceilingFinish,
                Level = string.IsNullOrWhiteSpace(levelStr) ? string.Empty : levelStr,
                IfcGUID = string.IsNullOrWhiteSpace(ifcGuid) ? string.Empty : ifcGuid,
                Contours = GetRoomContours(room)
            };
        }

        private static List<SmartRemontRoomPointDto> GetRoomContour(Room room)
        {
            var result = new List<SmartRemontRoomPointDto>();

            try
            {
                var options = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };

                var loops = room.GetBoundarySegments(options);
                if (loops == null || loops.Count == 0)
                    return result;

                var firstLoop = loops.FirstOrDefault();
                if (firstLoop == null || firstLoop.Count == 0)
                    return result;

                XYZ? lastPoint = null;

                foreach (var seg in firstLoop)
                {
                    var curve = seg?.GetCurve();
                    if (curve == null) continue;

                    var start = curve.GetEndPoint(0);
                    var end = curve.GetEndPoint(1);

                    if (lastPoint == null)
                    {
                        result.Add(ToPoint(start));
                    }

                    result.Add(ToPoint(end));
                    lastPoint = end;
                }
            }
            catch
            {
                // ignore boundary errors, return what we have
            }

            return result;
        }

        private static SmartRemontRoomPointDto ToPoint(XYZ p)
        {
            return new SmartRemontRoomPointDto
            {
                X = Math.Round(UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Meters), 3),
                Y = Math.Round(UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Meters), 3)
            };
        }

        private static string GetParameterString(Element element, string parameterName)
        {
            if (element == null || string.IsNullOrWhiteSpace(parameterName))
                return string.Empty;

            var parameter = element.LookupParameter(parameterName);
            if (parameter == null || !parameter.HasValue)
                return string.Empty;

            if (parameter.StorageType == StorageType.String)
                return parameter.AsString() ?? string.Empty;

            return parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
        }
    }
}

