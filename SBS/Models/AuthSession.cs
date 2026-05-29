using SmartRemont.ExportRooms.DTO;

namespace SmartRemont.ExportRooms.Models
{
    public class AuthSession
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public UserDto User { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(User?.Fio))
                    return User.Fio.Trim();
                if (!string.IsNullOrWhiteSpace(User?.Email))
                    return User.Email.Trim();
                return "пользователь";
            }
        }

        public static AuthSession FromResponse(RevitLoginResponse response)
        {
            return new AuthSession
            {
                AccessToken = response?.Token?.Access,
                RefreshToken = response?.Token?.Refresh,
                User = response?.User
            };
        }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(AccessToken) && User != null;
    }
}
