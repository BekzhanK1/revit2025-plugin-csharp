using System;
using System.Collections.Generic;

namespace SmartRemont.ExportSpecifications.DTO
{
    public class ScheduleExportRootDto
    {
        public DateTime GeneratedAt { get; set; }
        public List<ScheduleExportDto> Schedules { get; set; } = new();
    }

    public class ScheduleExportDto
    {
        public string Name { get; set; }
        public List<string> Headers { get; set; } = new();
        public List<Dictionary<string, string>> Rows { get; set; } = new();
    }
}
