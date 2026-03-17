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

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TestSbsSmartRemontCommand : BaseCommand
    {
        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r != null && r.Area > 0)
                    .ToList();

                if (!rooms.Any())
                {
                    TaskDialog.Show("ТЕСТ SBS + SR", "Не найдено размещенных помещений.");
                    return Result.Succeeded;
                }

                var allResults = new List<MaterialInfoDto>();

                foreach (var room in rooms)
                {
                    var roomFinishes = GetRoomFinishing(room);
                    foreach (var m in roomFinishes)
                    {
                        // Заполняем контекст комнаты для наглядности
                        m.RoomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
                        m.RoomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? string.Empty;

                        var levelName = string.Empty;
                        if (room.LevelId != ElementId.InvalidElementId)
                        {
                            var level = room.Document.GetElement(room.LevelId) as Level;
                            levelName = level?.Name ?? string.Empty;
                        }
                        m.RoomLevelName = levelName;

                        allResults.Add(m);
                    }
                }

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName = $"SmartRemont_TestRoomFinishing_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath = Path.Combine(desktopPath, fileName);

                File.WriteAllText(outputPath, JsonConvert.SerializeObject(allResults, Formatting.Indented));

                TaskDialog.Show(
                    "ТЕСТ SBS + SR",
                    $"Гибридный тест-экспорт завершен.\n\n" +
                    $"Помещений: {rooms.Count}\n" +
                    $"Материалов отделки (после фильтра): {allResults.Count}\n\n" +
                    $"{outputPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, "Ошибка в команде ТЕСТ SBS + SR");
                message = ex.Message;
                TaskDialog.Show("ТЕСТ SBS + SR", $"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        private List<MaterialInfoDto> GetRoomFinishing(Room room)
        {
            var finishResults = new List<MaterialInfoDto>();
            var doc = room.Document;

            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            var boundarySegments = room.GetBoundarySegments(options);
            if (boundarySegments == null) return finishResults;

            foreach (var segmentList in boundarySegments)
            {
                foreach (var segment in segmentList)
                {
                    Element element = doc.GetElement(segment.ElementId);
                    if (element == null) continue;

                    ICollection<ElementId> matIds = element.GetMaterialIds(false);
                    foreach (ElementId mId in matIds)
                    {
                        var mat = doc.GetElement(mId) as Material;
                        if (mat == null) continue;

                        if (!IsFinishingMaterial(mat)) continue;

                        double volumeInternal = 0;
                        double areaInternal = 0;

                        try { volumeInternal = element.GetMaterialVolume(mId); }
                        catch { /* ignore */ }

                        try { areaInternal = element.GetMaterialArea(mId, false); }
                        catch { /* ignore */ }

                        var metricArea = UnitUtils.ConvertFromInternalUnits(areaInternal, UnitTypeId.SquareMeters);
                        var metricVolume = UnitUtils.ConvertFromInternalUnits(volumeInternal, UnitTypeId.CubicMeters);

                        finishResults.Add(new MaterialInfoDto
                        {
                            Name = mat.Name,
                            Area = Math.Round(metricArea, 2),
                            Volume = Math.Round(metricVolume, 3)
                        });
                    }
                }
            }

            return finishResults;
        }

        private bool IsFinishingMaterial(Material mat)
        {
            if (mat == null) return false;

            var name = (mat.Name ?? string.Empty).ToLowerInvariant();
            var category = (mat.MaterialClass ?? string.Empty).ToLowerInvariant();

            string[] blacklist =
            {
                "бетон", "concrete",
                "кирпич", "brick",
                "газоблок", "газобетон", "masonry",
                "арматур",
                "монолит", "monolith",
                "ж/б", "жб", "rc"
            };

            return !blacklist.Any(word =>
                (!string.IsNullOrEmpty(name) && name.Contains(word)) ||
                (!string.IsNullOrEmpty(category) && category.Contains(word)));
        }
    }
}

