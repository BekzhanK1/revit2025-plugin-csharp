using System.Collections.Generic;

namespace SmartRemont.ExportRooms.Models
{
    public class RoomMeasurementsSnapshot
    {
        public List<RoomMeasurementsRoomRow> Rooms { get; set; } = new();
        public List<RoomMeasurementSourceInfo> Sources { get; set; } = new();
    }

    public class RoomMeasurementsRoomRow
    {
        public string RoomName { get; set; }
        public List<RoomMeasurementParamItem> Parameters { get; set; } = new();
    }

    public class RoomMeasurementParamItem
    {
        public string param_code { get; set; }
        public string param_name { get; set; }
        public double? param_value { get; set; }
    }

    public class RoomMeasurementSourceInfo
    {
        public string param_code { get; set; }
        public string param_name { get; set; }
        public string schedule_name_expected { get; set; }
        public string schedule_name_found { get; set; }
        public bool Found { get; set; }
        public string Message { get; set; }
    }
}
