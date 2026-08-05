using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;

namespace SmartRemont.ExportRooms.Services
{
    public class SavedCredentials
    {
        public string Email { get; set; }
        public string EncodedPassword { get; set; }
    }

    public static class CredentialManager
    {
        const string CredsFileName = "auth.credentials.json";

        static string CredsFilePath =>
            Path.Combine(ExportRoomsApplication._path ?? AppDomain.CurrentDomain.BaseDirectory, CredsFileName);

        public static void SaveCredentials(string email, string password)
        {
            try
            {
                var creds = new SavedCredentials
                {
                    Email = email,
                    EncodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(password))
                };

                var directory = Path.GetDirectoryName(CredsFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(CredsFilePath, JsonConvert.SerializeObject(creds));
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось сохранить учётные данные");
            }
        }

        public static (string email, string password) LoadCredentials()
        {
            try
            {
                if (!File.Exists(CredsFilePath))
                    return (null, null);

                var creds = JsonConvert.DeserializeObject<SavedCredentials>(File.ReadAllText(CredsFilePath));
                if (creds == null || string.IsNullOrEmpty(creds.EncodedPassword))
                    return (creds?.Email, null);

                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(creds.EncodedPassword));
                return (creds.Email, decoded);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось загрузить учётные данные");
                return (null, null);
            }
        }
    }
}
