using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class DsRoomChangeReadResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("remont_id")]
        public int? RemontId { get; set; }

        [JsonProperty("client_request_id")]
        public int? ClientRequestId { get; set; }

        [JsonProperty("ds_id")]
        public int? DsId { get; set; }

        [JsonProperty("header")]
        public DsRoomChangeHeaderDto Header { get; set; }

        [JsonProperty("data")]
        public DsRoomChangeBodyDto Data { get; set; }
    }

    public class DsRoomChangeHeaderDto
    {
        [JsonProperty("is_accept")]
        public int? IsAccept { get; set; }

        [JsonProperty("is_send_sign")]
        public bool? IsSendSign { get; set; }

        [JsonProperty("card_id")]
        public int? CardId { get; set; }
    }

    public class DsRoomChangeBodyDto
    {
        [JsonProperty("data")]
        public List<DsRoomChangeRoomDto> Rooms { get; set; } = new();

        [JsonProperty("sum")]
        public DsSumDto Sum { get; set; }

        [JsonProperty("wall_height")]
        public double? WallHeight { get; set; }

        [JsonProperty("wall_height_new")]
        public double? WallHeightNew { get; set; }

        [JsonProperty("ds_info")]
        public DsRoomChangeInfoDto DsInfo { get; set; }

        [JsonProperty("header")]
        public DsRoomChangeHeaderDto Header { get; set; }
    }

    public class DsSumDto
    {
        [JsonProperty("ds_sum")]
        public double? DsSum { get; set; }

        [JsonProperty("material_diff")]
        public double? MaterialDiff { get; set; }

        [JsonProperty("work_diff")]
        public double? WorkDiff { get; set; }

        [JsonProperty("service_diff")]
        public double? ServiceDiff { get; set; }
    }

    public class DsRoomChangeRoomDto
    {
        [JsonProperty("ds_room_change_id")]
        public int? DsRoomChangeId { get; set; }

        [JsonProperty("room_id")]
        public int? RoomId { get; set; }

        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("room_area")]
        public double? RoomArea { get; set; }

        [JsonProperty("action_code")]
        public string ActionCode { get; set; }

        [JsonProperty("order_num")]
        public int? OrderNum { get; set; }

        [JsonProperty("prev_room_area")]
        public double? PrevRoomArea { get; set; }
    }

    public class DsRoomChangeInfoDto
    {
        [JsonProperty("ds_id")]
        public int? DsId { get; set; }

        [JsonProperty("ds_type_name")]
        public string DsTypeName { get; set; }

        [JsonProperty("ds_date")]
        public string DsDate { get; set; }
    }
}
