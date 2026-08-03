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

    public sealed class RevitMaterialsSyncResult
    {
        public bool Success { get; init; }
        public int MaterialsLoaded { get; init; }
        public int ErrorCount { get; init; }
        public int TotalSyncable { get; init; }
        public string ErrorMessage { get; init; }
        public string SurfacesErrorMessage { get; init; }
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
                .Where(r => r.MaterialId.HasValue
                            && !IsSurfaceRow(r)
                            && !string.IsNullOrWhiteSpace(r.RevitFileUrl))
                .ToList();

            var surfaceRows = materialList
                .Where(r => r.MaterialId.HasValue && IsSurfaceRow(r))
                .ToList();

            if (rfaRows.Count == 0 && surfaceRows.Count == 0)
            {
                return new RevitMaterialsSyncResult
                {
                    Success = true,
                    ErrorMessage = "Нет файлов для синхронизации."
                };
            }

            var downloadTotal = rfaRows.Count + (surfaceRows.Count > 0 ? 1 : 0);
            var downloadDone = 0;

            Report(progress, "download", downloadDone, downloadTotal, $"Скачивание: {downloadDone} из {downloadTotal}");

            var downloadProgress = new Progress<(int materialId, int done, int total, bool downloading)>(_ =>
            {
                Report(progress, "download", downloadDone, downloadTotal, $"Скачивание: {downloadDone} из {downloadTotal}");
            });

            var downloadResults = await RevitMaterialsDownloadService
                .SyncAsync(rfaRows, downloadProgress)
                .ConfigureAwait(true);

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
                    surfacesRvtPath = surfacesDownload.FilePath;
                else
                    surfacesErrorMessage = surfacesDownload.ErrorMessage;
            }

            var importItems = downloadResults
                .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FilePath))
                .Select(r => (r.MaterialId, r.FilePath, r.RevitFileType))
                .ToList();

            var importCount = importItems.Count
                              + (surfaceRows.Count > 0 && surfacesRvtPath != null ? surfaceRows.Count : 0);

            Report(progress, "import", importCount, importCount, "Загрузка в проект...");

            var materialsLoaded = 0;

            if (importItems.Count > 0)
            {
                RevitFamilyImportService.LoadFamiliesIntoDocument(doc, importItems);
                materialsLoaded += importItems.Count;
            }

            if (surfaceRows.Count > 0 && !string.IsNullOrWhiteSpace(surfacesRvtPath))
            {
                RevitSurfaceImportService.CopyMaterialsIntoDocument(
                    doc,
                    surfacesRvtPath,
                    surfaceRows.Select(r => r.MaterialId.Value));

                materialsLoaded += surfaceRows.Count;
            }

            var errorCount = downloadResults.Count(r => !r.Success);
            if (surfaceRows.Count > 0 && string.IsNullOrWhiteSpace(surfacesRvtPath))
                errorCount += surfaceRows.Count;

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
                ErrorMessage = BuildErrorMessage(errorCount, downloadResults, surfacesErrorMessage)
            };
        }

        static string BuildErrorMessage(
            int errorCount,
            IReadOnlyCollection<DownloadResult> downloadResults,
            string surfacesErrorMessage)
        {
            if (errorCount == 0)
                return null;

            var parts = new List<string>();

            var rfaErrors = downloadResults.Where(r => !r.Success).ToList();
            if (rfaErrors.Count > 0)
            {
                parts.Add(
                    $"Не удалось скачать {rfaErrors.Count} файл(ов) RFA (material_id: "
                    + string.Join(", ", rfaErrors.Select(r => r.MaterialId)) + ").");
            }

            if (!string.IsNullOrWhiteSpace(surfacesErrorMessage))
                parts.Add(surfacesErrorMessage);

            return parts.Count > 0
                ? string.Join(" ", parts)
                : $"Инициализация завершена с ошибками: {errorCount}";
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
