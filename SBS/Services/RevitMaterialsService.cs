using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public static class RevitMaterialsService
    {
        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<RevitMaterialReadResponse> ReadAsync(int clientRequestId)
        {
            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            if (clientRequestId <= 0)
                throw new InvalidOperationException("Не указан ID заявки");

            var url = Configs.RevitMaterialReadUrl(clientRequestId);
            ExportRoomsApplication._logger?.Information(
                "Materials API read start: client_request_id={ClientRequestId}, url={Url}",
                clientRequestId,
                url);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            ExportRoomsApplication._logger?.Information(
                "Materials API read response: client_request_id={ClientRequestId}, http={HttpStatus}, body_length={BodyLength}",
                clientRequestId,
                (int)response.StatusCode,
                responseBody?.Length ?? 0);

            if (!response.IsSuccessStatusCode)
            {
                ExportRoomsApplication._logger?.Warning(
                    "Materials API read failed: client_request_id={ClientRequestId}, http={HttpStatus}, body_preview={BodyPreview}",
                    clientRequestId,
                    (int)response.StatusCode,
                    Truncate(responseBody, 500));

                var message = TryReadErrorMessage(responseBody)
                    ?? $"Ошибка запроса материалов ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<RevitMaterialReadResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ");

            if (!parsed.Status)
            {
                ExportRoomsApplication._logger?.Warning(
                    "Materials API returned status=false: client_request_id={ClientRequestId}, error={Error}",
                    clientRequestId,
                    parsed.Error ?? "—");
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка запроса материалов" : parsed.Error);
            }

            parsed.Data ??= new List<RevitMaterialRowDto>();

            var withUrl = 0;
            var withoutUrl = 0;
            var withoutId = 0;
            foreach (var row in parsed.Data)
            {
                if (row == null || !row.MaterialId.HasValue)
                {
                    withoutId++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row.RevitFileUrl))
                    withoutUrl++;
                else
                    withUrl++;
            }

            ExportRoomsApplication._logger?.Information(
                "Materials API parsed: client_request_id={ClientRequestId}, remont_id={RemontId}, rows={RowCount}, with_url={WithUrl}, without_url={WithoutUrl}, without_id={WithoutId}, surfaces_url={HasSurfacesUrl}, surfaces_hash={SurfacesHash}",
                clientRequestId,
                parsed.RemontId,
                parsed.Data.Count,
                withUrl,
                withoutUrl,
                withoutId,
                !string.IsNullOrWhiteSpace(parsed.SurfacesFileUrl),
                string.IsNullOrWhiteSpace(parsed.SurfacesFileHash) ? "—" : parsed.SurfacesFileHash.Trim());

            foreach (var row in parsed.Data)
            {
                if (row == null)
                    continue;

                ExportRoomsApplication._logger?.Debug(
                    "Materials API row: material_id={MaterialId}, type={RevitFileType}, type_code={TypeCode}, has_url={HasUrl}, asset={Asset}, name={Name}",
                    row.MaterialId,
                    row.RevitFileType ?? "—",
                    row.MaterialTypeCode ?? "—",
                    !string.IsNullOrWhiteSpace(row.RevitFileUrl),
                    row.RevitAssetName ?? "—",
                    row.MaterialName ?? "—");
            }

            return parsed;
        }

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;
            return value.Substring(0, maxLength) + "...";
        }

        public static async Task<(RevitMaterialReadResponse Data, bool Status, string Error)> TryReadAsync(int clientRequestId)
        {
            try
            {
                if (clientRequestId <= 0) return (null, false, "Не указан ID заявки");
                var session = ExportRoomsApplication.CurrentSession;
                if (session == null || string.IsNullOrWhiteSpace(session.AccessToken)) return (null, false, "Требуется авторизация");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Get, Configs.RevitMaterialReadUrl(clientRequestId));
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

                using var response = await Http.SendAsync(httpRequest).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var message = TryReadErrorMessage(responseBody) ?? $"Ошибка запроса материалов ({(int)response.StatusCode})";
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) message = "Сессия истекла. Выйдите и войдите снова.";
                    return (null, false, message);
                }

                var parsed = JsonConvert.DeserializeObject<RevitMaterialReadResponse>(responseBody);
                if (parsed == null) return (null, false, "Сервер вернул некорректный ответ");
                if (!parsed.Status) return (null, false, string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка запроса материалов" : parsed.Error);

                parsed.Data ??= new List<RevitMaterialRowDto>();
                return (parsed, true, null);
            }
            catch (Exception ex)
            {
                return (null, false, ex.Message);
            }
        }

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var error = JsonConvert.DeserializeObject<RevitMaterialReadResponse>(responseBody);
                if (!string.IsNullOrWhiteSpace(error?.Error))
                    return error.Error;

                dynamic raw = JsonConvert.DeserializeObject(responseBody);
                if (raw?.error != null)
                    return raw.error.ToString();
                if (raw?.detail != null)
                    return raw.detail.ToString();
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}
