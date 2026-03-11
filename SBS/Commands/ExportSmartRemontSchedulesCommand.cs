using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
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
    public class ExportSmartRemontSchedulesCommand : BaseCommand
    {
        private static readonly HashSet<string> ElectricalSchedules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Спецификация электрических приборов",
            "Спецификация LED лент"
        };

        private static readonly HashSet<string> PlumbingSchedules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Спецификация сантех. оборудования"
        };

        private static readonly string[] BillableScheduleNames =
        {
            // Отделка (дубль "напольных плиток" исключён, используем только "площади")
            "Спецификация галтели",
            "Спецификация краски для стен балкона",
            "Спецификация площади напольных плиток",
            "Спецификация площади настенных плиток",
            "Спецификация плинтуса",
            "Спецификация поклейка обоев с покраской",
            "Спецификация потолков",
            // Электрика
            "Спецификация электрических приборов",
            "Спецификация LED лент",
            // Сантехника
            "Спецификация сантех. оборудования"
        };

        private static readonly string[] ReferenceOnlyScheduleNames =
        {
            "Экспликация помещений до монтажных работ",
            "Экспликация помещений после монтажных работ",
            "Спецификация помещений в этапе монтажных работ",
            "Спецификация помещений в этапе обмерных работ"
        };

        private static readonly string[] TargetScheduleNames =
            BillableScheduleNames.Concat(ReferenceOnlyScheduleNames).ToArray();

        private static readonly string[] ApartmentAliases  = { "bi_квартира_номер", "квартира", "№ квартиры", "номер квартиры", "кв." };
        private static readonly string[] RoomAliases       = { "помещение", "наименование помещения", "наименование помещений", "номер помещения", "комната", "room" };
        private static readonly string[] WorkTypeAliases   = { "вид работ", "работа", "тип отделки", "наименование работ" };
        private static readonly string[] MaterialCodeAliases = { "id", "код", "артикул", "марка", "mark" };
        private static readonly string[] MaterialNameAliases = { "материал", "наименование", "тип", "типоразмер" };
        private static readonly string[] QuantityAliases   = { "кол-во", "количество", "площадь", "длина", "объем", "quantity", "число" };
        private static readonly string[] UnitAliases       = { "ед.", "ед.изм", "единица", "unit" };

        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                var schedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => s != null && !s.IsTemplate &&
                                TargetScheduleNames.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(s => s.Name)
                    .ToList();

                if (!schedules.Any())
                {
                    TaskDialog.Show("SmartRemont Export", "Не найдены целевые спецификации.");
                    return Result.Cancelled;
                }

                var sourceSummary = new List<ScheduleSummaryDto>();
                var normalizedItems = new List<(string ApartmentNumber, string RoomKey, SmartRemontWorkItemDto WorkItem)>();
                var billableCount  = 0;
                var referenceCount = 0;

                foreach (var schedule in schedules)
                {
                    var rows = ReadScheduleRows(schedule);
                    sourceSummary.Add(new ScheduleSummaryDto { Name = schedule.Name, RowsCount = rows.Count });

                    if (BillableScheduleNames.Contains(schedule.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        billableCount++;
                        normalizedItems.AddRange(ParseScheduleRows(schedule.Name, rows));
                    }
                    else if (ReferenceOnlyScheduleNames.Contains(schedule.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        referenceCount++;
                    }
                }

                normalizedItems = DeduplicateItems(normalizedItems);

                var apartments = normalizedItems
                    .Where(x => !string.IsNullOrWhiteSpace(x.ApartmentNumber) || !string.IsNullOrWhiteSpace(x.RoomKey))
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.ApartmentNumber) ? "Без номера квартиры" : x.ApartmentNumber)
                    .Select(ag => new SmartRemontScheduleApartmentDto
                    {
                        ApartmentNumber = ag.Key,
                        Rooms = ag
                            .GroupBy(x => string.IsNullOrWhiteSpace(x.RoomKey) ? "Без помещения" : x.RoomKey)
                            .Select(rg => new SmartRemontScheduleRoomDto
                            {
                                RoomKey   = rg.Key,
                                WorkItems = rg.Select(x => x.WorkItem).ToList()
                            })
                            .OrderBy(x => x.RoomKey)
                            .ToList()
                    })
                    .OrderBy(x => x.ApartmentNumber)
                    .ToList();

                var unmapped = normalizedItems
                    .Where(x => string.IsNullOrWhiteSpace(x.ApartmentNumber) && string.IsNullOrWhiteSpace(x.RoomKey))
                    .Select(x => x.WorkItem)
                    .Where(w => !string.IsNullOrWhiteSpace(w.MaterialName) && w.Quantity.HasValue)
                    .ToList();

                var payload = new SmartRemontScheduleExportDto
                {
                    GeneratedAt      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    SourceSchedules  = sourceSummary,
                    Apartments       = apartments,
                    UnmappedWorkItems = unmapped
                };

                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName    = $"SmartRemont_FinishingSchedules_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var outputPath  = Path.Combine(desktopPath, fileName);
                File.WriteAllText(outputPath, JsonConvert.SerializeObject(payload, Formatting.Indented));

                TaskDialog.Show(
                    "SmartRemont Export",
                    $"Экспорт завершен.\n\n" +
                    $"Спецификаций: {sourceSummary.Count}  (сметных: {billableCount}, справочных: {referenceCount})\n" +
                    $"Позиций: {normalizedItems.Count}  |  Квартир: {apartments.Count}\n" +
                    $"Unmapped: {unmapped.Count}\n\n{outputPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, "Ошибка при экспорте спецификаций SmartRemont");
                message = ex.Message;
                TaskDialog.Show("SmartRemont Export", $"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        // ── Revit reading ──────────────────────────────────────────────────────────

        private static List<Dictionary<string, string>> ReadScheduleRows(ViewSchedule schedule)
        {
            var result    = new List<Dictionary<string, string>>();
            var tableData = schedule.GetTableData();
            var body      = tableData.GetSectionData(SectionType.Body);
            var header    = tableData.GetSectionData(SectionType.Header);

            if (body == null || body.NumberOfRows <= 0 || body.NumberOfColumns <= 0)
                return result;

            var colMap = BuildHeaderMap(schedule, header, body);

            for (int row = body.FirstRowNumber; row <= body.LastRowNumber; row++)
            {
                var values      = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var hasAnyValue = false;

                for (int col = body.FirstColumnNumber; col <= body.LastColumnNumber; col++)
                {
                    var colName = colMap.TryGetValue(col, out var h) ? h : $"Column_{col}";
                    var cell    = SafeGetCellText(schedule, SectionType.Body, row, col);
                    if (!string.IsNullOrWhiteSpace(cell)) hasAnyValue = true;
                    if (!values.ContainsKey(colName)) values[colName] = cell;
                }

                if (hasAnyValue) result.Add(values);
            }

            return result;
        }

        private static Dictionary<int, string> BuildHeaderMap(
            ViewSchedule schedule, TableSectionData header, TableSectionData body)
        {
            var map = new Dictionary<int, string>();

            for (int col = body.FirstColumnNumber; col <= body.LastColumnNumber; col++)
            {
                var name = string.Empty;
                if (header != null && header.NumberOfRows > 0 &&
                    col >= header.FirstColumnNumber && col <= header.LastColumnNumber)
                {
                    for (int hr = header.LastRowNumber; hr >= header.FirstRowNumber; hr--)
                    {
                        var v = SafeGetCellText(schedule, SectionType.Header, hr, col);
                        if (!string.IsNullOrWhiteSpace(v)) { name = v; break; }
                    }
                }
                map[col] = string.IsNullOrWhiteSpace(name) ? $"Column_{col}" : name;
            }

            return map;
        }

        private static string SafeGetCellText(ViewSchedule schedule, SectionType section, int row, int col)
        {
            try { return schedule.GetCellText(section, row, col)?.Trim() ?? string.Empty; }
            catch { return string.Empty; }
        }

        // ── Column-label extraction (reads column names from body header row) ──────

        private static Dictionary<string, string> ExtractColumnLabels(List<Dictionary<string, string>> rows)
        {
            var labelKeywords = new[]
            {
                "кол-во", "количество", "площадь", "длина", "число", "литр", "масса",
                "наименование", "тип", "комната", "уровень", "сущ/накл", "мм", "изображение"
            };

            foreach (var row in rows)
            {
                var nonEmpty = row.Values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                if (nonEmpty.Count < 2) continue;

                // Only pure-text rows (no numbers) qualify as label rows
                if (nonEmpty.Any(v => ParseNullableDouble(v).HasValue)) continue;

                // At least one value must look like a column label keyword
                var matchCount = nonEmpty.Count(v =>
                    labelKeywords.Any(lbl => ContainsIgnoreCase(v, lbl)));
                if (matchCount < 1) continue;

                var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in row)
                    if (!string.IsNullOrWhiteSpace(kvp.Value))
                        labels[kvp.Key] = kvp.Value; // e.g.  Column_1 -> "Комната, имя"

                return labels;
            }

            return new Dictionary<string, string>();
        }

        private static Dictionary<string, string> ApplyColumnLabels(
            Dictionary<string, string> row, Dictionary<string, string> labels)
        {
            if (labels.Count == 0) return row;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in row)
            {
                var newKey = labels.TryGetValue(kvp.Key, out var label) ? label : kvp.Key;
                if (!result.ContainsKey(newKey)) result[newKey] = kvp.Value;
            }
            return result;
        }

        // ── Parsing ────────────────────────────────────────────────────────────────

        private static string GetDiscipline(string scheduleName)
        {
            if (ElectricalSchedules.Contains(scheduleName)) return "Электрика";
            if (PlumbingSchedules.Contains(scheduleName))   return "Сантехника";
            return "Отделка";
        }

        private static List<(string ApartmentNumber, string RoomKey, SmartRemontWorkItemDto WorkItem)>
            ParseScheduleRows(string scheduleName, List<Dictionary<string, string>> rows)
        {
            var result     = new List<(string, string, SmartRemontWorkItemDto)>();
            var discipline = GetDiscipline(scheduleName);

            // Rename Column_N keys to real column labels from the body header row
            var columnLabels = ExtractColumnLabels(rows);
            var labeled      = rows.Select(r => ApplyColumnLabels(r, columnLabels)).ToList();

            var currentSection   = string.Empty; // room context (Отделка / Сантехника)
            var currentWorkGroup = string.Empty; // work category context (Электрика)

            foreach (var row in labeled)
            {
                if (IsTotalRow(row))       continue;
                if (IsColumnLabelRow(row)) continue;

                if (IsSectionRow(row))
                {
                    var sectionName = GetFirstNonEmptyValue(row);
                    if (discipline == "Электрика")
                        currentWorkGroup = sectionName;
                    else
                        currentSection = sectionName;
                    continue;
                }

                var mapped = MapToWorkItem(scheduleName, discipline, row, currentSection, currentWorkGroup);
                if (mapped.WorkItem == null) continue;

                result.Add(mapped);
            }

            return result;
        }

        private static (string ApartmentNumber, string RoomKey, SmartRemontWorkItemDto WorkItem) MapToWorkItem(
            string scheduleName, string discipline,
            Dictionary<string, string> values,
            string currentSection, string currentWorkGroup)
        {
            var apartment = GetByAliases(values, ApartmentAliases);

            // Room resolution:
            //  Электрика  — room from "Комната, имя" column; section = work group
            //  Other      — room from section (or room column if present)
            var roomFromColumn = GetByAliases(values, RoomAliases);
            string room;

            if (discipline == "Электрика")
            {
                var hasSpecificRoom = !string.IsNullOrWhiteSpace(roomFromColumn) &&
                                      !ContainsIgnoreCase(roomFromColumn, "<варианты>");
                room = hasSpecificRoom ? roomFromColumn
                                       : (!string.IsNullOrWhiteSpace(currentWorkGroup) ? currentWorkGroup : "Без помещения");
            }
            else
            {
                room = !string.IsNullOrWhiteSpace(roomFromColumn) ? roomFromColumn : currentSection;
            }

            // Quantity + source column key (needed for мм→м)
            var (quantityRaw, quantitySourceKey) = GetByAliasesWithKey(values, QuantityAliases);
            if (string.IsNullOrWhiteSpace(quantityRaw))
            {
                quantityRaw      = GetBestQuantityCandidate(values);
                quantitySourceKey = string.Empty;
            }
            var quantity = ParseNullableDouble(quantityRaw);

            // Unit
            var unit = GetByAliases(values, UnitAliases);
            if (string.IsNullOrWhiteSpace(unit)) unit = InferUnitFromColumnKeys(values);
            if (string.IsNullOrWhiteSpace(unit)) unit = DefaultUnitByDisciplineAndSchedule(discipline, scheduleName);

            // мм → м conversion (LED лент and similar)
            if (!string.IsNullOrWhiteSpace(quantitySourceKey) && quantity.HasValue)
            {
                var sourceNorm = quantitySourceKey.ToLowerInvariant();
                if (sourceNorm.Contains(", мм") || sourceNorm.EndsWith(" мм"))
                {
                    quantity = Math.Round(quantity.Value / 1000.0, 3);
                    unit     = "м";
                }
            }

            // Material code
            var materialCode = GetByAliases(values, MaterialCodeAliases);
            if (materialCode == "-") materialCode = string.Empty;
            if (string.IsNullOrWhiteSpace(materialCode)) materialCode = InferMaterialCode(values);

            // Material name
            var materialName = GetByAliases(values, MaterialNameAliases);
            if (string.IsNullOrWhiteSpace(materialName) || materialName == "-")
                materialName = GetFirstMeaningfulTextValue(values, materialCode);

            // Сантехника: skip technical rows (empty name = only height data)
            if (discipline == "Сантехника" && string.IsNullOrWhiteSpace(materialName))
                return (apartment, room, null);

            // WorkType: for electrical use current work group as category label
            var explicitWorkType = GetByAliases(values, WorkTypeAliases);
            var workType = discipline == "Электрика" && !string.IsNullOrWhiteSpace(currentWorkGroup)
                ? currentWorkGroup
                : (!string.IsNullOrWhiteSpace(explicitWorkType) ? explicitWorkType : scheduleName);

            var workItem = new SmartRemontWorkItemDto
            {
                SourceSchedule = scheduleName,
                Discipline     = discipline,
                WorkType       = workType,
                MaterialCode   = materialCode,
                MaterialName   = materialName,
                Quantity       = quantity,
                Unit           = unit,
                RawValues      = values
            };

            if (string.IsNullOrWhiteSpace(workItem.MaterialName) && !workItem.Quantity.HasValue)
                return (apartment, room, null);

            return (apartment, room, workItem);
        }

        // ── Deduplication ──────────────────────────────────────────────────────────

        private static List<(string ApartmentNumber, string RoomKey, SmartRemontWorkItemDto WorkItem)>
            DeduplicateItems(List<(string ApartmentNumber, string RoomKey, SmartRemontWorkItemDto WorkItem)> items)
        {
            return items
                .GroupBy(x => (
                    x.ApartmentNumber,
                    x.RoomKey,
                    x.WorkItem.Discipline,
                    DedupeKey: !string.IsNullOrWhiteSpace(x.WorkItem.MaterialCode)
                        ? x.WorkItem.MaterialCode
                        : (x.WorkItem.MaterialName ?? string.Empty).ToLowerInvariant().Trim(),
                    x.WorkItem.Unit
                ))
                .Select(g => g.OrderByDescending(x => !string.IsNullOrWhiteSpace(x.WorkItem.MaterialCode)).First())
                .ToList();
        }

        // ── Row classification ─────────────────────────────────────────────────────

        private static bool IsTotalRow(Dictionary<string, string> values)
            => values.Values.Any(v => ContainsIgnoreCase(v, "общий итог") || ContainsIgnoreCase(v, "итог"));

        // After ApplyColumnLabels the label row becomes key==value for all entries
        private static bool IsColumnLabelRow(Dictionary<string, string> row)
        {
            var nonEmpty = row.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)).ToList();
            if (nonEmpty.Count < 2) return false;
            var matched = nonEmpty.Count(kvp =>
                string.Equals(kvp.Key?.Trim(), kvp.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
            return matched >= nonEmpty.Count - 1;
        }

        private static bool IsSectionRow(Dictionary<string, string> values)
        {
            var nonEmpty = values.Values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .ToList();

            if (nonEmpty.Count != 1) return false;
            var candidate = nonEmpty[0];
            if (ContainsIgnoreCase(candidate, "общий итог")) return false;
            if (ParseNullableDouble(candidate).HasValue)     return false;
            return true;
        }

        // ── Value-extraction helpers ───────────────────────────────────────────────

        private static string GetFirstNonEmptyValue(Dictionary<string, string> values)
            => values.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        // Prefer decimal values in non-primary columns; fallback to small integers
        private static string GetBestQuantityCandidate(Dictionary<string, string> values)
        {
            var entries = values
                .Select((kv, i) => new
                {
                    Index = i,
                    Value = (kv.Value ?? string.Empty).Trim(),
                    Num   = ParseNullableDouble(kv.Value)
                })
                .Where(x => x.Num.HasValue)
                .ToList();

            if (!entries.Any()) return string.Empty;

            var nonPrimary = entries.Where(x => x.Index > 0).ToList();

            var dec = nonPrimary.Where(x => x.Value.Contains(",") || x.Value.Contains(".")).ToList();
            if (dec.Any()) return dec.First().Value;

            var small = nonPrimary
                .Where(x => !(x.Value.Contains(",") || x.Value.Contains(".")) && x.Num.Value >= 0 && x.Num.Value < 10000)
                .ToList();
            if (small.Any()) return small.First().Value;

            return nonPrimary.Any() ? nonPrimary.First().Value : entries.First().Value;
        }

        // Heuristic: large integer without decimal = likely a material code
        private static string InferMaterialCode(Dictionary<string, string> values)
        {
            foreach (var v in values.Values)
            {
                var c = (v ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(c)) continue;
                if ((c.Contains(",") || c.Contains(".")) && ParseNullableDouble(c).HasValue) continue;
                if (double.TryParse(c, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) && n >= 100)
                    return c;
            }
            return string.Empty;
        }

        private static string GetFirstMeaningfulTextValue(Dictionary<string, string> values, string excludeCode)
        {
            foreach (var v in values.Values)
            {
                var t = (v ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(t))               continue;
                if (t == "-")                                    continue;
                if (ContainsIgnoreCase(t, "общий итог"))         continue;
                if (ContainsIgnoreCase(t, "<варианты>"))         continue;
                if (ParseNullableDouble(t).HasValue)             continue;
                if (!string.IsNullOrWhiteSpace(excludeCode) && t == excludeCode) continue;
                return t;
            }
            return string.Empty;
        }

        private static string InferUnitFromColumnKeys(Dictionary<string, string> values)
        {
            foreach (var key in values.Keys)
            {
                var k = (key ?? string.Empty).ToLowerInvariant();
                if (k.Contains("площадь"))                               return "м²";
                if (k.Contains("длина"))                                 return "м";
                if (k.Contains("литр"))                                  return "л";
                if (k.Contains("масса"))                                 return "кг";
                if (k.Contains("кол-во") || k.Contains("количество") ||
                    k.Contains("число"))                                  return "шт";
            }
            return string.Empty;
        }

        private static string DefaultUnitByDisciplineAndSchedule(string discipline, string scheduleName)
        {
            if (discipline == "Электрика" || discipline == "Сантехника") return "шт";

            var n = (scheduleName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("площади") || n.Contains("покраск") || n.Contains("обоев") ||
                n.Contains("потолков") || n.Contains("потолок") || n.Contains("плиток") ||
                n.Contains("краски")  || n.Contains("краска"))            return "м²";
            if (n.Contains("плинтус") || n.Contains("галтел") ||
                n.Contains("led")     || n.Contains("молдинг") ||
                n.Contains("т-профил"))                                    return "м";
            return string.Empty;
        }

        // ── Generic helpers ────────────────────────────────────────────────────────

        private static bool ContainsIgnoreCase(string source, string token)
            => (source ?? string.Empty).IndexOf(token ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string GetByAliases(Dictionary<string, string> values, string[] aliases)
            => GetByAliasesWithKey(values, aliases).value;

        private static (string value, string key) GetByAliasesWithKey(
            Dictionary<string, string> values, string[] aliases)
        {
            foreach (var pair in values)
            {
                var k = pair.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(k)) continue;
                var norm = k.ToLowerInvariant();
                foreach (var alias in aliases)
                    if (norm.Contains(alias))
                        return (pair.Value?.Trim() ?? string.Empty, k);
            }
            return (string.Empty, string.Empty);
        }

        private static double? ParseNullableDouble(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var c = input.Trim().Replace(" ", string.Empty);
            if (double.TryParse(c.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v1)) return v1;
            if (double.TryParse(c.Replace('.', ','), NumberStyles.Any, CultureInfo.GetCultureInfo("ru-RU"), out var v2)) return v2;
            return null;
        }
    }
}
