using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class ProjectInitProgress
    {
        public string Message { get; init; }
        public int Done { get; init; }
        public int Total { get; init; }
        public bool Indeterminate { get; init; }
    }

    public sealed class ProjectInitResult
    {
        public bool Success { get; set; }
        public string NewFilePath { get; set; }
        public int MaterialsLoaded { get; set; }
        public int Errors { get; set; }
        public string ErrorMessage { get; set; }
        public bool FileAlreadyExists { get; set; }
        public bool IsWorksharedWarning { get; set; }
        public bool RemontConflict { get; set; }
        public bool RolledBack { get; set; }
        public bool Cancelled { get; set; }
    }

    public static class ProjectInitService
    {
        public static async Task<ProjectInitResult> InitializeProjectAsync(
            Document doc,
            RemontOption remont,
            bool overwriteExistingFile,
            IProgress<ProjectInitProgress> progress = null,
            RevitMaterialReadResponse materialsResponse = null,
            CancellationToken cancellationToken = default,
            bool ignorePreflightValidation = false)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (remont == null)
                throw new ArgumentNullException(nameof(remont));

            var worksharedError = ValidateWorkshared(doc);
            if (worksharedError != null)
                return Fail(worksharedError);

            var clientRequestId = remont.ClientRequestId;
            if (clientRequestId <= 0)
                return Fail("Не указан ID заявки (client_request_id).");

            if (ProjectRemontMetadataService.IsInitialized(doc)
                && !ProjectRemontMetadataService.ValidateMatches(doc, clientRequestId))
            {
                var existing = ProjectRemontMetadataService.TryRead(doc);
                return new ProjectInitResult
                {
                    Success = false,
                    RemontConflict = true,
                    ErrorMessage =
                        $"Проект уже привязан к заявке #{existing?.ClientRequestId}. " +
                        $"Нельзя инициализировать с заявкой #{clientRequestId}."
                };
            }

            var initSw = Stopwatch.StartNew();
            materialsResponse = await EnsureMaterialsResponseAsync(
                clientRequestId,
                materialsResponse,
                progress,
                cancellationToken).ConfigureAwait(true);

            if (ProjectInitMaterialsPreflightService.CountSyncableMaterials(materialsResponse.Data) <= 0)
                return Fail(ProjectInitMaterialsPreflightService.BuildZeroSyncableMessage());

            var remontId = remont.RemontId ?? materialsResponse.RemontId ?? 0;
            var targetPath = ProjectFileNamingService.BuildFullPath(
                clientRequestId,
                remontId,
                remont.ResidentName,
                remont.FlatNum);

            Report(progress, "Сохранение копии проекта...", indeterminate: true);
            cancellationToken.ThrowIfCancellationRequested();

            var copySw = Stopwatch.StartNew();
            var copyResult = ProjectCopyService.SaveCopyAs(doc, targetPath, overwriteExistingFile);
            copySw.Stop();
            ExportRoomsApplication._logger?.Information(
                "Project init phase save_copy_as: elapsed_ms={ElapsedMs}, success={Success}",
                copySw.ElapsedMilliseconds,
                copyResult.Success);

            if (!copyResult.Success)
            {
                return new ProjectInitResult
                {
                    Success = false,
                    NewFilePath = copyResult.TargetPath,
                    FileAlreadyExists = copyResult.FileAlreadyExists,
                    IsWorksharedWarning = copyResult.IsWorksharedWarning,
                    ErrorMessage = copyResult.ErrorMessage
                };
            }

            try
            {
                var syncResult = await RunMaterialsSyncAsync(
                    doc,
                    clientRequestId,
                    materialsResponse,
                    progress,
                    cancellationToken,
                    ignorePreflightValidation).ConfigureAwait(true);

                if (!IsInitSyncSuccessful(syncResult, ignorePreflightValidation))
                {
                    return FailAfterCopy(BuildStrictSyncFailureMessage(syncResult), copyResult, syncResult);
                }

                Report(progress, "Запись метаданных заявки...", indeterminate: true);
                cancellationToken.ThrowIfCancellationRequested();

                var remontIdFinal = remontId;
                ProjectRemontMetadataService.Write(doc, new ProjectRemontMetadata
                {
                    RemontId = remontIdFinal,
                    ClientRequestId = clientRequestId
                });

                Report(progress, "Сохранение проекта...", indeterminate: true);
                doc.Save();

                ProjectInitRollbackService.CleanupVersionBackups(copyResult.TargetPath);

                initSw.Stop();
                ExportRoomsApplication._logger?.Information(
                    "Project init completed: client_request_id={ClientRequestId}, path={Path}, loaded={Loaded}, total_elapsed_ms={ElapsedMs}",
                    clientRequestId,
                    copyResult.TargetPath,
                    syncResult.MaterialsLoaded,
                    initSw.ElapsedMilliseconds);

                var partialErrors = syncResult.ErrorCount;
                return new ProjectInitResult
                {
                    Success = true,
                    NewFilePath = copyResult.TargetPath,
                    MaterialsLoaded = syncResult.MaterialsLoaded,
                    Errors = partialErrors,
                    IsWorksharedWarning = copyResult.IsWorksharedWarning,
                    ErrorMessage = BuildInitSuccessWarning(copyResult, syncResult, ignorePreflightValidation)
                };
            }
            catch (OperationCanceledException)
            {
                ExportRoomsApplication._logger?.Warning("Project init cancelled by user");
                return FailAfterCopy("Инициализация отменена.", copyResult, cancelled: true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init failed");
                return FailAfterCopy(ex.Message, copyResult);
            }
        }

        /// <summary>
        /// Повторная strict-синхронизация материалов без SaveCopyAs (проект уже инициализирован).
        /// </summary>
        public static async Task<ProjectInitResult> ResyncMaterialsAsync(
            Document doc,
            RemontOption remont,
            IProgress<ProjectInitProgress> progress = null,
            RevitMaterialReadResponse materialsResponse = null,
            CancellationToken cancellationToken = default)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (remont == null)
                throw new ArgumentNullException(nameof(remont));

            var worksharedError = ValidateWorkshared(doc);
            if (worksharedError != null)
                return Fail(worksharedError);

            var clientRequestId = remont.ClientRequestId;
            if (clientRequestId <= 0)
                return Fail("Не указан ID заявки (client_request_id).");

            if (!ProjectRemontMetadataService.CanUseHubWorkFeatures(doc)
                || !ProjectRemontMetadataService.ValidateMatches(doc, clientRequestId))
            {
                return Fail("Re-sync доступен только для уже инициализированного проекта текущей заявки.");
            }

            materialsResponse = await EnsureMaterialsResponseAsync(
                clientRequestId,
                materialsResponse,
                progress,
                cancellationToken).ConfigureAwait(true);

            if (ProjectInitMaterialsPreflightService.CountSyncableMaterials(materialsResponse.Data) <= 0)
                return Fail(ProjectInitMaterialsPreflightService.BuildZeroSyncableMessage());

            try
            {
                var syncResult = await RunMaterialsSyncAsync(
                    doc,
                    clientRequestId,
                    materialsResponse,
                    progress,
                    cancellationToken,
                    ignorePreflightValidation: false).ConfigureAwait(true);

                if (!IsInitSyncSuccessful(syncResult, ignorePreflightValidation: false))
                {
                    return new ProjectInitResult
                    {
                        Success = false,
                        NewFilePath = doc.PathName,
                        MaterialsLoaded = syncResult.MaterialsLoaded,
                        Errors = Math.Max(syncResult.ErrorCount, 1),
                        ErrorMessage = BuildStrictSyncFailureMessage(syncResult)
                    };
                }

                Report(progress, "Сохранение проекта...", indeterminate: true);
                doc.Save();
                ProjectInitRollbackService.CleanupVersionBackups(doc.PathName);

                return new ProjectInitResult
                {
                    Success = true,
                    NewFilePath = doc.PathName,
                    MaterialsLoaded = syncResult.MaterialsLoaded,
                    Errors = 0
                };
            }
            catch (OperationCanceledException)
            {
                return new ProjectInitResult
                {
                    Success = false,
                    Cancelled = true,
                    NewFilePath = doc.PathName,
                    ErrorMessage = "Re-sync отменён."
                };
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project resync failed");
                return Fail(ex.Message, doc.PathName);
            }
        }

        static async Task<RevitMaterialReadResponse> EnsureMaterialsResponseAsync(
            int clientRequestId,
            RevitMaterialReadResponse materialsResponse,
            IProgress<ProjectInitProgress> progress,
            CancellationToken cancellationToken)
        {
            if (materialsResponse != null)
            {
                materialsResponse.Data ??= new List<RevitMaterialRowDto>();
                return materialsResponse;
            }

            Report(progress, "Чтение материалов...", indeterminate: true);
            var sw = Stopwatch.StartNew();
            try
            {
                return await RevitMaterialsService.ReadAsync(clientRequestId, cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                sw.Stop();
                ExportRoomsApplication._logger?.Information(
                    "Project init phase materials_read: elapsed_ms={ElapsedMs}",
                    sw.ElapsedMilliseconds);
            }
        }

        static async Task<RevitMaterialsSyncResult> RunMaterialsSyncAsync(
            Document doc,
            int clientRequestId,
            RevitMaterialReadResponse materialsResponse,
            IProgress<ProjectInitProgress> progress,
            CancellationToken cancellationToken,
            bool ignorePreflightValidation)
        {
            Report(progress, "Синхронизация материалов...", indeterminate: true);
            var syncSw = Stopwatch.StartNew();
            try
            {
                var syncProgress = new Progress<RevitMaterialsSyncProgress>(p =>
                {
                    progress?.Report(new ProjectInitProgress
                    {
                        Message = p.Message,
                        Done = p.Done,
                        Total = p.Total,
                        Indeterminate = p.Total <= 0
                    });
                });

                var options = ignorePreflightValidation
                    ? RevitMaterialsSyncOptions.InitSkipSrIdValidation
                    : RevitMaterialsSyncOptions.StrictInit;

                return await RevitMaterialsSyncOrchestrator.SyncAllAsync(
                    doc,
                    clientRequestId,
                    materialsResponse.Data,
                    materialsResponse.SurfacesFileUrl?.Trim(),
                    materialsResponse.SurfacesFileHash?.Trim(),
                    syncProgress,
                    options,
                    cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                syncSw.Stop();
                ExportRoomsApplication._logger?.Information(
                    "Project init phase materials_sync: elapsed_ms={ElapsedMs}, ignore_preflight={IgnorePreflight}",
                    syncSw.ElapsedMilliseconds,
                    ignorePreflightValidation);
            }
        }

        static string ValidateWorkshared(Document doc)
        {
            if (doc == null || !doc.IsWorkshared)
                return null;

            return "Worksharing включён — инициализация и re-sync в v1 не поддерживаются. "
                   + "Откройте локальную копию шаблона без центральной модели.";
        }

        static bool IsInitSyncSuccessful(RevitMaterialsSyncResult syncResult, bool ignorePreflightValidation)
        {
            if (syncResult == null || syncResult.TotalSyncable <= 0)
                return false;

            if (ignorePreflightValidation)
                return syncResult.MaterialsLoaded > 0;

            return syncResult.Success
                   && syncResult.ErrorCount == 0
                   && syncResult.MaterialsLoaded >= syncResult.TotalSyncable;
        }

        static string BuildInitSuccessWarning(
            ProjectCopyResult copyResult,
            RevitMaterialsSyncResult syncResult,
            bool ignorePreflightValidation)
        {
            if (copyResult?.IsWorksharedWarning == true)
                return ProjectCopyService.WorksharedUnsupportedMessage;

            if (!ignorePreflightValidation || syncResult?.ErrorCount <= 0)
                return null;

            return $"Загружено {syncResult.MaterialsLoaded} из {syncResult.TotalSyncable}. "
                   + $"Не удалось: {syncResult.ErrorCount}. Проверьте SR_ID и surfaces.rvt.";
        }

        static string BuildStrictSyncFailureMessage(RevitMaterialsSyncResult syncResult)
        {
            if (!string.IsNullOrWhiteSpace(syncResult?.ErrorMessage))
                return syncResult.ErrorMessage;

            if (syncResult?.TotalSyncable > 0 && syncResult.MaterialsLoaded <= 0)
            {
                return $"Не импортировано ни одного материала из {syncResult.TotalSyncable}. "
                       + "Проверьте RFA, SR_ID и surfaces.rvt на сервере.";
            }

            return "Инициализация остановлена: не все материалы прошли проверку и загрузку.";
        }

        static ProjectInitResult FailAfterCopy(
            string message,
            ProjectCopyResult copyResult,
            RevitMaterialsSyncResult syncResult = null,
            bool cancelled = false)
        {
            var rolledBack = false;
            if (!cancelled && !string.IsNullOrWhiteSpace(copyResult?.TargetPath))
                rolledBack = ProjectInitRollbackService.TryRollbackInitCopy(copyResult.TargetPath);

            var fullMessage = message;
            if (rolledBack)
            {
                fullMessage += "\n\nКопия проекта на диске удалена. "
                               + ProjectInitRollbackService.CloseWithoutSavingHint;
            }

            return new ProjectInitResult
            {
                Success = false,
                Cancelled = cancelled,
                NewFilePath = copyResult?.TargetPath,
                MaterialsLoaded = syncResult?.MaterialsLoaded ?? 0,
                Errors = syncResult?.ErrorCount ?? (cancelled ? 0 : 1),
                IsWorksharedWarning = copyResult?.IsWorksharedWarning ?? false,
                RolledBack = rolledBack,
                ErrorMessage = fullMessage
            };
        }

        static void Report(IProgress<ProjectInitProgress> progress, string message, bool indeterminate = false) =>
            progress?.Report(new ProjectInitProgress
            {
                Message = message,
                Indeterminate = indeterminate
            });

        static ProjectInitResult Fail(string message, string newFilePath = null) =>
            new ProjectInitResult
            {
                Success = false,
                NewFilePath = newFilePath,
                ErrorMessage = message
            };
    }
}
