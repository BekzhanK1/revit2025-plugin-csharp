using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class RevitMaterialsSyncProgress
    {
        public string Phase { get; init; }
        public int Done { get; init; }
        public int Total { get; init; }
        public string Message { get; init; }
    }

    public sealed class RevitMaterialSyncItemResult
    {
        public int MaterialId { get; init; }
        public bool Success { get; init; }
        public string ErrorMessage { get; init; }
        /// <summary>download | import | surface</summary>
        public string Phase { get; init; }
    }

    public sealed class RevitMaterialsSyncResult
    {
        public bool Success { get; init; }
        public int MaterialsLoaded { get; init; }
        public int ErrorCount { get; init; }
        public int TotalSyncable { get; init; }
        public string ErrorMessage { get; init; }
        public string SurfacesErrorMessage { get; init; }
        public IReadOnlyList<RevitMaterialSyncItemResult> Items { get; init; }
            = Array.Empty<RevitMaterialSyncItemResult>();
    }

    public static class RevitMaterialsSyncOrchestrator
    {
        public static async Task<RevitMaterialsSyncResult> SyncAllAsync(
            Document doc,
            int clientRequestId,
            string surfacesFileUrl,
            string surfacesFileHash,
            IProgress<RevitMaterialsSyncProgress> progress = null)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var response = await RevitMaterialsService.ReadAsync(clientRequestId).ConfigureAwait(true);
            var url = surfacesFileUrl ?? response.SurfacesFileUrl?.Trim();
            var hash = surfacesFileHash ?? response.SurfacesFileHash?.Trim();

            return await SyncAllAsync(
                doc,
                clientRequestId,
                response.Data,
                url,
                hash,
                progress).ConfigureAwait(true);
        }

        public static async Task<RevitMaterialsSyncResult> SyncAllAsync(
            Document doc,
            int clientRequestId,
            IEnumerable<RevitMaterialRowDto> materials,
            string surfacesFileUrl,
            string surfacesFileHash,
            IProgress<RevitMaterialsSyncProgress> progress = null)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (clientRequestId <= 0)
                throw new ArgumentOutOfRangeException(nameof(clientRequestId), "clientRequestId must be positive.");

            var materialList = (materials ?? Enumerable.Empty<RevitMaterialRowDto>()).ToList();

            var rfaRows = materialList
                .Where(r => r != null
                            && r.MaterialId.HasValue
                            && !IsSurfaceRow(r)
                            && !string.IsNullOrWhiteSpace(r.RevitFileUrl))
                .ToList();

            var surfaceRows = materialList
                .Where(r => r != null && r.MaterialId.HasValue && IsSurfaceRow(r))
                .ToList();

            if (rfaRows.Count == 0 && surfaceRows.Count == 0)
            {
                return new RevitMaterialsSyncResult
                {
                    Success = true,
                    ErrorMessage = "Нет файлов для синхронизации."
                };
            }

            var itemResults = new List<RevitMaterialSyncItemResult>();
            var downloadTotal = rfaRows.Count + (surfaceRows.Count > 0 ? 1 : 0);
            var downloadDone = 0;

            Report(progress, "download", downloadDone, downloadTotal, $"Скачивание: {downloadDone} из {downloadTotal}");

            var downloadProgress = new Progress<(int materialId, int done, int total, bool downloading)>(p =>
            {
                Report(progress, "download", p.done, downloadTotal,
                    $"Скачивание: {p.done} из {downloadTotal}");
            });

            var downloadResults = await RevitMaterialsDownloadService
                .SyncAsync(rfaRows, downloadProgress)
                .ConfigureAwait(true);

            foreach (var dr in downloadResults.Where(r => !r.Success))
            {
                itemResults.Add(new RevitMaterialSyncItemResult
                {
                    MaterialId = dr.MaterialId,
                    Success = false,
                    Phase = "download",
                    ErrorMessage = HumanizeError(dr.ErrorMessage) ?? "Не удалось скачать RFA"
                });
            }

            downloadDone = rfaRows.Count;

            string surfacesRvtPath = null;
            string surfacesErrorMessage = null;
            if (surfaceRows.Count > 0)
            {
                var surfacesDownload = await RevitMaterialsDownloadService
                    .EnsureSurfacesLibraryAsync(clientRequestId, surfacesFileUrl, surfacesFileHash)
                    .ConfigureAwait(true);

                downloadDone = downloadTotal;
                Report(progress, "download", downloadDone, downloadTotal, $"Скачивание: {downloadDone} из {downloadTotal}");

                if (surfacesDownload.Success)
                {
                    surfacesRvtPath = surfacesDownload.FilePath;
                }
                else
                {
                    surfacesErrorMessage = HumanizeError(surfacesDownload.ErrorMessage)
                                          ?? "Не удалось скачать surfaces.rvt";
                    foreach (var row in surfaceRows)
                    {
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = row.MaterialId.Value,
                            Success = false,
                            Phase = "surface",
                            ErrorMessage = surfacesErrorMessage
                        });
                    }
                }
            }

            var importItems = downloadResults
                .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FilePath))
                .Select(r => (r.MaterialId, r.FilePath, r.RevitFileType))
                .ToList();

            var importTotal = importItems.Count
                              + (surfaceRows.Count > 0 && surfacesRvtPath != null ? surfaceRows.Count : 0);

            Report(progress, "import", 0, Math.Max(importTotal, 1), "Загрузка в проект...");

            var materialsLoaded = 0;

            if (importItems.Count > 0)
            {
                var familyResults = RevitFamilyImportService.LoadFamiliesIntoDocument(doc, importItems);
                foreach (var fr in familyResults)
                {
                    if (fr.Success)
                    {
                        materialsLoaded++;
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = fr.MaterialId,
                            Success = true,
                            Phase = "import"
                        });
                    }
                    else
                    {
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = fr.MaterialId,
                            Success = false,
                            Phase = "import",
                            ErrorMessage = HumanizeError(fr.ErrorMessage) ?? "Не удалось загрузить семейство"
                        });
                    }
                }
            }

            if (surfaceRows.Count > 0 && !string.IsNullOrWhiteSpace(surfacesRvtPath))
            {
                var surfaceResults = RevitSurfaceImportService.CopyMaterialsIntoDocument(
                    doc,
                    surfacesRvtPath,
                    surfaceRows.Select(r => r.MaterialId.Value));

                foreach (var sr in surfaceResults)
                {
                    if (sr.Success)
                    {
                        materialsLoaded++;
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = sr.MaterialId,
                            Success = true,
                            Phase = "surface"
                        });
                    }
                    else
                    {
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = sr.MaterialId,
                            Success = false,
                            Phase = "surface",
                            ErrorMessage = HumanizeError(sr.ErrorMessage)
                                           ?? "Не удалось импортировать surface"
                        });
                    }
                }
            }

            Report(progress, "import", importTotal, Math.Max(importTotal, 1), "Загрузка в проект...");

            var errorItems = itemResults.Where(i => !i.Success).ToList();
            var errorCount = errorItems.Count;

            ExportRoomsApplication._logger?.Information(
                "Materials sync completed: client_request_id={ClientRequestId}, loaded={Loaded}, errors={Errors}, syncable={Syncable}",
                clientRequestId,
                materialsLoaded,
                errorCount,
                rfaRows.Count + surfaceRows.Count);

            return new RevitMaterialsSyncResult
            {
                Success = errorCount == 0,
                MaterialsLoaded = materialsLoaded,
                ErrorCount = errorCount,
                TotalSyncable = rfaRows.Count + surfaceRows.Count,
                SurfacesErrorMessage = surfacesErrorMessage,
                Items = itemResults,
                ErrorMessage = BuildErrorMessage(errorItems, surfacesErrorMessage)
            };
        }

        static string BuildErrorMessage(
            IReadOnlyList<RevitMaterialSyncItemResult> errorItems,
            string surfacesErrorMessage)
        {
            if (errorItems == null || errorItems.Count == 0)
                return null;

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(surfacesErrorMessage))
                parts.Add(surfacesErrorMessage);

            var allErrors = errorItems
                .GroupBy(i => i.MaterialId)
                .Select(g => g.First())
                .Select(i => $"• #{i.MaterialId}: {i.ErrorMessage}")
                .ToList();

            if (allErrors.Count > 0)
                parts.Add(string.Join("\n", allErrors));

            return parts.Count > 0
                ? string.Join("\n\n", parts)
                : $"Ошибок: {errorItems.Count}";
        }

        /// <summary>Короткие понятные формулировки для UI.</summary>
        internal static string HumanizeError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var msg = raw.Trim();

            if (msg.Contains("Параметр SR_ID не найден", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("SR_ID не найден в FamilyManager", StringComparison.OrdinalIgnoreCase))
                return "В RFA нет параметра SR_ID";

            if (msg.Contains("пуст или не число", StringComparison.OrdinalIgnoreCase))
                return "SR_ID в RFA пуст или не число";

            if (msg.Contains("не совпадает с material_id", StringComparison.OrdinalIgnoreCase))
                return msg.Replace("не совпадает с material_id", "≠ material_id", StringComparison.OrdinalIgnoreCase);

            if (msg.Contains("не найден в surfaces.rvt", StringComparison.OrdinalIgnoreCase))
                return msg + " — добавьте тип с этим SR_ID в библиотеку";

            if (msg.Contains("API не вернул surfaces_file_url", StringComparison.OrdinalIgnoreCase))
                return "Не задана библиотека surfaces.rvt на сервере";

            if (msg.Contains("surfaces.rvt недоступна", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("403", StringComparison.Ordinal))
                return msg;

            return msg;
        }

        static bool IsSurfaceRow(RevitMaterialRowDto row) =>
            string.Equals(row?.RevitFileType?.Trim(), "surface", StringComparison.OrdinalIgnoreCase);

        static void Report(
            IProgress<RevitMaterialsSyncProgress> progress,
            string phase,
            int done,
            int total,
            string message)
        {
            progress?.Report(new RevitMaterialsSyncProgress
            {
                Phase = phase,
                Done = done,
                Total = total,
                Message = message
            });
        }
    }
}
