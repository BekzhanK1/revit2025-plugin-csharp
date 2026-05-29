using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class SmartRemontRoomsExportDto
    {
        public string GeneratedAt { get; set; }
        public List<SmartRemontRoomDto> Rooms { get; set; }
        public List<SmartRemontWorkItemDto> WorkItems { get; set; } = new();
    }

    public class SmartRemontRoomDto
    {
        public long   RevitId         { get; set; }
        public string UniqueId        { get; set; }
        public string ApartmentNumber { get; set; }
        public string Number          { get; set; }
        public string Name            { get; set; }
        public string LevelName       { get; set; }

        // ── Геометрические параметры ──────────────────────────────────────────
        /// <summary>Площадь пола, м²</summary>
        public double AreaM2      { get; set; }
        /// <summary>Периметр пола, м</summary>
        public double PerimeterM  { get; set; }
        /// <summary>Высота потолка, м</summary>
        public double HeightM     { get; set; }
        /// <summary>Площадь стен (брутто = периметр × высота), м²</summary>
        public double WallAreaM2  { get; set; }

        // ── Отделка (текстовые параметры из помещения) ────────────────────────
        public string FloorFinish   { get; set; }
        public string WallFinish    { get; set; }
        public string CeilingFinish { get; set; }
        public string Level         { get; set; }
        public string IfcGUID       { get; set; }

        // ── Реальные элементы отделки (заполняется при включённой опции) ──────
        /// <summary>Элементы полов, потолков и линейных изделий (плинтусы, молдинги).</summary>
        public List<SmartRemontFinishItemDto> Finishes { get; set; } = new();

        // ── Контур помещения ──────────────────────────────────────────────────
        /// <summary>Список контуров: первый — внешний, остальные — колонны/вырезы.</summary>
        public List<List<SmartRemontRoomPointDto>> Contours { get; set; }
    }

    /// <summary>Один элемент отделки, привязанный к помещению пространственно.</summary>
    public class SmartRemontFinishItemDto
    {
        /// <summary>"Floor" | "Ceiling" | "GenericModel"</summary>
        public string Category { get; set; }
        public string TypeName  { get; set; }
        public long   RevitId   { get; set; }
        /// <summary>Площадь элемента, м² (для полов и потолков)</summary>
        public double AreaM2    { get; set; }
        /// <summary>Длина элемента, м (для плинтусов, молдингов)</summary>
        public double LengthM   { get; set; }
    }

    public class SmartRemontRoomPointDto
    {
        public double X { get; set; }
        public double Y { get; set; }
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