#nullable disable
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using SBS.DTO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")] // Убирает ошибки про SaveFileDialog

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSmartRemontRoomsStructureCommand : BaseCommand
    {
        private const string ApartmentNumberParam = "BI_Квартира_Номер";

        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                // Получаем фазу активного вида
                ElementId activePhaseId = doc.ActiveView.get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId();

                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r != null && r.Area > 0)
                    // Важно: берем только комнаты текущей фазы, чтобы не было дублей
                    .Where(r => r.get_Parameter(BuiltInParameter.ROOM_PHASE).AsElementId() == activePhaseId)
                    .ToList();

                if (!rooms.Any())
                {
                    TaskDialog.Show("SR Экспорт", "Не найдено помещений в текущей фазе.");
                    return Result.Succeeded;
                }

                var roomDtos = rooms
                    .Select(MapRoomToDto)
                    .OrderBy(r => r.ApartmentNumber)
                    .ThenBy(r => r.Number)
                    .ToList();

                var payload = new SmartRemontRoomsExportDto
                {
                    GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Rooms = roomDtos
                };

                // Выбор пути сохранения
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName = $"SmartRemont_Rooms_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath = Path.Combine(desktopPath, fileName);

                File.WriteAllText(outputPath, JsonConvert.SerializeObject(payload, Formatting.Indented));

                TaskDialog.Show("SR Экспорт", $"Выгружено {roomDtos.Count} помещений.\nФайл: {outputPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static SmartRemontRoomDto MapRoomToDto(Room room)
        {
            var doc = room.Document;
            var apartmentNumber = GetParameterString(room, ApartmentNumberParam);

            // Revit 2024+ требует .Id.Value вместо .IntegerValue
            // Используем явное приведение к int, если твой DTO ждет int
            int revitId = (int)room.Id.Value;

            var areaParam = room.get_Parameter(BuiltInParameter.ROOM_AREA);
            var perimeterParam = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
            var heightParam = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);

            var level = doc.GetElement(room.LevelId) as Level;

            return new SmartRemontRoomDto
            {
                RevitId = revitId,
                UniqueId = room.UniqueId,
                ApartmentNumber = apartmentNumber,
                Number = room.Number,
                Name = room.Name,
                LevelName = level?.Name ?? "",
                AreaM2 = Math.Round(UnitUtils.ConvertFromInternalUnits(areaParam.AsDouble(), UnitTypeId.SquareMeters), 2),
                PerimeterM = Math.Round(UnitUtils.ConvertFromInternalUnits(perimeterParam.AsDouble(), UnitTypeId.Meters), 2),
                HeightM = Math.Round(UnitUtils.ConvertFromInternalUnits(heightParam.AsDouble(), UnitTypeId.Meters), 2),
                
                // ВАЖНО: Вызываем метод, который возвращает List<List<...>>
                Contours = GetRoomContours(room)
            };
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
            var parameter = element.LookupParameter(parameterName);
            return (parameter != null && parameter.HasValue) ? parameter.AsValueString() ?? parameter.AsString() : "";
        }
    }
}