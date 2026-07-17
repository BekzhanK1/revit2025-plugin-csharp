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

            var remontId = remont.RemontId ?? remont.Id;
            if (remontId <= 0)
            {
                return Fail("Не указан ID ремонта.");
            }

            if (ProjectRemontMetadataService.IsInitialized(doc)
                && !ProjectRemontMetadataService.ValidateMatches(doc, remontId))
            {
                var existing = ProjectRemontMetadataService.TryRead(doc);
                return new ProjectInitResult
                {
                    Success = false,
                    RemontConflict = true,
                    ErrorMessage =
                        $"Проект уже привязан к ремонту #{existing?.RemontId}. " +
                        $"Нельзя инициализировать с ремонтом #{remontId}."
                };
            }

            Report(progress, materialsResponse == null ? "Чтение материалов..." : "Подготовка материалов...");
            if (materialsResponse == null)
            {
                try
                {
                    materialsResponse = await RevitMaterialsService.ReadAsync(remontId).ConfigureAwait(true);
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

            var targetPath = ProjectFileNamingService.BuildFullPath(
                remontId,
                ResolveResidentName(remont));

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

            Report(progress, "Запись метаданных ремонта...");
            try
            {
                var clientRequestId = remont.ClientRequestId;
                if (clientRequestId <= 0 && materialsResponse.ClientRequestId.HasValue)
                    clientRequestId = materialsResponse.ClientRequestId.Value;

                ProjectRemontMetadataService.Write(doc, new ProjectRemontMetadata
                {
                    RemontId = remontId,
                    ClientRequestId = clientRequestId
                });
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init: metadata write failed");
                return Fail("Не удалось записать метаданные ремонта: " + ex.Message, copyResult.TargetPath);
            }

            Report(progress, "Синхронизация материалов...");
            RevitMaterialsSyncResult syncResult;
            try
            {
                syncResult = await RevitMaterialsSyncOrchestrator.SyncAllAsync(
                    doc,
                    remontId,
                    materialsResponse.Data,
                    materialsResponse.SurfacesFileUrl?.Trim(),
                    materialsResponse.SurfacesFileHash?.Trim()).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init: materials sync failed");
                return Fail("Синхронизация материалов не удалась: " + ex.Message, copyResult.TargetPath);
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
                "Project init completed: remont_id={RemontId}, path={Path}, loaded={Loaded}, errors={Errors}",
                remontId,
                copyResult.TargetPath,
                syncResult.MaterialsLoaded,
                syncResult.ErrorCount);

            return new ProjectInitResult
            {
                Success = syncResult.ErrorCount == 0,
                NewFilePath = copyResult.TargetPath,
                MaterialsLoaded = syncResult.MaterialsLoaded,
                Errors = syncResult.ErrorCount,
                IsWorksharedWarning = copyResult.IsWorksharedWarning,
                ErrorMessage = syncResult.ErrorCount > 0
                    ? syncResult.ErrorMessage ?? $"Инициализация завершена с ошибками: {syncResult.ErrorCount}"
                    : copyResult.IsWorksharedWarning ? ProjectCopyService.WorksharedUnsupportedMessage : null
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
