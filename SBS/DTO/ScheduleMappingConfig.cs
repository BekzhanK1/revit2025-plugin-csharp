using System.Collections.Generic;

namespace SBS.DTO
{
    public class ScheduleMappingConfig
    {
        public List<ScheduleMapping> Schedules { get; set; } = new();
    }

    public class ScheduleMapping
    {
        public string ScheduleName { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>
        /// SR-раздел, например: Floors, Ceilings, WallPaint, Wallpaper, FloorTile, WallTile,
        /// Baseboard, Molding, Adhesives, Grout, Primer, Doors, Windows, Electrical, Plumbing
        /// </summary>
        public string Discipline { get; set; }

        /// <summary>Дополнительная метка типа работ (может быть пустой).</summary>
        public string WorkType { get; set; }

        public string ColMaterialName { get; set; }
        public string ColMaterialCode { get; set; }
        public string ColQuantity { get; set; }
        public string ColUnit { get; set; }
        public string ColRoomName { get; set; }
        public string ColRoomNumber { get; set; }
        public string ColApartment { get; set; }
    }
}

