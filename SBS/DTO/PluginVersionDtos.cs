using Newtonsoft.Json;

namespace SmartRemont.ExportRooms.DTO
{
    public class PluginVersionCheckResponse
    {
        [JsonProperty("status")]
        public bool? Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("client_version")]
        public string ClientVersion { get; set; }

        [JsonProperty("latest_version_name")]
        public string LatestVersionName { get; set; }

        [JsonProperty("latest_file_url")]
        public string LatestFileUrl { get; set; }

        [JsonProperty("min_supported_version_name")]
        public string MinSupportedVersionName { get; set; }

        [JsonProperty("is_supported")]
        public bool? IsSupported { get; set; }

        [JsonProperty("update_available")]
        public bool? UpdateAvailable { get; set; }
    }
}
