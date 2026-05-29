using Autodesk.Revit.DB;

namespace SmartRemont.ExportRooms.DTO
{
    public class BoundingBoxDto
    {
        public XYZ Min { get; set; }
        public XYZ Max { get; set; }
    }
}

