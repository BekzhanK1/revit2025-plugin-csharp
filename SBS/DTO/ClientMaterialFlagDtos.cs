using Newtonsoft.Json;
using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    /// <summary>
    /// GET /revit/plugin/material/flags/ — метки строк ТК: неактуальность относительно подбора и наличие.
    /// </summary>
    public class ClientMaterialFlagsResponse
    {
        [JsonProperty("status")]
        public bool Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("client_request_id")]
        public int? ClientRequestId { get; set; }

        [JsonProperty("kit_checked")]
        public bool KitChecked { get; set; }

        /// <summary>replaced, work_set_removed, disabled, inactive, not_avail, no_city_provider, provider_refused → число строк ТК.</summary>
        [JsonProperty("summary")]
        public Dictionary<string, int> Summary { get; set; } = new();

        [JsonProperty("data")]
        public List<ClientMaterialFlagRowDto> Data { get; set; } = new();
    }

    public class ClientMaterialFlagRowDto
    {
        [JsonProperty("client_material_id")]
        public int ClientMaterialId { get; set; }

        [JsonProperty("room_id")]
        public int? RoomId { get; set; }

        [JsonProperty("work_set_id")]
        public int? WorkSetId { get; set; }

        [JsonProperty("material_id")]
        public int? MaterialId { get; set; }

        [JsonProperty("material_set_id")]
        public int? MaterialSetId { get; set; }

        /// <summary>null | ok | replaced | disabled | work_set_removed | ds</summary>
        [JsonProperty("kit_status")]
        public string KitStatus { get; set; }

        [JsonProperty("kit_material_id")]
        public int? KitMaterialId { get; set; }

        [JsonProperty("kit_material_name")]
        public string KitMaterialName { get; set; }

        /// <summary>ok | owned | inactive | not_avail | no_city_provider</summary>
        [JsonProperty("avail_status")]
        public string AvailStatus { get; set; }

        [JsonProperty("avail_items")]
        public List<ClientMaterialAvailItemDto> AvailItems { get; set; } = new();

        [JsonProperty("provider_refused")]
        public List<ClientMaterialProviderRefusalDto> ProviderRefused { get; set; } = new();
    }

    public class ClientMaterialAvailItemDto
    {
        [JsonProperty("material_id")]
        public int MaterialId { get; set; }

        [JsonProperty("material_name")]
        public string MaterialName { get; set; }

        [JsonProperty("avail_status")]
        public string AvailStatus { get; set; }
    }

    public class ClientMaterialProviderRefusalDto
    {
        [JsonProperty("material_id")]
        public int MaterialId { get; set; }

        [JsonProperty("material_name")]
        public string MaterialName { get; set; }

        [JsonProperty("provider_name")]
        public string ProviderName { get; set; }

        [JsonProperty("refused_at")]
        public string RefusedAt { get; set; }
    }
}
