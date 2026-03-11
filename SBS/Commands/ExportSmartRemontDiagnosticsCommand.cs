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
    public class ExportSmartRemontDiagnosticsCommand : BaseCommand
    {
        private const int MaxElementsForParamScan = 300;
        private const int MaxTopParams = 50;
        private const int MaxSampleValuesPerParam = 5;
        private const int MaxScheduleRowsPreview = 30;

        private static readonly BuiltInCategory[] TargetCategories =
        {
            BuiltInCategory.OST_Rooms,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_LightingFixtures
        };

        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                var categories = BuildCategoryDiagnostics(doc);
                var schedules = BuildScheduleDiagnostics(doc);
                var quality = BuildDataQuality(schedules);
                var recommendations = BuildRecommendations(categories, quality);

                var report = new SmartRemontModelDiagnosticsDto
                {
                    Meta = new DiagnosticsMetaDto
                    {
                        GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        RevitVersion = commandData.Application.Application.VersionNumber,
                        DocumentTitle = doc.Title ?? string.Empty,
                        DocumentPath = doc.PathName ?? string.Empty
                    },
                    Categories = categories,
                    Schedules = schedules,
                    DataQuality = quality,
                    Recommendations = recommendations
                };

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName = $"SmartRemont_ModelDiagnostics_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath = Path.Combine(desktopPath, fileName);
                var json = JsonConvert.SerializeObject(report, Formatting.Indented);
                File.WriteAllText(outputPath, json);

                TaskDialog.Show(
                    "SmartRemont Diagnostics",
                    $"Диагностика завершена.\n\n" +
                    $"Категорий: {categories.Count}\n" +
                    $"Спецификаций: {schedules.Count}\n" +
                    $"Строк спецификаций: {quality.TotalScheduleRows}\n" +
                    $"Файл: {outputPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, "Ошибка полной диагностики SmartRemont");
                message = ex.Message;
                TaskDialog.Show("SmartRemont Diagnostics", $"Ошибка диагностики: {ex.Message}");
                return Result.Failed;
            }
        }

        private static List<CategoryDiagnosticsDto> BuildCategoryDiagnostics(Document document)
        {
            var result = new List<CategoryDiagnosticsDto>();

            foreach (var bic in TargetCategories)
            {
                var collector = new FilteredElementCollector(document)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType();

                var all = collector.ToElements();
                var sample = all.Take(MaxElementsForParamScan).ToList();

                var paramStats = new Dictionary<string, ParamStat>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in sample)
                {
                    foreach (Parameter p in element.Parameters)
                    {
                        var name = p?.Definition?.Name;
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        if (!paramStats.TryGetValue(name, out var stat))
                        {
                            stat = new ParamStat();
                            paramStats[name] = stat;
                        }

                        stat.Present++;

                        var value = TryGetParameterText(p);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            stat.Filled++;
                            if (stat.Samples.Count < MaxSampleValuesPerParam && !stat.Samples.Contains(value))
                                stat.Samples.Add(value);
                        }
                    }
                }

                var topParams = paramStats
                    .OrderByDescending(x => x.Value.Present)
                    .Take(MaxTopParams)
                    .Select(x => new ParameterCoverageDto
                    {
                        Name = x.Key,
                        PresentInElements = x.Value.Present,
                        FilledValues = x.Value.Filled,
                        SampleValues = x.Value.Samples
                    })
                    .ToList();

                result.Add(new CategoryDiagnosticsDto
                {
                    Category = bic.ToString().Replace("OST_", string.Empty),
                    ElementsCount = all.Count,
                    TopParameters = topParams
                });
            }

            return result.OrderByDescending(x => x.ElementsCount).ToList();
        }

        private static List<ScheduleDiagnosticsDto> BuildScheduleDiagnostics(Document document)
        {
            var schedules = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(s => s != null && !s.IsTemplate)
                .OrderBy(s => s.Name)
                .ToList();

            var result = new List<ScheduleDiagnosticsDto>();

            foreach (var schedule in schedules)
            {
                var tableData = schedule.GetTableData();
                var body = tableData.GetSectionData(SectionType.Body);
                var header = tableData.GetSectionData(SectionType.Header);

                if (body == null || body.NumberOfColumns <= 0)
                    continue;

                var headersMap = BuildHeaderMap(schedule, header, body);
                var headers = headersMap.OrderBy(k => k.Key).Select(v => v.Value).ToList();

                var rowSamples = new List<ScheduleRowSampleDto>();
                if (body.NumberOfRows > 0)
                {
                    var takeRows = Math.Min(MaxScheduleRowsPreview, body.NumberOfRows);
                    for (int i = 0; i < takeRows; i++)
                    {
                        var row = body.FirstRowNumber + i;
                        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int col = body.FirstColumnNumber; col <= body.LastColumnNumber; col++)
                        {
                            var key = headersMap[col];
                            var value = SafeGetCellText(schedule, SectionType.Body, row, col);
                            values[key] = value;
                        }

                        rowSamples.Add(new ScheduleRowSampleDto
                        {
                            RowType = ClassifyRow(values),
                            Values = values
                        });
                    }
                }

                result.Add(new ScheduleDiagnosticsDto
                {
                    Name = schedule.Name,
                    ColumnsCount = body.NumberOfColumns,
                    RowsCount = body.NumberOfRows,
                    Headers = headers,
                    RowSamples = rowSamples
                });
            }

            return result;
        }

        private static DataQualityDiagnosticsDto BuildDataQuality(List<ScheduleDiagnosticsDto> schedules)
        {
            var rows = schedules.SelectMany(s => s.RowSamples).ToList();
            return new DataQualityDiagnosticsDto
            {
                TotalSchedules = schedules.Count,
                TotalScheduleRows = schedules.Sum(s => s.RowsCount),
                HeaderLikeRows = rows.Count(r => r.RowType == "Header"),
                SectionLikeRows = rows.Count(r => r.RowType == "Section"),
                TotalLikeRows = rows.Count(r => r.RowType == "Total"),
                ItemLikeRows = rows.Count(r => r.RowType == "Item")
            };
        }

        private static List<string> BuildRecommendations(List<CategoryDiagnosticsDto> categories, DataQualityDiagnosticsDto quality)
        {
            var result = new List<string>();

            var rooms = categories.FirstOrDefault(c => c.Category.Equals("Rooms", StringComparison.OrdinalIgnoreCase));
            if (rooms == null || rooms.ElementsCount == 0)
                result.Add("В модели не обнаружены размещенные Rooms. Для группировки сметы нужна стратегия через секции спецификаций.");

            if (quality.ItemLikeRows == 0)
                result.Add("В сэмпле спецификаций не найдены item-строки. Проверьте структуру таблиц и заголовки колонок.");

            if (quality.SectionLikeRows > 0 && quality.ItemLikeRows > 0)
                result.Add("Обнаружена секционная структура спецификаций: рекомендуется контекстный парсер (section -> item).");

            result.Add("Для боевого экспорта фиксируйте маппинг колонок по имени спецификации (ID/Тип/Кол-во/Ед.).");
            return result;
        }

        private static Dictionary<int, string> BuildHeaderMap(ViewSchedule schedule, TableSectionData header, TableSectionData body)
        {
            var headers = new Dictionary<int, string>();
            for (int col = body.FirstColumnNumber; col <= body.LastColumnNumber; col++)
            {
                var headerName = string.Empty;
                if (header != null &&
                    header.NumberOfRows > 0 &&
                    col >= header.FirstColumnNumber &&
                    col <= header.LastColumnNumber)
                {
                    for (int hr = header.LastRowNumber; hr >= header.FirstRowNumber; hr--)
                    {
                        var value = SafeGetCellText(schedule, SectionType.Header, hr, col);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            headerName = value;
                            break;
                        }
                    }
                }
                headers[col] = string.IsNullOrWhiteSpace(headerName) ? $"Column_{col}" : headerName;
            }
            return headers;
        }

        private static string SafeGetCellText(ViewSchedule schedule, SectionType section, int row, int col)
        {
            try
            {
                return schedule.GetCellText(section, row, col)?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ClassifyRow(Dictionary<string, string> values)
        {
            if (values == null || values.Count == 0)
                return "Empty";

            var first = values.Values.FirstOrDefault()?.Trim() ?? string.Empty;
            var rest = values.Values.Skip(1).ToList();
            var restHasValues = rest.Any(v => !string.IsNullOrWhiteSpace(v));

            if (first.IndexOf("итог", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Total";

            if (!string.IsNullOrWhiteSpace(first) && !restHasValues)
                return "Section";

            if (first.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("тип", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("наименование", StringComparison.OrdinalIgnoreCase))
                return "Header";

            if (IsLikelyMaterialCode(first))
                return "Item";

            if (!string.IsNullOrWhiteSpace(first) && restHasValues)
                return "Item";

            return "Unknown";
        }

        private static bool IsLikelyMaterialCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (value == "-")
                return true;
            return value.All(char.IsDigit);
        }

        private static string TryGetParameterText(Parameter parameter)
        {
            try
            {
                if (parameter == null || !parameter.HasValue)
                    return string.Empty;

                if (parameter.StorageType == StorageType.String)
                    return parameter.AsString() ?? string.Empty;
                return parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private class ParamStat
        {
            public int Present { get; set; }
            public int Filled { get; set; }
            public List<string> Samples { get; set; } = new List<string>();
        }
    }
}
