using System.Collections.Generic;

namespace SBS.DTO
{
    public class SmartRemontScheduleExportDto
    {
        public string GeneratedAt { get; set; }
        public List<ScheduleSummaryDto> SourceSchedules { get; set; }
        public List<SmartRemontScheduleApartmentDto> Apartments { get; set; }
        public List<SmartRemontWorkItemDto> UnmappedWorkItems { get; set; }
    }

    public class ScheduleSummaryDto
    {
        public string Name { get; set; }
        public int RowsCount { get; set; }
    }

    public class SmartRemontScheduleApartmentDto
    {
        public string ApartmentNumber { get; set; }
        public List<SmartRemontScheduleRoomDto> Rooms { get; set; }
    }

    public class SmartRemontScheduleRoomDto
    {
        public string RoomKey { get; set; }
        public List<SmartRemontWorkItemDto> WorkItems { get; set; }
    }

    public class SmartRemontWorkItemDto
    {
        public string SourceSchedule { get; set; }
        public string Discipline { get; set; }
        public string WorkType { get; set; }
        public string MaterialCode { get; set; }
        public string MaterialName { get; set; }
        public double? Quantity { get; set; }
        public string Unit { get; set; }
        public int? RoomRevitId { get; set; }
        public string RoomUniqueId { get; set; }
        public string RoomNumber { get; set; }
        public string RoomName { get; set; }
        public string RoomLevelName { get; set; }
        public Dictionary<string, string> RawValues { get; set; }
    }
}
