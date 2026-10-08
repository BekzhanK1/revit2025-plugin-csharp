using Newtonsoft.Json;

namespace SmartRemont.ExportRooms.DTO
{
    public class ProjectTemplateCheckResponse
    {
        [JsonProperty("status")]
        public bool? Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("grade_id")]
        public int GradeId { get; set; }

        [JsonProperty("is_current")]
        public bool IsCurrent { get; set; }

        [JsonProperty("template_name")]
        public string TemplateName { get; set; }

        [JsonProperty("version_name")]
        public string VersionName { get; set; }

        [JsonProperty("file_name")]
        public string FileName { get; set; }

        [JsonProperty("file_url")]
        public string FileUrl { get; set; }

        [JsonProperty("file_hash")]
        public string FileHash { get; set; }

        [JsonProperty("file_size_bytes")]
        public long? FileSizeBytes { get; set; }
    }
}
