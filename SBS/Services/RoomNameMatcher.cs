using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// «Кухня», «Кухня №2», «Спальня 2» → базовое имя «Кухня» / «Спальня».
    /// </summary>
    public static class RoomNameMatcher
    {
        static readonly Regex TrailingNumberRegex = new(
            @"^(.+?)\s+(?:№\s*)?(\d+)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string GetBaseName(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                return string.Empty;

            var trimmed = roomName.Trim();
            var numberSignIndex = trimmed.IndexOf('№');
            if (numberSignIndex > 0)
                return trimmed.Substring(0, numberSignIndex).TrimEnd();

            var m = TrailingNumberRegex.Match(trimmed);
            if (m.Success)
                return m.Groups[1].Value.TrimEnd();

            return trimmed;
        }

        public static bool MatchesBaseName(string roomName, string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
                return false;

            return string.Equals(
                GetBaseName(roomName),
                baseName.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesAnyBaseName(string roomName, IReadOnlyList<string> baseNames)
        {
            if (baseNames == null || baseNames.Count == 0)
                return true;

            foreach (var baseName in baseNames)
            {
                if (MatchesBaseName(roomName, baseName))
                    return true;
            }

            return false;
        }

        public static bool IsAllowedRoom(string roomName, IReadOnlyList<string> roomBaseNamesFilter) =>
            MatchesAnyBaseName(roomName, roomBaseNamesFilter);
    }
}
