using System.Collections.Generic;

namespace SmartRemont.ExportRooms.Models
{
    public class RoomMaterialsSnapshot
    {
        public List<RoomMaterialsRoomRow> Rooms { get; set; } = new();
        public int TotalElements { get; set; }
        public int ElementsWithCode { get; set; }
        public int UnassignedElements { get; set; }
        public int SkippedWithoutCode { get; set; }
        public int SkippedExcludedCategory { get; set; }
        public int OnlyAdskCodeCount { get; set; }
        public int OnlyClassifierCodeCount { get; set; }
        public int OnlyErboEomCodeCount { get; set; }
        public int BothCodesCount { get; set; }
        public int ConflictingCodesCount { get; set; }
        public RoomPaintSourceInfo PaintSource { get; set; }
    }

    public class RoomMaterialsRoomRow
    {
        public string RoomName { get; set; }
        public List<RoomMaterialItem> Items { get; set; } = new();
        public List<RoomPaintItem> PaintItems { get; set; } = new();
    }

    public class RoomMaterialItem
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public long? CategoryId { get; set; }
        public string AdskProductCode { get; set; }
        public string ClassificationCode { get; set; }
        public string ErboEomCode { get; set; }
        public string CodeSourceNote { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class RoomPaintItem
    {
        public string ProductId { get; set; }
        public string IdSourceNote { get; set; }
        public string MaterialType { get; set; }
        public double? AreaM2 { get; set; }
        public double? Liters { get; set; }
        public double? MassKg { get; set; }
    }

    public class RoomPaintScheduleResult
    {
        public List<RoomMaterialsRoomRow> Rooms { get; set; } = new();
        public RoomPaintSourceInfo Source { get; set; } = new();
    }

    public class RoomPaintSourceInfo
    {
        public string ScheduleNameExpected { get; set; }
        public string ScheduleNameFound { get; set; }
        public bool Found { get; set; }
        public string LitersFormula { get; set; }
        public string LitersFormulaNote { get; set; }
        public string Message { get; set; }
        public string DetailLines { get; set; }
    }
}
