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

        /// <summary>Эталонный пакет подбора заявки: строки ТК сверялись с ним. null — сверка с подбором заявки.</summary>
        [JsonProperty("etalon")]
        public ClientMaterialEtalonDto Etalon { get; set; }

        /// <summary>Чего нет в ТК: пары «комната + конструктив» эталона (или подбора заявки), которых нет ни в одной строке ТК.</summary>
        [JsonProperty("missing")]
        public List<ClientMaterialMissingRowDto> Missing { get; set; } = new();
    }

    public class ClientMaterialEtalonDto
    {
        [JsonProperty("preset_kit_id")]
        public int? PresetKitId { get; set; }

        [JsonProperty("preset_kit_name")]
        public string PresetKitName { get; set; }
    }

    public class ClientMaterialMissingRowDto
    {
        [JsonProperty("room_id")]
        public int? RoomId { get; set; }

        [JsonProperty("room_name")]
        public string RoomName { get; set; }

        [JsonProperty("work_set_id")]
        public int? WorkSetId { get; set; }

        [JsonProperty("work_set_name")]
        public string WorkSetName { get; set; }

        /// <summary>etalon_new — нет и в подборе заявки (эталон дополнили позже); in_kit — есть в подборе заявки, но не в ТК.</summary>
        [JsonProperty("missing_kind")]
        public string MissingKind { get; set; }

        [JsonProperty("compare_kit_id")]
        public int? CompareKitId { get; set; }

        [JsonProperty("kit_material_id")]
        public int? KitMaterialId { get; set; }

        [JsonProperty("kit_material_set_id")]
        public int? KitMaterialSetId { get; set; }

        [JsonProperty("kit_material_name")]
        public string KitMaterialName { get; set; }
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

        /// <summary>Подбор, с которым сверили строку: эталон или подбор заявки.</summary>
        [JsonProperty("compare_kit_id")]
        public int? CompareKitId { get; set; }

        /// <summary>true — у подбора есть эталон, но в нём нет пары «комната + конструктив» строки, сверили с подбором заявки.</summary>
        [JsonProperty("etalon_missing")]
        public bool? EtalonMissing { get; set; }

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
