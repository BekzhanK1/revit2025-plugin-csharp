using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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
    public class ExportSmartRemontCommand : BaseCommand
    {
        private const string ApartmentNumberParam = "BI_Квартира_Номер";
        private const string FloorFinishParam = "BI_Отделка_Пол";
        private const string WallFinishParam = "BI_Отделка_Стены";
        private const string CeilingFinishParam = "BI_Отделка_Потолок";
        private const string BaseboardFinishParam = "BI_Отделка_Плинтус";

        private const double SqFtToSqM = 0.092903;
        private const double FtToM = 0.3048;

        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                // Собираем только размещенные стены.
                var walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall))
                    .WhereElementIsNotElementType()
                    .Cast<Wall>()
                    .Where(w => w != null && w.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() > 0)
                    .ToList();

                if (!walls.Any())
                {
                    TaskDialog.Show("SmartRemont Export", "Не найдено размещенных стен для выгрузки.");
                    return Result.Succeeded;
                }

                var exportRows = walls.Select(MapWallToDto).ToList();

                // Группируем по номеру квартиры.
                var groupedByApartment = exportRows
                    .GroupBy(w => string.IsNullOrWhiteSpace(w.ApartmentNumber) ? "Без номера квартиры" : w.ApartmentNumber)
                    .Select(g => new SmartRemontApartmentDto
                    {
                        ApartmentNumber = g.Key,
                        WallsCount = g.Count(),
                        Walls = g.Select(x => x.Wall).ToList()
                    })
                    .OrderBy(x => x.ApartmentNumber)
                    .ToList();

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName = $"SmartRemont_WallsExport_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath = Path.Combine(desktopPath, fileName);

                var json = JsonConvert.SerializeObject(groupedByApartment, Formatting.Indented);
                File.WriteAllText(outputPath, json);

                TaskDialog.Show(
                    "SmartRemont Export",
                    $"Выгрузка завершена.\n\nКвартир: {groupedByApartment.Count}\nСтен: {walls.Count}\nФайл: {outputPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, "Ошибка при экспорте данных SmartRemont по стенам");
                message = ex.Message;
                TaskDialog.Show("SmartRemont Export", $"Ошибка выгрузки: {ex.Message}");
                return Result.Failed;
            }
        }

        // Безопасно читаем строковый параметр по имени.
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

        private static double GetParameterDouble(Element element, BuiltInParameter builtInParameter)
        {
            var parameter = element?.get_Parameter(builtInParameter);
            if (parameter == null || !parameter.HasValue)
                return 0d;

            return parameter.AsDouble();
        }

        private static double GetWallHeightFeet(Wall wall)
        {
            // Для неприкрепленных стен.
            var userHeight = GetParameterDouble(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (userHeight > 0)
                return userHeight;

            // Fallback для некоторых типов/версий.
            var attrHeight = GetParameterDouble(wall, BuiltInParameter.WALL_ATTR_HEIGHT_PARAM);
            if (attrHeight > 0)
                return attrHeight;

            return 0d;
        }

        private static double ToRoundedSqM(double areaInSqFt)
        {
            return Math.Round(areaInSqFt * SqFtToSqM, 2);
        }

        private static double ToRoundedM(double lengthInFt)
        {
            return Math.Round(lengthInFt * FtToM, 2);
        }

        private static (string ApartmentNumber, SmartRemontWallDto Wall) MapWallToDto(Wall wall)
        {
            var apartmentNumber = GetParameterString(wall, ApartmentNumberParam);
            var areaSqFt = GetParameterDouble(wall, BuiltInParameter.HOST_AREA_COMPUTED);
            var lengthFt = GetParameterDouble(wall, BuiltInParameter.CURVE_ELEM_LENGTH);
            var heightFt = GetWallHeightFeet(wall);
            var type = wall.Document.GetElement(wall.GetTypeId()) as WallType;
            var thicknessFt = type?.Width ?? 0d;

            var dto = new SmartRemontWallDto
            {
                RevitId = wall.Id.IntegerValue,
                UniqueId = wall.UniqueId ?? string.Empty,
                WallType = type?.Name ?? string.Empty,
                Dimensions = new WallDimensionsDto
                {
                    AreaM2 = ToRoundedSqM(areaSqFt),
                    LengthM = ToRoundedM(lengthFt),
                    HeightM = ToRoundedM(heightFt),
                    ThicknessM = ToRoundedM(thicknessFt)
                },
                Finishes = new WallFinishesDto
                {
                    Floor = GetParameterString(wall, FloorFinishParam),
                    Walls = GetParameterString(wall, WallFinishParam),
                    Ceiling = GetParameterString(wall, CeilingFinishParam),
                    Baseboard = GetParameterString(wall, BaseboardFinishParam)
                }
            };

            return (apartmentNumber, dto);
        }
    }
}
