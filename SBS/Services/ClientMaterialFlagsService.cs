using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>Тон метки: Bad — красная, Warn — жёлтая, Mute — серая.</summary>
    public enum MaterialFlagTone
    {
        None,
        Mute,
        Warn,
        Bad,
    }

    /// <summary>Сводка меток ТК по одному material_id для окна «Материалы Revit».</summary>
    public sealed class MaterialFlagInfo
    {
        public MaterialFlagTone Tone { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// Метки строк ТК (неактуальность относительно подбора, наличие).
    /// Только показ: замена материала делается в MySpace через ДС.
    /// </summary>
    public static class ClientMaterialFlagsService
    {
        public static async Task<(ClientMaterialFlagsResponse Data, bool Status, string Error)> TryReadAsync(int clientRequestId)
        {
            try
            {
                if (clientRequestId <= 0) return (null, false, "Не указан ID заявки");

                using var response = await AuthApiClient.SendAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, Configs.RevitMaterialFlagsUrl(clientRequestId))).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var error = TryReadErrorMessage(responseBody) ?? $"Ошибка запроса меток ТК ({(int)response.StatusCode})";
                    ExportRoomsApplication._logger?.Warning(
                        "Material flags read failed: client_request_id={ClientRequestId}, http={HttpStatus}, error={Error}",
                        clientRequestId,
                        (int)response.StatusCode,
                        error);
                    return (null, false, error);
                }

                var parsed = JsonConvert.DeserializeObject<ClientMaterialFlagsResponse>(responseBody);
                if (parsed == null) return (null, false, "Сервер вернул некорректный ответ");
                if (!parsed.Status) return (null, false, string.IsNullOrWhiteSpace(parsed.Error) ? "Ошибка запроса меток ТК" : parsed.Error);

                parsed.Data ??= new List<ClientMaterialFlagRowDto>();
                parsed.Summary ??= new Dictionary<string, int>();
                return (parsed, true, null);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Material flags read failed: client_request_id={ClientRequestId}", clientRequestId);
                return (null, false, ex.Message);
            }
        }

        public static int CountStale(ClientMaterialFlagsResponse flags) =>
            SummaryValue(flags, "replaced") + SummaryValue(flags, "disabled") + SummaryValue(flags, "work_set_removed");

        /// <summary>Строки, где проблема с наличием видна проектировщику (без «нет поставщика в городе»).</summary>
        public static int CountUnavailable(ClientMaterialFlagsResponse flags) =>
            SummaryValue(flags, "inactive") + SummaryValue(flags, "not_avail") + SummaryValue(flags, "provider_refused");

        /// <summary>Метки по material_id: голова строки ТК, элементы набора и отказы поставщиков.</summary>
        public static Dictionary<int, MaterialFlagInfo> BuildByMaterial(ClientMaterialFlagsResponse flags)
        {
            var parts = new Dictionary<int, List<(MaterialFlagTone Tone, string Text)>>();

            void Add(int? materialId, MaterialFlagTone tone, string text)
            {
                if (materialId is not int id || string.IsNullOrWhiteSpace(text)) return;
                if (!parts.TryGetValue(id, out var list))
                    parts[id] = list = new List<(MaterialFlagTone, string)>();
                if (!list.Any(p => p.Text == text))
                    list.Add((tone, text));
            }

            foreach (var row in flags?.Data ?? new List<ClientMaterialFlagRowDto>())
            {
                switch (row.KitStatus)
                {
                    case "replaced":
                        Add(row.MaterialId, MaterialFlagTone.Warn,
                            row.KitMaterialId.HasValue ? $"Заменён в подборе → {row.KitMaterialId}" : "Заменён в подборе");
                        break;
                    case "disabled":
                        Add(row.MaterialId, MaterialFlagTone.Warn, "Выключен в подборе");
                        break;
                    case "work_set_removed":
                        Add(row.MaterialId, MaterialFlagTone.Mute, "Конструктив убран из подбора");
                        break;
                }

                Add(row.MaterialId, AvailTone(row.AvailStatus), AvailText(row.AvailStatus));
                foreach (var item in row.AvailItems ?? new List<ClientMaterialAvailItemDto>())
                    Add(item.MaterialId, AvailTone(item.AvailStatus), AvailText(item.AvailStatus));
                foreach (var refusal in row.ProviderRefused ?? new List<ClientMaterialProviderRefusalDto>())
                    Add(refusal.MaterialId, MaterialFlagTone.Bad,
                        string.IsNullOrWhiteSpace(refusal.ProviderName) ? "Поставщик отказал" : $"Поставщик отказал: {refusal.ProviderName}");
            }

            return parts.ToDictionary(
                p => p.Key,
                p => new MaterialFlagInfo
                {
                    Tone = p.Value.Max(x => x.Tone),
                    Text = string.Join(" · ", p.Value.Select(x => x.Text)),
                });
        }

        static int SummaryValue(ClientMaterialFlagsResponse flags, string key) =>
            flags?.Summary != null && flags.Summary.TryGetValue(key, out var value) ? value : 0;

        static string AvailText(string status) => status switch
        {
            "inactive" => "Снят с продажи",
            "not_avail" => "Нет в наличии",
            "no_city_provider" => "Нет поставщика в городе",
            _ => null,
        };

        static MaterialFlagTone AvailTone(string status) => status switch
        {
            "inactive" => MaterialFlagTone.Bad,
            "not_avail" => MaterialFlagTone.Bad,
            "no_city_provider" => MaterialFlagTone.Mute,
            _ => MaterialFlagTone.None,
        };

        static string TryReadErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var parsed = JsonConvert.DeserializeObject<ClientMaterialFlagsResponse>(body);
                return string.IsNullOrWhiteSpace(parsed?.Error) ? null : parsed.Error;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
