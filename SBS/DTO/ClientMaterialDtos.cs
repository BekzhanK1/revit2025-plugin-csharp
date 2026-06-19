using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class ClientMaterialTkReadResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("remont_id")]
        public int? RemontId { get; set; }

        [JsonProperty("client_request_id")]
        public int? ClientRequestId { get; set; }

        [JsonProperty("data")]
        public List<ClientMaterialRowDto> Data { get; set; } = new();
    }

    public class ClientMaterialRowDto
    {
        [JsonProperty("client_material_id")]
        public int? ClientMaterialId { get; set; }

        [JsonProperty("room_id")]
        public int? RoomId { get; set; }

        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("work_set_id")]
        public int? WorkSetId { get; set; }

        [JsonProperty("work_set_name")]
        public string WorkSetName { get; set; }

        [JsonProperty("material_id")]
        public int? MaterialId { get; set; }

        [JsonProperty("material_name")]
        public string MaterialName { get; set; }

        [JsonProperty("material_set_id")]
        public int? MaterialSetId { get; set; }

        [JsonProperty("set_name")]
        public string SetName { get; set; }

        [JsonProperty("is_optional")]
        public int IsOptional { get; set; }
    }
}
