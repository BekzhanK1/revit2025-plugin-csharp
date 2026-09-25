using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class ProjectInitPreflightIssue
    {
        public int MaterialId { get; init; }
        public string MaterialName { get; init; }
        public string Message { get; init; }
    }

    public sealed class ProjectInitPreflightResult
    {
        public int SyncableCount { get; init; }
        public int DownloadReadyCount { get; init; }
        public IReadOnlyList<ProjectInitPreflightIssue> Issues { get; init; } = Array.Empty<ProjectInitPreflightIssue>();
        public bool CanInit => SyncableCount > 0 && (Issues == null || Issues.Count == 0);
    }

    public static class ProjectInitMaterialsPreflightService
    {
        public static int CountSyncableMaterials(IEnumerable<RevitMaterialRowDto> materials) =>
            RevitMaterialsSyncOrchestrator.CountSyncableMaterials(materials);

        public static string BuildZeroSyncableMessage() =>
            "Нет материалов для загрузки в Revit: все позиции помечены как no_model/none или у RFA нет файла на сервере. "
            + "Инициализация возможна только когда есть 3D (RFA) или surface с URL.";

        public static async Task<ProjectInitPreflightResult> RunAsync(
            Document doc,
            RevitMaterialReadResponse materialsResponse,
            int clientRequestId,
            IProgress<string> progress = null,
            CancellationToken cancellationToken = default,
            bool ignoreHostProject = false)
        {
            var materialList = materialsResponse?.Data ?? new List<RevitMaterialRowDto>();
            var syncableCount = CountSyncableMaterials(materialList);
            if (syncableCount <= 0)
            {
                return new ProjectInitPreflightResult
                {
                    SyncableCount = 0,
                    DownloadReadyCount = 0,
                    Issues = Array.Empty<ProjectInitPreflightIssue>()
                };
            }

            progress?.Report("Ожидание фоновой загрузки файлов…");
            try
            {
                // Revit API (OpenDocumentFile) ниже должен идти в UI-потоке. ConfigureAwait(false)
                // уводит продолжение в пул, и Revit отвечает InternalException.
                await RevitMaterialsSyncOrchestrator.PreDownloadTask.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Preflight: pre-download task failed");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var rfaRows = materialList
                .Where(r => r != null && r.MaterialId.HasValue
                            && string.Equals(r.RevitFileType?.Trim(), "rfa", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(r.RevitFileUrl))
                .ToList();
            var surfaceRows = materialList
                .Where(r => r != null && r.MaterialId.HasValue
                            && string.Equals(r.RevitFileType?.Trim(), "surface", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var issues = new List<ProjectInitPreflightIssue>();
            var downloadReady = 0;

            // Индекс SR_ID строится ОДИН раз на весь preflight, а не на каждый материал —
            // BuildSrIdIndex сканирует весь документ (FamilySymbol/ElementType/Material),
            // повтор на N материалов давал N полных сканов и заметный лаг открытия preview.
            var srIdIndex = doc != null
                ? RevitMaterialPresenceService.BuildSrIdIndex(doc)
                : new Dictionary<int, string>();

            foreach (var row in rfaRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var materialId = row.MaterialId!.Value;
                var label = string.IsNullOrWhiteSpace(row.MaterialName) ? $"#{materialId}" : row.MaterialName.Trim();
                progress?.Report($"Проверка SR_ID: {label}");

                if (!ignoreHostProject
                    && doc != null
                    && RevitMaterialPresenceService.LookupInIndex(srIdIndex, materialId).IsInProject)
                {
                    downloadReady++;
                    continue;
                }

                var manifestPath = TryGetCachedRfaPath(materialId);
                if (string.IsNullOrWhiteSpace(manifestPath))
                {
                    // Фоновая загрузка уже завершилась (см. await выше) — если файла в кэше всё
                    // же нет, это провал скачивания (403/404/сеть), а не "ещё не скачан".
                    var lastError = RevitMaterialsDownloadService.GetLastDownloadError(materialId);
                    var message = !string.IsNullOrWhiteSpace(lastError)
                        ? "Не удалось скачать RFA: " + (RevitMaterialsSyncOrchestrator.HumanizeError(lastError) ?? lastError)
                        : "RFA ещё не скачан — дождитесь загрузки или проверьте сеть";
                    issues.Add(new ProjectInitPreflightIssue
                    {
                        MaterialId = materialId,
                        MaterialName = label,
                        Message = message
                    });
                    continue;
                }

                downloadReady++;
                if (doc == null)
                    continue;

                var srIdError = RevitFamilyImportService.ValidateSrIdInRfaFile(
                    doc.Application,
                    manifestPath,
                    materialId);

                if (!string.IsNullOrWhiteSpace(srIdError))
                {
                    issues.Add(new ProjectInitPreflightIssue
                    {
                        MaterialId = materialId,
                        MaterialName = label,
                        Message = RevitMaterialsSyncOrchestrator.HumanizeError(srIdError) ?? srIdError
                    });
                }
            }

            if (surfaceRows.Count > 0)
            {
                var surfacesDownload = await TryEnsureSurfacesCachedAsync(
                    clientRequestId,
                    materialsResponse?.SurfacesFileUrl,
                    materialsResponse?.SurfacesFileHash,
                    cancellationToken).ConfigureAwait(true);

                if (!surfacesDownload.Success)
                {
                    // Настоящая причина (403/404/нет surfaces_file_url) вместо общей фразы.
                    var message = RevitMaterialsSyncOrchestrator.HumanizeError(surfacesDownload.ErrorMessage)
                                   ?? "surfaces.rvt не скачан";
                    foreach (var row in surfaceRows)
                    {
                        issues.Add(new ProjectInitPreflightIssue
                        {
                            MaterialId = row.MaterialId!.Value,
                            MaterialName = row.MaterialName ?? $"#{row.MaterialId}",
                            Message = message
                        });
                    }
                }
                else
                {
                    var surfacesPath = surfacesDownload.FilePath;
                    var surfaceIdsInProject = !ignoreHostProject && doc != null
                        ? surfaceRows
                            .Select(r => r.MaterialId!.Value)
                            .Where(id => RevitMaterialPresenceService.LookupInIndex(srIdIndex, id).IsInProject)
                            .ToList()
                        : new List<int>();

                    // Счётчик "готово" должен учитывать surface так же, как RFA — иначе он всегда
                    // застревает на rfaRows.Count из (rfaRows+surfaceRows), даже когда всё в порядке.
                    downloadReady += surfaceIdsInProject.Count;

                    var surfaceIdsToValidate = surfaceRows
                        .Select(r => r.MaterialId!.Value)
                        .Where(id => !surfaceIdsInProject.Contains(id))
                        .Distinct()
                        .ToList();

                    if (surfaceIdsToValidate.Count > 0 && doc != null)
                    {
                        progress?.Report("Проверка SR_ID в surfaces.rvt…");
                        var validation = RevitSurfaceImportService.ValidateMaterialsInLibrary(
                            doc.Application,
                            surfacesPath,
                            surfaceIdsToValidate);

                        foreach (var item in validation)
                        {
                            if (item.Success)
                            {
                                downloadReady++;
                                continue;
                            }

                            issues.Add(new ProjectInitPreflightIssue
                            {
                                MaterialId = item.MaterialId,
                                MaterialName = $"#{item.MaterialId}",
                                Message = RevitMaterialsSyncOrchestrator.HumanizeError(item.ErrorMessage)
                                           ?? item.ErrorMessage
                            });
                        }
                    }
                    else if (doc == null)
                    {
                        // Нет документа для проверки SR_ID — скачивание прошло, засчитываем как готово.
                        downloadReady += surfaceIdsToValidate.Count;
                    }
                }
            }

            return new ProjectInitPreflightResult
            {
                SyncableCount = syncableCount,
                DownloadReadyCount = downloadReady,
                Issues = issues
            };
        }

        static string TryGetCachedRfaPath(int materialId) =>
            RevitMaterialsDownloadService.TryGetCachedFilePath(materialId, out var path) ? path : null;

        static async Task<DownloadResult> TryEnsureSurfacesCachedAsync(
            int clientRequestId,
            string surfacesFileUrl,
            string surfacesFileHash,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(surfacesFileUrl))
            {
                return new DownloadResult
                {
                    Success = false,
                    RevitFileType = "surface",
                    ErrorMessage = "API не вернул surfaces_file_url"
                };
            }

            return await RevitMaterialsDownloadService
                .EnsureSurfacesLibraryAsync(clientRequestId, surfacesFileUrl, surfacesFileHash, cancellationToken)
                .ConfigureAwait(true);
        }
    }
}
