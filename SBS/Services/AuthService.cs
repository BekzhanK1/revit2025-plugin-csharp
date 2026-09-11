using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
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

        internal static async Task<bool> TryRefreshTokenCoreAsync(CancellationToken cancellationToken = default)
        {
            var session = ExportRoomsApplication.CurrentSession ?? AuthStorage.Load();
            if (session == null || string.IsNullOrWhiteSpace(session.RefreshToken))
                return false;

            var body = JsonConvert.SerializeObject(new { refresh = session.RefreshToken });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(Configs.AuthRefreshUrl, content, cancellationToken)
                .ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                ExportRoomsApplication._logger?.Warning(
                    "Token refresh failed: http={HttpStatus}, body={BodyPreview}",
                    (int)response.StatusCode,
                    Truncate(responseBody, 200));
                return false;
            }

            try
            {
                dynamic parsed = JsonConvert.DeserializeObject(responseBody);
                var access = parsed?.access?.ToString();
                var refresh = parsed?.refresh?.ToString();

                if (string.IsNullOrWhiteSpace(access))
                {
                    var loginResponse = JsonConvert.DeserializeObject<RevitLoginResponse>(responseBody);
                    access = loginResponse?.Token?.Access;
                    refresh = refresh ?? loginResponse?.Token?.Refresh;
                }

                if (string.IsNullOrWhiteSpace(access))
                    return false;

                session.AccessToken = access.Trim();
                if (!string.IsNullOrWhiteSpace(refresh))
                    session.RefreshToken = refresh.Trim();

                AuthStorage.Save(session);
                ExportRoomsApplication.CurrentSession = session;

                ExportRoomsApplication._logger?.Information("Token refresh succeeded");
                return true;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Token refresh response parse failed");
                return false;
            }
        }

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength) + "…";
        }
    }
}
