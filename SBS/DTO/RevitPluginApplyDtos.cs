using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    // GET /revit/plugin/measures/read/?client_request_id=

    public class MeasureRoomInfoDto
    {
        [JsonProperty("room_id")]
        public int RoomId { get; set; }

        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("planirovka_room_id")]
        public int PlanirovkaRoomId { get; set; }

        [JsonProperty("planirovka_name")]
        public string PlanirovkaName { get; set; }

        [JsonProperty("is_measure_confirm")]
        public int IsMeasureConfirm { get; set; }

        [JsonProperty("parameters")]
        public List<MeasureApplyParamDto> CurrentParameters { get; set; }
    }

    public class MeasuresReadResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("client_request_id")]
        public int ClientRequestId { get; set; }

        [JsonProperty("data")]
        public List<MeasureRoomInfoDto> Data { get; set; } = new();
    }

    // POST /revit/plugin/measures/apply/

    public class MeasureApplyParamDto
    {
        [JsonProperty("param_code")]
        public string ParamCode { get; set; }

        [JsonProperty("param_value")]
        public string ParamValue { get; set; }
    }

    public class MeasureApplyRoomDto
    {
        [JsonProperty("room_id")]
        public int RoomId { get; set; }

        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("params")]
        public List<MeasureApplyParamDto> Params { get; set; } = new();
    }

    public class MeasuresApplyRequest
    {
        [JsonProperty("client_request_id")]
        public int ClientRequestId { get; set; }

        [JsonProperty("rooms")]
        public List<MeasureApplyRoomDto> Rooms { get; set; } = new();
    }

    public class ApplySkippedRoomDto
    {
        [JsonProperty("room_id")]
        public int? RoomId { get; set; }

        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }

    public class MeasuresApplyDataDto
    {
        [JsonProperty("applied_rooms")]
        public int AppliedRooms { get; set; }

        [JsonProperty("applied_params")]
        public int AppliedParams { get; set; }

        [JsonProperty("skipped")]
        public List<ApplySkippedRoomDto> Skipped { get; set; } = new();
    }

    public class MeasuresApplyResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("data")]
        public MeasuresApplyDataDto Data { get; set; }
    }

    // POST /revit/plugin/ds/room-change/apply/

    public class DsRoomChangeApplyRoomDto
    {
        [JsonProperty("room_id")]
        public int RoomId { get; set; }

        [JsonProperty("new_area")]
        public double NewArea { get; set; }
    }

    public class DsRoomChangeApplyRequest
    {
        [JsonProperty("client_request_id")]
        public int ClientRequestId { get; set; }

        [JsonProperty("wall_height")]
        public double? WallHeight { get; set; }

        [JsonProperty("rooms")]
        public List<DsRoomChangeApplyRoomDto> Rooms { get; set; } = new();
    }

    public class DsRoomChangeApplyDataDto
    {
        [JsonProperty("ds_id")]
        public int DsId { get; set; }

        [JsonProperty("remont_id")]
        public int? RemontId { get; set; }

        [JsonProperty("created")]
        public bool Created { get; set; }

        [JsonProperty("applied_rooms")]
        public int AppliedRooms { get; set; }

        [JsonProperty("skipped")]
        public List<ApplySkippedRoomDto> Skipped { get; set; } = new();

        [JsonProperty("wall_height_changed")]
        public bool WallHeightChanged { get; set; }
    }

    public class DsRoomChangeApplyResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("data")]
        public DsRoomChangeApplyDataDto Data { get; set; }
    }
}
