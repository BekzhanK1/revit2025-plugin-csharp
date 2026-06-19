using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    public enum TkMaterialCompareStatus
    {
        NotApplicable,
        InTk,
        NotInTk,
        TkOnly
    }

    public sealed class TkMaterialEntry
    {
        public string MaterialId { get; init; }
        public string DisplayName { get; init; }
        public string WorkSetName { get; init; }
        public bool IsSet { get; init; }
    }

    public static class TkMaterialCompareService
    {
        static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

        public static Dictionary<string, List<TkMaterialEntry>> BuildEntriesByRoomKey(
            IEnumerable<ClientMaterialRowDto> rows)
        {
            var map = new Dictionary<string, List<TkMaterialEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows ?? Enumerable.Empty<ClientMaterialRowDto>())
            {
                var entry = ToEntry(row);
                if (entry == null)
                    continue;

                var roomKey = DsAreaCompareService.GetRoomCompareKey(row.RoomName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(roomKey))
                    roomKey = "_unknown_";

                if (!map.TryGetValue(roomKey, out var list))
                {
                    list = new List<TkMaterialEntry>();
                    map[roomKey] = list;
                }

                if (!list.Any(e => string.Equals(e.MaterialId, entry.MaterialId, StringComparison.OrdinalIgnoreCase)))
                    list.Add(entry);
            }

            return map;
        }

        static TkMaterialEntry ToEntry(ClientMaterialRowDto row)
        {
            if (row == null)
                return null;

            if (row.MaterialId is > 0)
            {
                return new TkMaterialEntry
                {
                    MaterialId = row.MaterialId.Value.ToString(),
                    DisplayName = BuildDisplayName(row.MaterialName, row.WorkSetName),
                    WorkSetName = StripHtml(row.WorkSetName),
                    IsSet = false
                };
            }

            if (row.MaterialSetId is > 0)
            {
                return new TkMaterialEntry
                {
                    MaterialId = $"set:{row.MaterialSetId.Value}",
                    DisplayName = BuildDisplayName(row.SetName ?? row.MaterialName, row.WorkSetName),
                    WorkSetName = StripHtml(row.WorkSetName),
                    IsSet = true
                };
            }

            return null;
        }

        static string BuildDisplayName(string primary, string workSetName)
        {
            var name = StripHtml(primary);
            if (string.IsNullOrWhiteSpace(name))
                name = "Материал";

            var workSet = StripHtml(workSetName);
            return string.IsNullOrWhiteSpace(workSet) ? name : $"{workSet}: {name}";
        }

        public static string StripHtml(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase);
            text = HtmlTagRegex.Replace(text, string.Empty);
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        public static TkMaterialCompareStatus ResolveRevitRowStatus(
            string productId,
            IReadOnlyCollection<TkMaterialEntry> tkEntriesForRoom)
        {
            if (tkEntriesForRoom == null || tkEntriesForRoom.Count == 0)
                return TkMaterialCompareStatus.NotApplicable;

            if (!MaterialValidationService.IsNumericMaterialId(productId))
                return TkMaterialCompareStatus.NotApplicable;

            var tkIds = new HashSet<string>(
                tkEntriesForRoom
                    .Where(e => !e.IsSet)
                    .Select(e => e.MaterialId),
                StringComparer.OrdinalIgnoreCase);

            return tkIds.Contains(productId.Trim())
                ? TkMaterialCompareStatus.InTk
                : TkMaterialCompareStatus.NotInTk;
        }

        public static IEnumerable<TkMaterialEntry> GetTkOnlyEntries(
            IReadOnlyCollection<TkMaterialEntry> tkEntriesForRoom,
            IEnumerable<string> revitProductIds)
        {
            if (tkEntriesForRoom == null || tkEntriesForRoom.Count == 0)
                yield break;

            var revitIds = new HashSet<string>(
                (revitProductIds ?? Enumerable.Empty<string>())
                    .Where(MaterialValidationService.IsNumericMaterialId)
                    .Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in tkEntriesForRoom)
            {
                if (entry.IsSet)
                {
                    yield return entry;
                    continue;
                }

                if (!revitIds.Contains(entry.MaterialId))
                    yield return entry;
            }
        }
    }
}
