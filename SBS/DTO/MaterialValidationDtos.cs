using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class MaterialValidationRequest
    {
        [JsonProperty("material_ids")]
        public List<string> MaterialIds { get; set; } = new();
    }

    public class MaterialValidationResponse
    {
        [JsonProperty("data")]
        public MaterialValidationDataDto Data { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("status")]
        public bool Status { get; set; }
    }

    public class MaterialValidationDataDto
    {
        [JsonProperty("found_ids")]
        public List<string> FoundIds { get; set; } = new();
    }
}
