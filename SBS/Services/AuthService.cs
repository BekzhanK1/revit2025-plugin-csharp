using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
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

        /// <summary>
        /// Вход чужим access JWT. Только тестовый API: на проде метод не сохраняет сессию.
        /// Refresh-токена нет, пароль в Credential Manager не пишется.
        /// </summary>
        public static async Task<AuthSession> LoginWithAccessTokenAsync(string rawToken)
        {
            if (!Configs.UseTestApi || !Configs.IsTestApi)
                throw new InvalidOperationException("Вход по JWT доступен только на тестовом API.");

            var accessToken = NormalizeAccessToken(rawToken);
            var employeeId = ReadEmployeeId(accessToken);
            EnsureNotExpired(accessToken);
            await EnsureServerAcceptsTokenAsync(accessToken).ConfigureAwait(false);

            var session = new AuthSession
            {
                AccessToken = accessToken,
                User = new UserDto
                {
                    EmployeeId = employeeId,
                    Fio = "сотрудник #" + employeeId
                }
            };

            if (!session.IsValid)
                throw new InvalidOperationException("Не удалось собрать сессию из JWT.");

            AuthStorage.Save(session);
            ExportRoomsApplication.CurrentSession = session;
            ExportRoomsApplication._logger?.Information(
                "Test JWT login: employee_id={EmployeeId}, api={Api}",
                employeeId,
                Configs.ApiOriginUrl);
            return session;
        }

        static string NormalizeAccessToken(string rawToken)
        {
            var token = (rawToken ?? string.Empty).Trim().Trim('"');
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("Bearer ".Length).Trim();

            var parts = token.Split('.');
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                throw new InvalidOperationException("Это не JWT. Вставьте access-токен целиком.");

            return token;
        }

        static int ReadEmployeeId(string accessToken)
        {
            var payload = ReadPayload(accessToken);
            var raw = payload?["user_id"]?.ToString();
            if (!int.TryParse(raw, out var employeeId) || employeeId <= 0)
                throw new InvalidOperationException("В JWT нет user_id сотрудника.");

            return employeeId;
        }

        static void EnsureNotExpired(string accessToken)
        {
            var payload = ReadPayload(accessToken);
            var rawExp = payload?["exp"]?.ToString();
            if (!long.TryParse(rawExp, out var exp))
                return;

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (expiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("JWT истёк.");
        }

        static async Task EnsureServerAcceptsTokenAsync(string accessToken)
        {
            var body = JsonConvert.SerializeObject(new QuickSearchRequest { ClientRequestId = 1 });
            using var request = new HttpRequestMessage(HttpMethod.Post, Configs.QuickSearchUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Сервер не принял JWT. Нужен access-токен тестового API.");

            if ((int)response.StatusCode >= 500)
            {
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    TryReadErrorMessage(responseBody) ?? "Тестовый API не ответил на проверку JWT.");
            }
        }

        static Newtonsoft.Json.Linq.JObject ReadPayload(string accessToken)
        {
            try
            {
                var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2: payload += "=="; break;
                    case 3: payload += "="; break;
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return Newtonsoft.Json.Linq.JObject.Parse(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не удалось прочитать JWT.", ex);
            }
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
