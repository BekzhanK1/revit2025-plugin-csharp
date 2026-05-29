using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public static class RevitEventTypes
    {
        public const string DsAreaChange = "DS_AREA_CHANGE";
    }

    public class RevitEventCreateRequest
    {
        [JsonProperty("remont_id")]
        public int RemontId { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("payload")]
        public DsAreaChangePayloadDto Payload { get; set; }
    }

    public class DsAreaChangePayloadDto
    {
        [JsonProperty("source")]
        public string Source { get; set; } = "revit";

        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("rooms")]
        public List<RemontRoomAreaDto> Rooms { get; set; } = new();
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
}
