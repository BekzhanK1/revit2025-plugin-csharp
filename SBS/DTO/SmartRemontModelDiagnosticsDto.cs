using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class SmartRemontModelDiagnosticsDto
    {
        public DiagnosticsMetaDto Meta { get; set; }
        public List<CategoryDiagnosticsDto> Categories { get; set; }
        public List<ScheduleDiagnosticsDto> Schedules { get; set; }
        public DataQualityDiagnosticsDto DataQuality { get; set; }
        public List<string> Recommendations { get; set; }
    }

    public class DiagnosticsMetaDto
    {
        public string GeneratedAt { get; set; }
        public string RevitVersion { get; set; }
        public string DocumentTitle { get; set; }
        public string DocumentPath { get; set; }
    }

    public class CategoryDiagnosticsDto
    {
        public string Category { get; set; }
        public int ElementsCount { get; set; }
        public List<ParameterCoverageDto> TopParameters { get; set; }
    }

    public class ParameterCoverageDto
    {
        public string Name { get; set; }
        public int PresentInElements { get; set; }
        public int FilledValues { get; set; }
        public List<string> SampleValues { get; set; }
    }

    public class ScheduleDiagnosticsDto
    {
        public string Name { get; set; }
        public int ColumnsCount { get; set; }
        public int RowsCount { get; set; }
        public List<string> Headers { get; set; }
        public List<ScheduleRowSampleDto> RowSamples { get; set; }
    }

    public class ScheduleRowSampleDto
    {
        public string RowType { get; set; }
        public Dictionary<string, string> Values { get; set; }
    }

    public class DataQualityDiagnosticsDto
    {
        public int TotalSchedules { get; set; }
        public int TotalScheduleRows { get; set; }
        public int HeaderLikeRows { get; set; }
        public int SectionLikeRows { get; set; }
        public int TotalLikeRows { get; set; }
        public int ItemLikeRows { get; set; }
    }
}
