using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class RevitMaterialReadResponse
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
        public List<RevitMaterialRowDto> Data { get; set; } = new();
    }

    public class RevitMaterialRowDto
    {
        [JsonProperty("material_id")]
        public int? MaterialId { get; set; }

        [JsonProperty("material_name")]
        public string MaterialName { get; set; }

        [JsonProperty("material_type_id")]
        public int? MaterialTypeId { get; set; }

        [JsonProperty("material_type_code")]
        public string MaterialTypeCode { get; set; }

        [JsonProperty("revit_file_type")]
        public string RevitFileType { get; set; }

        [JsonProperty("revit_file_url")]
        public string RevitFileUrl { get; set; }

        [JsonProperty("revit_file_hash")]
        public string RevitFileHash { get; set; }

        [JsonProperty("revit_asset_name")]
        public string RevitAssetName { get; set; }
    }
}
