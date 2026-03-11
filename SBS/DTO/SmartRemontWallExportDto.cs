using System.Collections.Generic;

namespace SBS.DTO
{
    public class SmartRemontApartmentDto
    {
        public string ApartmentNumber { get; set; }
        public int WallsCount { get; set; }
        public List<SmartRemontWallDto> Walls { get; set; }
    }

    public class SmartRemontWallDto
    {
        public int RevitId { get; set; }
        public string UniqueId { get; set; }
        public string WallType { get; set; }
        public WallDimensionsDto Dimensions { get; set; }
        public WallFinishesDto Finishes { get; set; }
    }

    public class WallDimensionsDto
    {
        public double AreaM2 { get; set; }
        public double LengthM { get; set; }
        public double HeightM { get; set; }
        public double ThicknessM { get; set; }
    }

    public class WallFinishesDto
    {
        public string Floor { get; set; }
        public string Walls { get; set; }
        public string Ceiling { get; set; }
        public string Baseboard { get; set; }
    }
}
