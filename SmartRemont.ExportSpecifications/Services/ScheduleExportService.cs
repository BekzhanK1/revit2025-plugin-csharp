using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using SmartRemont.ExportSpecifications.DTO;

namespace SmartRemont.ExportSpecifications.Services
{
    public static class ScheduleExportService
    {
        public static List<ViewSchedule> ListExportableSchedules(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(IsExportable)
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static ScheduleExportRootDto BuildExport(Document doc, IEnumerable<string> selectedNames)
        {
            var nameSet = new HashSet<string>(selectedNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var root = new ScheduleExportRootDto
            {
                GeneratedAt = DateTime.Now
            };

            if (nameSet.Count == 0)
                return root;

            var schedules = ListExportableSchedules(doc)
                .Where(s => nameSet.Contains(s.Name));

            foreach (var schedule in schedules)
                root.Schedules.Add(ReadSchedule(schedule));

            return root;
        }

        public static bool IsExportable(ViewSchedule schedule)
        {
            if (schedule == null) return false;
            if (schedule.IsTemplate) return false;
            if (schedule.IsTitleblockRevisionSchedule) return false;
            if (schedule.IsInternalKeynoteSchedule) return false;

            var defn = schedule.Definition;
            if (defn == null) return false;

            var categoryId = defn.CategoryId;
            if (categoryId == new ElementId(BuiltInCategory.OST_Sheets)) return false;
            if (categoryId == new ElementId(BuiltInCategory.OST_Revisions)) return false;
            if (categoryId == new ElementId(BuiltInCategory.OST_Views)) return false;

            return true;
        }

        static ScheduleExportDto ReadSchedule(ViewSchedule schedule)
        {
            var dto = new ScheduleExportDto { Name = schedule.Name };

            TableData td;
            try { td = schedule.GetTableData(); }
            catch { return dto; }

            if (td == null) return dto;

            var body = td.GetSectionData(SectionType.Body);
            if (body == null) return dto;

            int nRows = body.NumberOfRows;
            int nCols = body.NumberOfColumns;
            if (nRows <= 0 || nCols <= 0) return dto;

            const int headerRowIndex = 0;
            var headers = new List<string>(nCols);
            for (int c = 0; c < nCols; c++)
            {
                var header = (schedule.GetCellText(SectionType.Body, headerRowIndex, c) ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(header))
                    header = $"Column{c + 1}";
                headers.Add(header);
            }

            dto.Headers = headers;

            for (int r = 1; r < nRows; r++)
            {
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < nCols; c++)
                {
                    var key = headers[c];
                    var value = (schedule.GetCellText(SectionType.Body, r, c) ?? string.Empty).Trim();
                    // Keep first occurrence if duplicate headers collide
                    if (!row.ContainsKey(key))
                        row[key] = value;
                }

                dto.Rows.Add(row);
            }

            return dto;
        }
    }
}
