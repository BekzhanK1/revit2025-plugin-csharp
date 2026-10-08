using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// Все ведомости модели как есть — для разбора, почему объёмы ДС ТК посчитались так.
    /// Строки — массивы ячеек, а не словари: одинаковые заголовки колонок не теряются.
    /// </summary>
    public static class ScheduleDumpService
    {
        public static JObject Build(Document doc)
        {
            var root = new JObject
            {
                ["generated_at"] = DateTime.Now.ToString("o"),
                ["document_title"] = doc?.Title
            };

            var schedules = new JArray();
            if (doc != null)
            {
                // Какой источник плагина читает ведомость — по именам из конфига (с точками и без).
                var sourceByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in TkQtyScheduleMapping.All)
                {
                    foreach (var name in entry.ScheduleNamesExact ?? new List<string>())
                    {
                        var key = Normalize(name);
                        if (string.IsNullOrEmpty(key))
                            continue;
                        if (!sourceByName.TryGetValue(key, out var codes))
                            sourceByName[key] = codes = new List<string>();
                        if (!codes.Contains(entry.Code))
                            codes.Add(entry.Code);
                    }
                }

                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(IsExportable)
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var schedule in all)
                {
                    var item = Read(schedule);
                    if (sourceByName.TryGetValue(Normalize(schedule.Name), out var codes))
                        item["plugin_sources"] = new JArray(codes);
                    schedules.Add(item);
                }
            }

            root["schedule_count"] = schedules.Count;
            root["schedules"] = schedules;
            return root;
        }

        static JObject Read(ViewSchedule schedule)
        {
            var item = new JObject
            {
                ["name"] = schedule.Name,
                ["element_id"] = schedule.Id.Value
            };

            try
            {
                item["is_key_schedule"] = schedule.Definition?.IsKeySchedule ?? false;
            }
            catch
            {
                // у части ведомостей Definition недоступен
            }

            TableData td;
            try { td = schedule.GetTableData(); }
            catch (Exception ex)
            {
                item["error"] = ex.Message;
                return item;
            }

            var body = td?.GetSectionData(SectionType.Body);
            if (body == null || body.NumberOfRows <= 0 || body.NumberOfColumns <= 0)
            {
                item["headers"] = new JArray();
                item["rows"] = new JArray();
                return item;
            }

            var columns = body.NumberOfColumns;
            var headers = new JArray();
            for (var c = 0; c < columns; c++)
                headers.Add(Cell(schedule, 0, c));

            var rows = new JArray();
            for (var r = 1; r < body.NumberOfRows; r++)
            {
                var row = new JArray();
                for (var c = 0; c < columns; c++)
                    row.Add(Cell(schedule, r, c));
                rows.Add(row);
            }

            item["headers"] = headers;
            item["rows"] = rows;
            return item;
        }

        static string Cell(ViewSchedule schedule, int row, int col)
        {
            try
            {
                return (schedule.GetCellText(SectionType.Body, row, col) ?? string.Empty).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        static bool IsExportable(ViewSchedule schedule)
        {
            if (schedule == null || schedule.IsTemplate)
                return false;
            if (schedule.IsTitleblockRevisionSchedule || schedule.IsInternalKeynoteSchedule)
                return false;

            ElementId categoryId;
            try { categoryId = schedule.Definition?.CategoryId; }
            catch { return true; }
            if (categoryId == null)
                return true;
            return categoryId != new ElementId(BuiltInCategory.OST_Sheets)
                   && categoryId != new ElementId(BuiltInCategory.OST_Revisions)
                   && categoryId != new ElementId(BuiltInCategory.OST_Views);
        }

        static string Normalize(string name) =>
            string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : name.Trim().Replace("<", string.Empty).Replace(">", string.Empty).Trim().TrimEnd('.');
    }
}
