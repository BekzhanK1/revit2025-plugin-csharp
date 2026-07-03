using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public static class ProjectRemontBindingService
    {
        public sealed class BindResult
        {
            public bool Bound { get; init; }
            public ProjectRemontMetadata Metadata { get; init; }
            public RemontOption Remont { get; init; }
        }

        public static BindResult TryBindFromDocument(Document doc)
        {
            var metadata = ProjectRemontMetadataService.TryRead(doc);
            if (metadata == null || metadata.RemontId <= 0)
                return new BindResult { Bound = false };

            var remont = CreateRemontOptionFromMetadata(metadata);
            ExportRoomsApplication.SelectedRemont = remont;

            ExportRoomsApplication._logger?.Information(
                "Auto-bound remont from document storage: remont_id={RemontId}, client_request_id={ClientRequestId}",
                metadata.RemontId,
                metadata.ClientRequestId);

            return new BindResult { Bound = true, Metadata = metadata, Remont = remont };
        }

        public static async Task TryEnrichFromQuickSearchAsync(RemontOption remont)
        {
            if (remont?.RemontId is not int remontId || remontId <= 0)
                return;

            try
            {
                var results = await RemontService.QuickSearchAsync(byRemontId: true, remontId)
                    .ConfigureAwait(false);
                var match = FindBestMatch(results, remont);
                if (match == null)
                    return;

                ApplyEnrichment(remont, match);
                ExportRoomsApplication._logger?.Information(
                    "Enriched bound remont from quick_search: remont_id={RemontId}", remontId);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex,
                    "Could not enrich bound remont from quick_search: remont_id={RemontId}", remontId);
            }
        }

        static RemontOption CreateRemontOptionFromMetadata(ProjectRemontMetadata metadata) =>
            new RemontOption
            {
                RemontId = metadata.RemontId,
                ClientRequestId = metadata.ClientRequestId,
                Name = $"Ремонт #{metadata.RemontId}"
            };

        static RemontOption FindBestMatch(IReadOnlyList<RemontOption> results, RemontOption bound)
        {
            if (results == null || results.Count == 0)
                return null;

            if (bound.ClientRequestId > 0)
            {
                foreach (var item in results)
                {
                    if (item.RemontId == bound.RemontId && item.ClientRequestId == bound.ClientRequestId)
                        return item;
                }
            }

            foreach (var item in results)
            {
                if (item.RemontId == bound.RemontId)
                    return item;
            }

            return results.Count == 1 ? results[0] : null;
        }

        static void ApplyEnrichment(RemontOption target, RemontOption source)
        {
            target.Name = source.Name;
            target.ClientName = source.ClientName;
            target.ResidentName = source.ResidentName;
            target.FlatNum = source.FlatNum;
            target.PresetName = source.PresetName;
            if (source.ClientRequestId > 0)
                target.ClientRequestId = source.ClientRequestId;
        }
    }
}
