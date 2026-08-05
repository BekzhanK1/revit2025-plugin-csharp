using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class QuickSearchRequest
    {
        [JsonProperty("client_request_id", NullValueHandling = NullValueHandling.Ignore)]
        public int? ClientRequestId { get; set; }

        [JsonProperty("remont_id", NullValueHandling = NullValueHandling.Ignore)]
        public int? RemontId { get; set; }
    }

    public class QuickSearchResponse
    {
        [JsonProperty("data")]
        public List<QuickSearchItemDto> Data { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("status")]
        public bool Status { get; set; }
    }

    public class QuickSearchItemDto
    {
        [JsonProperty("client_request_id")]
        public int ClientRequestId { get; set; }

        [JsonProperty("remont_id")]
        public int? RemontId { get; set; }

        [JsonProperty("client_name")]
        public string ClientName { get; set; }

        [JsonProperty("resident_name")]
        public string ResidentName { get; set; }

        [JsonProperty("flat_num")]
        public string FlatNum { get; set; }

        [JsonProperty("request_status_name")]
        public string RequestStatusName { get; set; }

        [JsonProperty("remont_status_name")]
        public string RemontStatusName { get; set; }

        [JsonProperty("project_accepted")]
        public int? ProjectAccepted { get; set; }

        [JsonProperty("remont_type")]
        public string RemontType { get; set; }

        [JsonProperty("preset_name")]
        public string PresetName { get; set; }

        [JsonProperty("preset_kit_name")]
        public string PresetKitName { get; set; }
    }
}
