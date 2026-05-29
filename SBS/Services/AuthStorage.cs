using Newtonsoft.Json;
using SmartRemont.ExportRooms.Models;
using System;
using System.IO;

namespace SmartRemont.ExportRooms.Services
{
    public static class AuthStorage
    {
        const string SessionFileName = "auth.session.json";

        static string SessionFilePath =>
            Path.Combine(ExportRoomsApplication._path ?? AppDomain.CurrentDomain.BaseDirectory, SessionFileName);

        public static AuthSession Load()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                    return null;

                var json = File.ReadAllText(SessionFilePath);
                var session = JsonConvert.DeserializeObject<AuthSession>(json);
                return session != null && session.IsValid ? session : null;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось загрузить сессию авторизации");
                return null;
            }
        }

        public static void Save(AuthSession session)
        {
            if (session == null || !session.IsValid)
                throw new ArgumentException("Некорректная сессия для сохранения");

            var directory = Path.GetDirectoryName(SessionFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(SessionFilePath, json);
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось удалить файл сессии");
            }
        }
    }
}
