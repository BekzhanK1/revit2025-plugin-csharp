using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
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
    }

    public static class ProjectInitService
    {
        public static async Task<ProjectInitResult> InitializeProjectAsync(
            Document doc,
            RemontOption remont,
            bool overwriteExistingFile,
            IProgress<string> progress = null,
            RevitMaterialReadResponse materialsResponse = null)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (remont == null)
                throw new ArgumentNullException(nameof(remont));

            var clientRequestId = remont.ClientRequestId;
            if (clientRequestId <= 0)
            {
                return Fail("Не указан ID заявки (client_request_id).");
            }

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

            Report(progress, materialsResponse == null ? "Чтение материалов..." : "Подготовка материалов...");
            if (materialsResponse == null)
            {
                try
                {
                    materialsResponse = await RevitMaterialsService.ReadAsync(clientRequestId).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(ex, "Project init: materials read failed");
                    return Fail("Не удалось получить материалы: " + ex.Message);
                }
            }
            else if (materialsResponse.Data == null)
            {
                materialsResponse.Data = new List<RevitMaterialRowDto>();
            }

            var remontId = remont.RemontId ?? materialsResponse.RemontId ?? 0;

            var targetPath = ProjectFileNamingService.BuildFullPath(
                clientRequestId,
                remontId,
                remont.ResidentName,
                remont.FlatNum);

            Report(progress, "Сохранение копии проекта...");
            var copyResult = ProjectCopyService.SaveCopyAs(doc, targetPath, overwriteExistingFile);
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

            Report(progress, "Синхронизация материалов...");
            RevitMaterialsSyncResult syncResult;
            try
            {
                syncResult = await RevitMaterialsSyncOrchestrator.SyncAllAsync(
                    doc,
                    clientRequestId,
                    materialsResponse.Data,
                    materialsResponse.SurfacesFileUrl?.Trim(),
                    materialsResponse.SurfacesFileHash?.Trim()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init: materials sync failed");
                return Fail("Синхронизация материалов не удалась: " + ex.Message, copyResult.TargetPath);
            }

            if (syncResult.ErrorCount > 0)
            {
                ExportRoomsApplication._logger?.Warning(
                    "Project init: materials sync completed with errors ({ErrorCount}). Skipping metadata binding.",
                    syncResult.ErrorCount);

                return new ProjectInitResult
                {
                    Success = false,
                    NewFilePath = copyResult.TargetPath,
                    MaterialsLoaded = syncResult.MaterialsLoaded,
                    Errors = syncResult.ErrorCount,
                    IsWorksharedWarning = copyResult.IsWorksharedWarning,
                    ErrorMessage = syncResult.ErrorMessage ?? $"Синхронизация материалов завершилась с ошибками: {syncResult.ErrorCount}. Проект не привязан."
                };
            }

            Report(progress, "Запись метаданных заявки...");
            try
            {
                ProjectRemontMetadataService.Write(doc, new ProjectRemontMetadata
                {
                    RemontId = remontId,
                    ClientRequestId = clientRequestId
                });
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init: metadata write failed");
                return Fail("Не удалось записать метаданные заявки: " + ex.Message, copyResult.TargetPath);
            }

            Report(progress, "Сохранение проекта...");
            try
            {
                doc.Save();
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init: document save failed");
                return new ProjectInitResult
                {
                    Success = false,
                    NewFilePath = copyResult.TargetPath,
                    MaterialsLoaded = syncResult.MaterialsLoaded,
                    Errors = syncResult.ErrorCount,
                    IsWorksharedWarning = copyResult.IsWorksharedWarning,
                    ErrorMessage = "Не удалось сохранить проект: " + ex.Message
                };
            }

            CleanupBackupFiles(copyResult.TargetPath);

            ExportRoomsApplication._logger?.Information(
                "Project init completed: client_request_id={ClientRequestId}, path={Path}, loaded={Loaded}, errors={Errors}",
                clientRequestId,
                copyResult.TargetPath,
                syncResult.MaterialsLoaded,
                syncResult.ErrorCount);

            return new ProjectInitResult
            {
                Success = true,
                NewFilePath = copyResult.TargetPath,
                MaterialsLoaded = syncResult.MaterialsLoaded,
                Errors = 0,
                IsWorksharedWarning = copyResult.IsWorksharedWarning,
                ErrorMessage = copyResult.IsWorksharedWarning ? ProjectCopyService.WorksharedUnsupportedMessage : null
            };
        }

        static string ResolveResidentName(RemontOption remont)
        {
            if (!string.IsNullOrWhiteSpace(remont.ResidentName))
                return remont.ResidentName.Trim();

            if (!string.IsNullOrWhiteSpace(remont.Name))
                return remont.Name.Trim();

            return null;
        }

        /// <summary>
        /// Revit пишет версионный бэкап ({name}.NNNN.rvt) рядом с файлом при каждом Save,
        /// MaximumBackups в SaveOptions не может быть 0 — удаляем бэкап вручную после init.
        /// </summary>
        static void CleanupBackupFiles(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return;

            try
            {
                var directory = Path.GetDirectoryName(targetPath);
                var baseName = Path.GetFileNameWithoutExtension(targetPath);
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseName))
                    return;

                var pattern = "^" + Regex.Escape(baseName) + @"\.\d{4}\.rvt$";
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var fileName = Path.GetFileName(file);
                    if (!regex.IsMatch(fileName))
                        continue;

                    File.Delete(file);
                    ExportRoomsApplication._logger?.Information(
                        "Project init: removed backup file {BackupPath}", file);
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project init: backup cleanup failed for {TargetPath}", targetPath);
            }
        }

        static void Report(IProgress<string> progress, string message) =>
            progress?.Report(message);

        static ProjectInitResult Fail(string message, string newFilePath = null) =>
            new ProjectInitResult
            {
                Success = false,
                NewFilePath = newFilePath,
                ErrorMessage = message
            };
    }
}
