using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public static class RevitEventTypes
    {
        public const string DsAreaChange = "DS_AREA_CHANGE";
        public const string Measures = "MEASURES";
    }

    public class RevitEventCreateRequest
    {
        [JsonProperty("remont_id")]
        public int RemontId { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("payload")]
        public object Payload { get; set; }
    }

    public class DsAreaChangePayloadDto
    {
        [JsonProperty("source")]
        public string Source { get; set; } = "revit";

        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("wall_height")]
        public double WallHeight { get; set; }

        [JsonProperty("rooms")]
        public List<RemontRoomAreaDto> Rooms { get; set; } = new();
    }

    public class MeasuresPayloadDto
    {
        [JsonProperty("source")]
        public string Source { get; set; } = "revit";

        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("rooms")]
        public List<MeasuresRoomDto> Rooms { get; set; } = new();
    }

    public class MeasuresRoomDto
    {
        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("parameters")]
        public List<MeasureParamDto> Parameters { get; set; } = new();
    }

    public class MeasureParamDto
    {
        [JsonProperty("param_code")]
        public string ParamCode { get; set; }

        [JsonProperty("param_name")]
        public string ParamName { get; set; }

        [JsonProperty("param_value")]
        public double? ParamValue { get; set; }
    }

    public class RevitEventCreateResponse
    {
        [JsonProperty("data")]
        public RevitEventCreateDataDto Data { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("status")]
        public bool Status { get; set; }
    }

    public class RevitEventCreateDataDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("remont_id")]
        public int RemontId { get; set; }

        [JsonProperty("event_type_code")]
        public string EventTypeCode { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }
    }

    public class RevitEventStatusResponse
    {
        [JsonProperty("data")]
        public RevitEventStatusDataDto Data { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("status")]
        public bool Status { get; set; }
    }

    public class RevitEventStatusDataDto
    {
        [JsonProperty("has_event")]
        public bool HasEvent { get; set; }

        [JsonProperty("event_type_code")]
        public string EventTypeCode { get; set; }

        [JsonProperty("event_id")]
        public int? EventId { get; set; }

        [JsonProperty("is_imported")]
        public bool? IsImported { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }
    }
}
