using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using SBS.DTO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportAllElementsCommand : BaseCommand
    {
        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);
            
            try
            {                
                var selection = GetCategoriesToExport();
                if (selection == null || !selection.Any())
                {
                    System.Windows.MessageBox.Show("Не выбрано ни одной категории");
                    return Result.Cancelled;
                }

                // Настройки экспорта по умолчанию: выгружаем всё
                var exportSettings = new Views.ExportSettings
                {
                    ExportGeometry = true,
                    ExportBoundingBox = true,
                    
                    ExportAllParameters = true,
                    ExportGeometryParams = true,
                    ExportMaterials = true,
                    ExportConstruction = true,
                    ExportIdentity = true,
                    ExportPhasing = true,
                    ExportStructural = true,
                    ExportAnalytical = true,
                    ExportElectrical = true,
                    ExportMechanical = true,
                    ExportPlumbing = true,
                    ExportGraphics = true,
                    ExportOther = true,
                    
                    ExportSharedParams = true,
                    ExportProjectParams = true,
                    ExportTypeParams = true
                };

                string folderPath = null;
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Выберите папку для экспорта файлов";
                    fbd.ShowNewFolderButton = true;

                    if (fbd.ShowDialog() == DialogResult.OK)
                    {
                        folderPath = fbd.SelectedPath;
                    }
                }

                if (!string.IsNullOrEmpty(folderPath))
                {
                    var result = ExportByCategoryToFiles(doc, selection, folderPath, exportSettings);
                    
                    if (result.TotalCount > 0)
                    {
                        System.Windows.MessageBox.Show(
                            $"Экспорт завершен!\n\n" +
                            $"Всего элементов: {result.TotalCount}\n" +
                            $"Создано файлов: {result.FilesCreated}\n" +
                            $"Папка: {folderPath}",
                            "Экспорт завершен");
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Нет элементов для выгрузки");
                    }
                }
                else
                {
                    return Result.Cancelled;
                }
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, "Ошибка при экспорте элементов");
                System.Windows.MessageBox.Show($"Ошибка: {ex.Message}");
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        private class ExportResult
        {
            public int TotalCount { get; set; }
            public int FilesCreated { get; set; }
        }

        private ExportResult ExportByCategoryToFiles(Document doc, List<BuiltInCategory> categories, string folderPath, Views.ExportSettings settings)
        {
            var result = new ExportResult();
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            AppTools._logger?.Information($"Начало экспорта из {categories.Count} категорий в папку: {folderPath}");

            foreach (var category in categories)
            {
                try
                {
                    var elements = new FilteredElementCollector(doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    int categoryCount = elements.Count;
                    
                    if (categoryCount == 0)
                    {
                        AppTools._logger?.Information($"Категория {category}: элементов не найдено, пропускаем");
                        continue;
                    }

                    AppTools._logger?.Information($"Обработка категории {category}: найдено {categoryCount} элементов");

                    // Создаем имя файла для категории
                    string categoryName = GetCategoryFileName(category);
                    string fileName = $"revit_{categoryName}_{timestamp}.json";
                    string filePath = Path.Combine(folderPath, fileName);

                    int processed = ExportCategoryToFile(doc, elements, filePath, category.ToString(), settings);

                    if (processed > 0)
                    {
                        result.TotalCount += processed;
                        result.FilesCreated++;
                        AppTools._logger?.Information($"Создан файл: {fileName}, элементов: {processed}");
                    }
                }
                catch (Exception ex)
                {
                    AppTools._logger?.Error(ex, $"Ошибка при обработке категории {category}");
                }
            }

            AppTools._logger?.Information($"Экспорт завершен: {result.TotalCount} элементов в {result.FilesCreated} файлах");

            return result;
        }

        private int ExportCategoryToFile(Document doc, ICollection<Element> elements, string filePath, string categoryName, Views.ExportSettings settings)
        {
            int processed = 0;
            int errors = 0;

            using (StreamWriter file = new StreamWriter(filePath, false, Encoding.UTF8))
            using (JsonTextWriter writer = new JsonTextWriter(file))
            {
                writer.Formatting = Formatting.Indented;
                writer.WriteStartArray();

                foreach (var element in elements)
                {
                    try
                    {
                        var dto = ExtractElementData(element, doc, settings);
                        if (dto != null)
                        {
                            var serializer = new JsonSerializer();
                            serializer.Serialize(writer, dto);
                            processed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        AppTools._logger?.Warning(ex, $"Ошибка при обработке элемента {element.Id} категории {categoryName}");
                    }
                }

                writer.WriteEndArray();
            }

            return processed;
        }

        private string GetCategoryFileName(BuiltInCategory category)
        {
            // Преобразуем имя категории в понятное имя файла
            var name = category.ToString().Replace("OST_", "");
            return name;
        }

        private List<BuiltInCategory> GetCategoriesToExport()
        {
            // Все категории Revit
            var categories = new List<BuiltInCategory>();
            
            foreach (BuiltInCategory category in Enum.GetValues(typeof(BuiltInCategory)))
            {
                // Пропускаем невалидные категории
                if (category == BuiltInCategory.INVALID)
                    continue;
                    
                categories.Add(category);
            }

            return categories;
        }

        private RevitElementDto ExtractElementData(Element element, Document doc, Views.ExportSettings settings)
        {
            try
            {
                if (element == null)
                {
                    AppTools._logger?.Warning("Element is null");
                    return null;
                }

                if (settings == null)
                {
                    AppTools._logger?.Warning("Settings is null");
                    return null;
                }

                var dto = new RevitElementDto
                {
                    Id = element.Id.IntegerValue,
                    UniqueId = element.UniqueId ?? "",
                    Category = element.Category?.Name ?? "Без категории",
                    Parameters = new Dictionary<string, ParameterDto>(),
                    Geometry = new List<GeometryLineDto>()
                };

                // Получаем семейство и тип
                try
                {
                    var elementType = doc.GetElement(element.GetTypeId());
                    if (elementType != null)
                    {
                        dto.TypeName = elementType.Name;
                        
                        if (element is FamilyInstance familyInstance)
                        {
                            dto.FamilyName = familyInstance.Symbol?.Family?.Name ?? "Неизвестно";
                        }
                    }

                    // Извлекаем параметры элемента
                    if (settings.ExportProjectParams)
                    {
                        ExtractParameters(element, dto.Parameters, "", settings);
                    }

                    // Извлекаем параметры типа
                    if (elementType != null && settings.ExportTypeParams)
                    {
                        ExtractParameters(elementType, dto.Parameters, "Type_", settings);
                    }
                }
                catch (Exception ex)
                {
                    AppTools._logger?.Warning(ex, $"Ошибка при извлечении параметров элемента {element.Id}");
                }

                // Извлекаем геометрию
                if (settings.ExportGeometry || settings.ExportBoundingBox)
                {
                    try
                    {
                        if (settings.ExportGeometry)
                        {
                            dto.Geometry = ExtractGeometry(element);
                        }
                        
                        if (settings.ExportBoundingBox)
                        {
                            dto.BoundingBox = ExtractBoundingBox(element);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppTools._logger?.Warning(ex, $"Не удалось извлечь геометрию элемента {element.Id}");
                    }
                }

                return dto;
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, $"Критическая ошибка при обработке элемента {element?.Id}");
                return null;
            }
        }

        private void ExtractParameters(Element element, Dictionary<string, ParameterDto> paramDict, string prefix, Views.ExportSettings settings)
        {
            if (settings == null)
                return;

            foreach (Parameter param in element.Parameters)
            {
                try
                {
                    if (param == null || !param.HasValue || param.Definition == null)
                        continue;

                    // Фильтруем общие параметры
                    if (param.IsShared && !settings.ExportSharedParams)
                        continue;

                    var groupName = GetParameterGroupName(param);

                    // Фильтруем по группе параметра
                    if (!settings.ShouldExportParameter(groupName))
                        continue;

                    var paramDto = new ParameterDto
                    {
                        Name = param.Definition.Name ?? "Без имени",
                        ValueType = GetParameterValueType(param),
                        StorageType = param.StorageType.ToString(),
                        GroupName = groupName,
                        IsReadOnly = param.IsReadOnly,
                        IsShared = param.IsShared,
                    };

                    // Получаем значение в зависимости от типа
                    switch (param.StorageType)
                    {
                        case StorageType.String:
                            paramDto.Value = param.AsString() ?? "";
                            break;
                        case StorageType.Integer:
                            paramDto.Value = param.AsInteger().ToString();
                            break;
                        case StorageType.Double:
                            double value = param.AsDouble();
                            paramDto.Value = param.AsValueString() ?? value.ToString();
                            paramDto.Unit = GetParameterUnit(param);
                            break;
                        case StorageType.ElementId:
                            var elemId = param.AsElementId();
                            if (elemId != null && elemId.IntegerValue != -1)
                            {
                                var linkedElem = element.Document.GetElement(elemId);
                                paramDto.Value = linkedElem?.Name ?? elemId.ToString();
                            }
                            break;
                    }

                    var key = prefix + param.Definition.Name;
                    if (!paramDict.ContainsKey(key))
                    {
                        paramDict[key] = paramDto;
                    }
                }
                catch (Exception ex)
                {
                    AppTools._logger?.Warning(ex, $"Ошибка при чтении параметра {param?.Definition?.Name}");
                }
            }
        }

        private string GetParameterValueType(Parameter param)
        {
            try
            {
                if (param?.Definition == null)
                    return param?.StorageType.ToString() ?? "Unknown";

                // В Revit 2021+ используется GetDataType() вместо ParameterType
                var dataType = param.Definition.GetDataType();
                return dataType?.TypeId ?? param.StorageType.ToString();
            }
            catch
            {
                return param?.StorageType.ToString() ?? "Unknown";
            }
        }

        private string GetParameterGroupName(Parameter param)
        {
            try
            {
                var groupTypeId = param?.Definition?.GetGroupTypeId()?.TypeId ?? string.Empty;
                if (string.IsNullOrEmpty(groupTypeId))
                    return string.Empty;

                // Нормализуем имена групп под старый формат PG_*,
                // чтобы существующая фильтрация настроек работала как раньше.
                if (groupTypeId.Contains("geometry"))
                    return "PG_GEOMETRY";
                if (groupTypeId.Contains("materials"))
                    return "PG_MATERIALS";
                if (groupTypeId.Contains("construction"))
                    return "PG_CONSTRUCTION";
                if (groupTypeId.Contains("identity"))
                    return "PG_IDENTITY_DATA";
                if (groupTypeId.Contains("phasing"))
                    return "PG_PHASING";
                if (groupTypeId.Contains("structural"))
                    return "PG_STRUCTURAL";
                if (groupTypeId.Contains("analytical"))
                    return "PG_ANALYTICAL_MODEL";
                if (groupTypeId.Contains("electrical"))
                    return "PG_ELECTRICAL";
                if (groupTypeId.Contains("mechanical"))
                    return "PG_MECHANICAL";
                if (groupTypeId.Contains("plumbing"))
                    return "PG_PLUMBING";
                if (groupTypeId.Contains("graphics"))
                    return "PG_GRAPHICS";

                return groupTypeId;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetParameterUnit(Parameter param)
        {
            try
            {
                var unitType = param.GetUnitTypeId();
                return unitType?.TypeId ?? "";
            }
            catch
            {
                return "";
            }
        }

        private List<GeometryLineDto> ExtractGeometry(Element element)
        {
            var result = new List<GeometryLineDto>();
            
            if (element == null)
                return result;

            try
            {
                // Упрощенная геометрия для экономии памяти
                Options opt = new Options
                {
                    DetailLevel = ViewDetailLevel.Coarse, // Грубый уровень детализации
                    ComputeReferences = false // Не вычисляем референсы
                };

                GeometryElement geomElem = element.get_Geometry(opt);
                if (geomElem == null)
                    return result;

                // Ограничиваем количество линий для каждого элемента (максимум 100)
                int lineCount = 0;
                const int maxLines = 100;

                foreach (GeometryObject geomObj in geomElem)
                {
                    if (lineCount >= maxLines)
                        break;
                    
                    ProcessGeometryObject(geomObj, result, ref lineCount, maxLines);
                }
            }
            catch (Exception ex)
            {
                AppTools._logger?.Warning(ex, $"Ошибка извлечения геометрии элемента {element.Id}");
            }

            return result ?? new List<GeometryLineDto>();
        }

        private void ProcessGeometryObject(GeometryObject geomObj, List<GeometryLineDto> result, ref int lineCount, int maxLines)
        {
            if (lineCount >= maxLines)
                return;

            if (geomObj is Solid solid && solid.Volume > 0)
            {
                // Берем только часть ребер, не все
                int edgeIndex = 0;
                foreach (Edge edge in solid.Edges)
                {
                    if (lineCount >= maxLines || edgeIndex >= 50) // Максимум 50 ребер на solid
                        break;

                    try
                    {
                        Curve curve = edge.AsCurve();
                        result.Add(new GeometryLineDto
                        {
                            Point1 = curve.GetEndPoint(0),
                            Point2 = curve.GetEndPoint(1),
                        });
                        lineCount++;
                        edgeIndex++;
                    }
                    catch
                    {
                        // Пропускаем проблемные ребра
                    }
                }
            }
            else if (geomObj is Curve curve)
            {
                try
                {
                    result.Add(new GeometryLineDto
                    {
                        Point1 = curve.GetEndPoint(0),
                        Point2 = curve.GetEndPoint(1),
                    });
                    lineCount++;
                }
                catch
                {
                    // Пропускаем проблемные кривые
                }
            }
            else if (geomObj is GeometryInstance geomInstance && lineCount < maxLines)
            {
                try
                {
                    GeometryElement instGeom = geomInstance.GetInstanceGeometry();
                    if (instGeom != null)
                    {
                        foreach (GeometryObject instObj in instGeom)
                        {
                            if (lineCount >= maxLines)
                                break;
                            ProcessGeometryObject(instObj, result, ref lineCount, maxLines);
                        }
                    }
                }
                catch
                {
                    // Пропускаем проблемные экземпляры
                }
            }
        }

        private BoundingBoxDto ExtractBoundingBox(Element element)
        {
            if (element == null)
                return null;

            try
            {
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox != null && bbox.Min != null && bbox.Max != null)
                {
                    return new BoundingBoxDto
                    {
                        Min = bbox.Min,
                        Max = bbox.Max
                    };
                }
            }
            catch (Exception ex)
            {
                AppTools._logger?.Warning(ex, $"Ошибка извлечения BoundingBox элемента {element.Id}");
            }

            return null;
        }
    }
}

