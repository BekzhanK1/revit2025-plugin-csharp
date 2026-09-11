using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
        static Task _preDownloadTask = Task.CompletedTask;

        public static Task PreDownloadTask => _preDownloadTask;

        public static int CountSyncableMaterials(IEnumerable<RevitMaterialRowDto> materials)
        {
            var materialList = (materials ?? Enumerable.Empty<RevitMaterialRowDto>()).ToList();
            return GetRfaRows(materialList).Count + GetSurfaceRows(materialList).Count;
        }

        /// <summary>
        /// Фоновое скачивание RFA/surfaces.rvt, пока пользователь смотрит preview.
        /// Повторный вызов при init безопасен — сработает кэш.
        /// </summary>
        public static void StartBackgroundPreDownload(
            int clientRequestId,
            IEnumerable<RevitMaterialRowDto> materials,
            string surfacesFileUrl,
            string surfacesFileHash)
        {
            _preDownloadTask = PreDownloadAsync(clientRequestId, materials, surfacesFileUrl, surfacesFileHash);
        }

        public static async Task PreDownloadAsync(
            int clientRequestId,
            IEnumerable<RevitMaterialRowDto> materials,
            string surfacesFileUrl,
            string surfacesFileHash,
            CancellationToken cancellationToken = default)
        {
            if (clientRequestId <= 0)
                return;

            var materialList = (materials ?? Enumerable.Empty<RevitMaterialRowDto>()).ToList();
            var rfaRows = GetRfaRows(materialList);
            var surfaceRows = GetSurfaceRows(materialList);
            var url = surfacesFileUrl?.Trim();
            var hash = surfacesFileHash?.Trim();

            if (rfaRows.Count == 0 && surfaceRows.Count == 0)
                return;

            var sw = Stopwatch.StartNew();
            ExportRoomsApplication._logger?.Information(
                "Materials pre-download start: client_request_id={ClientRequestId}, rfa={RfaCount}, surface={SurfaceCount}",
                clientRequestId,
                rfaRows.Count,
                surfaceRows.Count);

            try
            {
                if (rfaRows.Count > 0)
                {
                    var downloadResults = await RevitMaterialsDownloadService
                        .SyncAsync(rfaRows, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    ExportRoomsApplication._logger?.Information(
                        "Materials pre-download RFA done: ok={Ok}, failed={Failed}, cache_hit={Skipped}, elapsed_ms={ElapsedMs}",
                        downloadResults.Count(r => r.Success),
                        downloadResults.Count(r => !r.Success),
                        downloadResults.Count(r => r.Success && r.Skipped),
                        sw.ElapsedMilliseconds);
                }

                if (surfaceRows.Count > 0)
                {
                    var surfacesDownload = await RevitMaterialsDownloadService
                        .EnsureSurfacesLibraryAsync(clientRequestId, url, hash, cancellationToken)
                        .ConfigureAwait(false);

                    ExportRoomsApplication._logger?.Information(
                        "Materials pre-download surfaces done: success={Success}, skipped={Skipped}, elapsed_ms={ElapsedMs}",
                        surfacesDownload.Success,
                        surfacesDownload.Skipped,
                        sw.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "Materials pre-download failed after {ElapsedMs} ms for client_request_id={ClientRequestId}",
                    sw.ElapsedMilliseconds,
                    clientRequestId);
            }
        }

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
            IProgress<RevitMaterialsSyncProgress> progress = null,
            RevitMaterialsSyncOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (clientRequestId <= 0)
                throw new ArgumentOutOfRangeException(nameof(clientRequestId), "clientRequestId must be positive.");

            cancellationToken.ThrowIfCancellationRequested();

            var materialList = (materials ?? Enumerable.Empty<RevitMaterialRowDto>()).ToList();
            var syncSw = Stopwatch.StartNew();

            var nullRows = materialList.Count(r => r == null);
            var missingId = materialList.Count(r => r != null && !r.MaterialId.HasValue);
            var nonSurface = materialList.Where(r => r != null && r.MaterialId.HasValue && !IsSurfaceRow(r)).ToList();
            var nonSurfaceMissingUrl = nonSurface.Count(r => string.IsNullOrWhiteSpace(r.RevitFileUrl));

            var rfaRows = GetRfaRows(materialList);
            var surfaceRows = GetSurfaceRows(materialList);

            ExportRoomsApplication._logger?.Information(
                "Materials sync filter: client_request_id={ClientRequestId}, total={Total}, null={NullRows}, missing_id={MissingId}, rfa_syncable={Rfa}, surface={Surface}, non_surface_no_url={NoUrl}, surfaces_file_url={HasSurfacesUrl}",
                clientRequestId,
                materialList.Count,
                nullRows,
                missingId,
                rfaRows.Count,
                surfaceRows.Count,
                nonSurfaceMissingUrl,
                !string.IsNullOrWhiteSpace(surfacesFileUrl));

            if (nonSurfaceMissingUrl > 0)
            {
                var ids = nonSurface
                    .Where(r => string.IsNullOrWhiteSpace(r.RevitFileUrl))
                    .Select(r => r.MaterialId!.Value)
                    .Take(30);
                ExportRoomsApplication._logger?.Warning(
                    "Materials sync: skipped non-surface rows without revit_file_url (showing up to 30): {MaterialIds}",
                    string.Join(", ", ids));
            }

            if (rfaRows.Count == 0 && surfaceRows.Count == 0)
            {
                ExportRoomsApplication._logger?.Warning(
                    "Materials sync: nothing to sync for client_request_id={ClientRequestId} (total rows={Total}). Init will report MaterialsLoaded=0.",
                    clientRequestId,
                    materialList.Count);
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

            var rfaNames = rfaRows.ToDictionary(
                r => r.MaterialId!.Value,
                r => string.IsNullOrWhiteSpace(r.MaterialName) ? $"#{r.MaterialId}" : r.MaterialName.Trim());

            var downloadProgress = new Progress<(int materialId, int done, int total, bool downloading)>(p =>
            {
                string label;
                if (p.downloading && rfaNames.TryGetValue(p.materialId, out var name))
                    label = $"Скачивание RFA: {name} (#{p.materialId})";
                else if (p.downloading)
                    label = $"Скачивание RFA #{p.materialId}…";
                else
                    label = $"Скачано RFA: {Math.Min(p.done, downloadTotal)} из {downloadTotal}";

                Report(progress, "download", p.done, downloadTotal, label);
            });

            ExportRoomsApplication._logger?.Information(
                "Materials sync download start: rfa_count={RfaCount}",
                rfaRows.Count);

            var downloadSw = Stopwatch.StartNew();
            cancellationToken.ThrowIfCancellationRequested();

            var downloadResults = await RevitMaterialsDownloadService
                .SyncAsync(rfaRows, downloadProgress, cancellationToken)
                .ConfigureAwait(true);
            downloadSw.Stop();

            var downloadOk = downloadResults.Count(r => r.Success);
            var downloadFail = downloadResults.Count(r => !r.Success);
            var downloadSkipped = downloadResults.Count(r => r.Success && r.Skipped);
            ExportRoomsApplication._logger?.Information(
                "Materials sync download done: ok={Ok}, failed={Failed}, cache_hit={Skipped}, elapsed_ms={ElapsedMs}",
                downloadOk,
                downloadFail,
                downloadSkipped,
                downloadSw.ElapsedMilliseconds);

            foreach (var dr in downloadResults.Where(r => !r.Success))
            {
                ExportRoomsApplication._logger?.Warning(
                    "Materials sync download error: material_id={MaterialId}, error={Error}",
                    dr.MaterialId,
                    dr.ErrorMessage ?? "—");
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
                ExportRoomsApplication._logger?.Information(
                    "Materials sync surfaces download start: surface_rows={SurfaceCount}, has_url={HasUrl}, hash={Hash}",
                    surfaceRows.Count,
                    !string.IsNullOrWhiteSpace(surfacesFileUrl),
                    string.IsNullOrWhiteSpace(surfacesFileHash) ? "—" : surfacesFileHash.Trim());

                Report(progress, "download", downloadDone, downloadTotal, "Скачивание библиотеки surfaces.rvt…");

                var surfacesDownloadSw = Stopwatch.StartNew();
                var surfacesDownload = await RevitMaterialsDownloadService
                    .EnsureSurfacesLibraryAsync(clientRequestId, surfacesFileUrl, surfacesFileHash, cancellationToken)
                    .ConfigureAwait(true);
                surfacesDownloadSw.Stop();

                downloadDone = downloadTotal;
                Report(progress, "download", downloadDone, downloadTotal, $"Скачивание: {downloadDone} из {downloadTotal}");

                if (surfacesDownload.Success)
                {
                    surfacesRvtPath = surfacesDownload.FilePath;
                    Report(
                        progress,
                        "download",
                        downloadTotal,
                        downloadTotal,
                        surfacesDownload.Skipped
                            ? "surfaces.rvt уже в кэше"
                            : "surfaces.rvt скачан");
                    ExportRoomsApplication._logger?.Information(
                        "Materials sync surfaces download ok: path={Path}, skipped={Skipped}, elapsed_ms={ElapsedMs}",
                        surfacesRvtPath,
                        surfacesDownload.Skipped,
                        surfacesDownloadSw.ElapsedMilliseconds);
                }
                else
                {
                    surfacesErrorMessage = HumanizeError(surfacesDownload.ErrorMessage)
                                          ?? "Не удалось скачать surfaces.rvt";
                    ExportRoomsApplication._logger?.Warning(
                        "Materials sync surfaces download failed after {ElapsedMs} ms: {Error}",
                        surfacesDownloadSw.ElapsedMilliseconds,
                        surfacesErrorMessage);
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

            if (options?.ValidateSrIdBeforeImport == true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunSrIdPreflightValidation(
                    doc,
                    downloadResults,
                    surfacesRvtPath,
                    surfaceRows,
                    itemResults,
                    progress,
                    cancellationToken);
            }

            if (options?.AbortBeforeImportOnErrors == true && itemResults.Any(i => !i.Success))
            {
                var preflightErrors = itemResults.Where(i => !i.Success).ToList();
                ExportRoomsApplication._logger?.Warning(
                    "Materials sync aborted before import: errors={Errors}",
                    preflightErrors.Count);

                syncSw.Stop();
                return new RevitMaterialsSyncResult
                {
                    Success = false,
                    MaterialsLoaded = 0,
                    ErrorCount = preflightErrors.Count,
                    TotalSyncable = rfaRows.Count + surfaceRows.Count,
                    SurfacesErrorMessage = surfacesErrorMessage,
                    Items = itemResults,
                    ErrorMessage = BuildErrorMessage(preflightErrors, surfacesErrorMessage)
                };
            }

            var importItems = downloadResults
                .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FilePath))
                .Select(r => (r.MaterialId, r.FilePath, r.RevitFileType))
                .ToList();

            var importTotal = importItems.Count
                              + (surfaceRows.Count > 0 && surfacesRvtPath != null ? surfaceRows.Count : 0);

            ExportRoomsApplication._logger?.Information(
                "Materials sync import start: rfa_import_items={ImportItems}, surface_import={SurfaceImport}, doc_title={DocTitle}",
                importItems.Count,
                surfaceRows.Count > 0 && surfacesRvtPath != null ? surfaceRows.Count : 0,
                doc.Title);

            Report(progress, "import", 0, Math.Max(importTotal, 1),
                importTotal > 0
                    ? $"Импорт в проект: 0 из {importTotal}"
                    : "Загрузка в проект...");

            var materialsLoaded = 0;
            var importDone = 0;
            var rfaImportSw = Stopwatch.StartNew();

            if (importItems.Count > 0)
            {
                var familyImportProgress = new Progress<(int done, int total, int materialId, string label)>(p =>
                {
                    Report(
                        progress,
                        "import",
                        p.done,
                        importTotal,
                        $"Импорт {p.done}/{importTotal}: {p.label}");
                });

                var familyResults = RevitFamilyImportService.LoadFamiliesIntoDocument(
                    doc,
                    importItems,
                    familyImportProgress,
                    rfaNames);

                foreach (var fr in familyResults)
                {
                    if (fr.Success)
                    {
                        materialsLoaded++;
                        ExportRoomsApplication._logger?.Debug(
                            "Materials sync import ok: material_id={MaterialId}, family={Family}, already={Already}",
                            fr.MaterialId,
                            fr.FamilyName ?? "—",
                            fr.AlreadyInProject);
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = fr.MaterialId,
                            Success = true,
                            Phase = "import"
                        });
                    }
                    else
                    {
                        ExportRoomsApplication._logger?.Warning(
                            "Materials sync import failed: material_id={MaterialId}, error={Error}",
                            fr.MaterialId,
                            fr.ErrorMessage ?? "—");
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = fr.MaterialId,
                            Success = false,
                            Phase = "import",
                            ErrorMessage = HumanizeError(fr.ErrorMessage) ?? "Не удалось загрузить семейство"
                        });
                    }
                }

                importDone = familyResults.Count;
            }

            rfaImportSw.Stop();
            ExportRoomsApplication._logger?.Information(
                "Materials sync RFA import done: items={Items}, loaded={Loaded}, elapsed_ms={ElapsedMs}",
                importItems.Count,
                materialsLoaded,
                rfaImportSw.ElapsedMilliseconds);

            if (surfaceRows.Count > 0 && !string.IsNullOrWhiteSpace(surfacesRvtPath))
            {
                var surfaceImportSw = Stopwatch.StartNew();
                var surfaceResults = RevitSurfaceImportService.CopyMaterialsIntoDocument(
                    doc,
                    surfacesRvtPath,
                    surfaceRows.Select(r => r.MaterialId.Value));

                foreach (var sr in surfaceResults)
                {
                    importDone++;
                    Report(
                        progress,
                        "import",
                        importDone,
                        importTotal,
                        $"Импорт {importDone}/{importTotal}: surface #{sr.MaterialId}");
                    if (sr.Success)
                    {
                        materialsLoaded++;
                        ExportRoomsApplication._logger?.Debug(
                            "Materials sync surface import ok: material_id={MaterialId}",
                            sr.MaterialId);
                        itemResults.Add(new RevitMaterialSyncItemResult
                        {
                            MaterialId = sr.MaterialId,
                            Success = true,
                            Phase = "surface"
                        });
                    }
                    else
                    {
                        ExportRoomsApplication._logger?.Warning(
                            "Materials sync surface import failed: material_id={MaterialId}, error={Error}",
                            sr.MaterialId,
                            sr.ErrorMessage ?? "—");
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

                surfaceImportSw.Stop();
                ExportRoomsApplication._logger?.Information(
                    "Materials sync surface import done: items={Items}, elapsed_ms={ElapsedMs}",
                    surfaceRows.Count,
                    surfaceImportSw.ElapsedMilliseconds);
            }

            Report(progress, "import", importTotal, Math.Max(importTotal, 1),
                importTotal > 0 ? $"Импорт завершён: {importTotal} из {importTotal}" : "Загрузка в проект...");

            var errorItems = itemResults.Where(i => !i.Success).ToList();
            var errorCount = errorItems.Count;

            if (errorCount > 0)
            {
                foreach (var err in errorItems.Take(50))
                {
                    ExportRoomsApplication._logger?.Warning(
                        "Materials sync error item: material_id={MaterialId}, phase={Phase}, error={Error}",
                        err.MaterialId,
                        err.Phase ?? "—",
                        err.ErrorMessage ?? "—");
                }
            }

            syncSw.Stop();
            ExportRoomsApplication._logger?.Information(
                "Materials sync completed: client_request_id={ClientRequestId}, loaded={Loaded}, errors={Errors}, syncable={Syncable}, rfa={Rfa}, surface={Surface}, total_elapsed_ms={ElapsedMs}",
                clientRequestId,
                materialsLoaded,
                errorCount,
                rfaRows.Count + surfaceRows.Count,
                rfaRows.Count,
                surfaceRows.Count,
                syncSw.ElapsedMilliseconds);

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

        static List<RevitMaterialRowDto> GetRfaRows(IEnumerable<RevitMaterialRowDto> materialList) =>
            (materialList ?? Enumerable.Empty<RevitMaterialRowDto>())
                .Where(r => r != null && r.MaterialId.HasValue && !IsSurfaceRow(r))
                .Where(r => !string.IsNullOrWhiteSpace(r.RevitFileUrl))
                .ToList();

        static List<RevitMaterialRowDto> GetSurfaceRows(IEnumerable<RevitMaterialRowDto> materialList) =>
            (materialList ?? Enumerable.Empty<RevitMaterialRowDto>())
                .Where(r => r != null && r.MaterialId.HasValue && IsSurfaceRow(r))
                .ToList();

        static void RunSrIdPreflightValidation(
            Document doc,
            IReadOnlyList<DownloadResult> downloadResults,
            string surfacesRvtPath,
            IReadOnlyList<RevitMaterialRowDto> surfaceRows,
            List<RevitMaterialSyncItemResult> itemResults,
            IProgress<RevitMaterialsSyncProgress> progress,
            CancellationToken cancellationToken = default)
        {
            var validationTotal = downloadResults.Count(r => r.Success && !string.IsNullOrWhiteSpace(r.FilePath))
                                  + (surfaceRows.Count > 0 && !string.IsNullOrWhiteSpace(surfacesRvtPath)
                                      ? surfaceRows.Count
                                      : 0);
            var validationDone = 0;

            // Один индекс SR_ID на весь проход валидации вместо пересборки на каждый материал
            // (BuildSrIdIndex — полный скан документа; см. аналогичный фикс в preflight preview).
            var srIdIndex = RevitMaterialPresenceService.BuildSrIdIndex(doc);

            Report(
                progress,
                "validation",
                validationDone,
                Math.Max(validationTotal, 1),
                validationTotal > 0
                    ? "Проверка SR_ID: 0 из " + validationTotal
                    : "Проверка SR_ID...");

            foreach (var download in downloadResults.Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FilePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                validationDone++;
                Report(
                    progress,
                    "validation",
                    validationDone,
                    Math.Max(validationTotal, 1),
                    $"Проверка SR_ID RFA #{download.MaterialId}…");

                if (RevitMaterialPresenceService.LookupInIndex(srIdIndex, download.MaterialId).IsInProject)
                    continue;

                var srIdError = RevitFamilyImportService.ValidateSrIdInRfaFile(
                    doc.Application,
                    download.FilePath,
                    download.MaterialId);

                if (string.IsNullOrWhiteSpace(srIdError))
                    continue;

                ExportRoomsApplication._logger?.Warning(
                    "Materials sync SR_ID validation failed: material_id={MaterialId}, error={Error}",
                    download.MaterialId,
                    srIdError);

                itemResults.Add(new RevitMaterialSyncItemResult
                {
                    MaterialId = download.MaterialId,
                    Success = false,
                    Phase = "validation",
                    ErrorMessage = HumanizeError(srIdError) ?? srIdError
                });
            }

            if (surfaceRows.Count == 0 || string.IsNullOrWhiteSpace(surfacesRvtPath))
                return;

            var surfaceIds = surfaceRows
                .Select(r => r.MaterialId!.Value)
                .Distinct()
                .Where(id => !RevitMaterialPresenceService.LookupInIndex(srIdIndex, id).IsInProject)
                .ToList();

            if (surfaceIds.Count == 0)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            var validationResults = RevitSurfaceImportService.ValidateMaterialsInLibrary(
                doc.Application,
                surfacesRvtPath,
                surfaceIds);

            foreach (var validation in validationResults)
            {
                validationDone++;
                Report(
                    progress,
                    "validation",
                    Math.Min(validationDone, validationTotal),
                    Math.Max(validationTotal, 1),
                    validation.Success
                        ? $"SR_ID surface #{validation.MaterialId}: OK"
                        : $"SR_ID surface #{validation.MaterialId}: ошибка");

                if (validation.Success)
                    continue;

                if (itemResults.Any(i => i.MaterialId == validation.MaterialId && !i.Success))
                    continue;

                itemResults.Add(new RevitMaterialSyncItemResult
                {
                    MaterialId = validation.MaterialId,
                    Success = false,
                    Phase = "validation",
                    ErrorMessage = HumanizeError(validation.ErrorMessage) ?? validation.ErrorMessage
                });
            }
        }

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
