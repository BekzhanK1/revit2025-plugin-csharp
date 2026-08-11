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
            if (metadata == null || metadata.ClientRequestId <= 0)
                return new BindResult { Bound = false };

            var remont = CreateRemontOptionFromMetadata(metadata);
            ExportRoomsApplication.SelectedRemont = remont;

            ExportRoomsApplication._logger?.Information(
                "Auto-bound remont from document storage: client_request_id={ClientRequestId}, remont_id={RemontId}",
                metadata.ClientRequestId,
                metadata.RemontId);

            return new BindResult { Bound = true, Metadata = metadata, Remont = remont };
        }

        public static async Task<(bool Ok, string Error)> TryEnrichFromQuickSearchAsync(RemontOption remont)
        {
            if (remont == null || remont.ClientRequestId <= 0)
                return (false, "Не указан ID заявки");

            var clientRequestId = remont.ClientRequestId;

            try
            {
                var results = await RemontService.QuickSearchAsync(byRemontId: false, clientRequestId)
                    .ConfigureAwait(false);
                var match = FindBestMatch(results, remont);
                if (match == null)
                {
                    var msg =
                        $"Quick search не нашёл заявку #{clientRequestId} на {Configs.ApiOriginUrl}."
                        + " Проверьте apiOriginUrl (testapi vs prod) и права OA__RemontFormQuickSearch.";
                    ExportRoomsApplication._logger?.Warning(
                        "Enrich quick_search: no match for client_request_id={ClientRequestId}, api={Api}",
                        clientRequestId,
                        Configs.ApiOriginUrl);
                    return (false, msg);
                }

                ApplyEnrichment(remont, match);
                ExportRoomsApplication._logger?.Information(
                    "Enriched bound remont from quick_search: client_request_id={ClientRequestId}, api={Api}",
                    clientRequestId,
                    Configs.ApiOriginUrl);
                return (true, null);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex,
                    "Could not enrich bound remont from quick_search: client_request_id={ClientRequestId}, api={Api}",
                    clientRequestId,
                    Configs.ApiOriginUrl);
                return (false, ex.Message + $"\nAPI: {Configs.ApiOriginUrl}");
            }
        }

        static RemontOption CreateRemontOptionFromMetadata(ProjectRemontMetadata metadata) =>
            new RemontOption
            {
                RemontId = metadata.RemontId > 0 ? metadata.RemontId : null,
                ClientRequestId = metadata.ClientRequestId,
                Name = metadata.RemontId > 0
                    ? $"Ремонт #{metadata.RemontId}"
                    : $"Заявка #{metadata.ClientRequestId}"
            };

        static RemontOption FindBestMatch(IReadOnlyList<RemontOption> results, RemontOption bound)
        {
            if (results == null || results.Count == 0)
                return null;

            foreach (var item in results)
            {
                if (item.ClientRequestId == bound.ClientRequestId)
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
            target.PresetKitName = source.PresetKitName;
            target.ProjectAccepted = source.ProjectAccepted;
            if (source.RemontId is > 0)
                target.RemontId = source.RemontId;
            if (source.ClientRequestId > 0)
                target.ClientRequestId = source.ClientRequestId;
        }
    }
}
