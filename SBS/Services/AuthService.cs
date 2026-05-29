using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public static class AuthService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<AuthSession> LoginAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Укажите email");
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Укажите пароль");

            var requestBody = new RevitLoginRequest
            {
                Email = email.Trim(),
                Password = password
            };

            var json = JsonConvert.SerializeObject(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(Configs.AuthLoginUrl, content).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка авторизации ({(int)response.StatusCode})";
                throw new InvalidOperationException(message);
            }

            var loginResponse = JsonConvert.DeserializeObject<RevitLoginResponse>(responseBody);
            var session = AuthSession.FromResponse(loginResponse);
            if (session == null || !session.IsValid)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            AuthStorage.Save(session);
            ExportRoomsApplication.CurrentSession = session;
            return session;
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                dynamic error = JsonConvert.DeserializeObject(responseBody);
                if (error?.detail != null)
                    return error.detail.ToString();
                if (error?.message != null)
                    return error.message.ToString();
                if (error?.error != null)
                    return error.error.ToString();
            }
            catch
            {
                // ignore parse errors
            }

            return null;
        }

        public static AuthSession RestoreSession()
        {
            var session = AuthStorage.Load();
            ExportRoomsApplication.CurrentSession = session;
            return session;
        }

        public static void Logout()
        {
            AuthStorage.Clear();
            ExportRoomsApplication.CurrentSession = null;
            ExportRoomsApplication.SelectedRemont = null;
        }
    }
}
