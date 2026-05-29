using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class RemontRoomsJsonDto
    {
        [JsonProperty("rooms")]
        public List<RemontRoomAreaDto> Rooms { get; set; } = new();
    }

    public class RemontRoomAreaDto
    {
        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("room_area_m2")]
        public double RoomAreaM2 { get; set; }
    }
}
