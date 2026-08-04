using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Text;

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

        public bool HasGrant(string grant)
        {
            if (string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(grant))
                return false;

            try
            {
                var parts = AccessToken.Split('.');
                if (parts.Length != 3) return false;

                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);
                if (data?.grants != null)
                {
                    foreach (var g in data.grants)
                    {
                        if (string.Equals(g.ToString(), grant, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false; // Grants array exists but doesn't contain the grant
                }
                
                // If grants array is missing from token, assume true to not block the UI
                return true;
            }
            catch
            {
                // ignore parsing errors
            }

            return true;
        }
    }
}
