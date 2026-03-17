using System.Collections.Generic;

namespace SBS.DTO
{
    public class SmartRemontRoomsExportDto
    {
        public string GeneratedAt { get; set; }
        public List<SmartRemontRoomDto> Rooms { get; set; }
    }

    public class SmartRemontRoomDto
    {
        public int RevitId { get; set; }
        public string UniqueId { get; set; }
        public string ApartmentNumber { get; set; }
        public string Number { get; set; }
        public string Name { get; set; }
        public string LevelName { get; set; }
        public double AreaM2 { get; set; }
        public double PerimeterM { get; set; }
        public double HeightM { get; set; }

        // Список списков: первый - внешние стены, остальные - колонны внутри
        public List<List<SmartRemontRoomPointDto>> Contours { get; set; } 
    }

    public class SmartRemontRoomPointDto
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}