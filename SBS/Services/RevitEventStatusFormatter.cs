using SmartRemont.ExportRooms.DTO;
using System;
using System.Globalization;

namespace SmartRemont.ExportRooms.Services
{
    public static class RevitEventStatusFormatter
    {
        public static string FormatBadgeText(RevitEventStatusDataDto status)
        {
            if (status == null || !status.HasEvent)
                return null;

            return status.IsImported == true ? "Применено" : "Отправлено";
        }

        public static string FormatBannerText(RevitEventStatusDataDto status)
        {
            if (status == null || !status.HasEvent)
                return null;

            var when = FormatCreatedAt(status.CreatedAt);
            var eventPart = status.EventId.HasValue ? $" · событие #{status.EventId.Value}" : "";

            if (status.IsImported == true)
            {
                return string.IsNullOrEmpty(when)
                    ? $"Данные уже отправлены и применены в MySpace{eventPart}"
                    : $"Данные уже отправлены и применены в MySpace · {when}{eventPart}";
            }

            return string.IsNullOrEmpty(when)
                ? $"Данные уже отправлены · ожидают применения в MySpace{eventPart}"
                : $"Данные уже отправлены · {when} · ожидают применения в MySpace{eventPart}";
        }

        public static string FormatSubtitleSuffix(RevitEventStatusDataDto status)
        {
            if (status == null || !status.HasEvent)
                return null;

            return status.IsImported == true
                ? "Уже применено в MySpace"
                : "Уже отправлено · ожидает применения";
        }

        static string FormatCreatedAt(string createdAt)
        {
            if (string.IsNullOrWhiteSpace(createdAt))
                return null;

            if (DateTime.TryParse(
                    createdAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var utc))
                return utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

            return null;
        }
    }
}
