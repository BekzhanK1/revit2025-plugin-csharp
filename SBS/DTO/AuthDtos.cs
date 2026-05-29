using Newtonsoft.Json;

namespace SmartRemont.ExportRooms.DTO
{
    public class RevitLoginRequest
    {
        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }
    }

    public class RevitLoginResponse
    {
        [JsonProperty("token")]
        public TokenDto Token { get; set; }

        [JsonProperty("user")]
        public UserDto User { get; set; }
    }

    public class TokenDto
    {
        [JsonProperty("access")]
        public string Access { get; set; }

        [JsonProperty("refresh")]
        public string Refresh { get; set; }
    }

    public class UserDto
    {
        [JsonProperty("employee_id")]
        public int? EmployeeId { get; set; }

        [JsonProperty("fio")]
        public string Fio { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }
    }
}
