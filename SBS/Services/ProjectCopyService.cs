using Autodesk.Revit.DB;
using System;
using System.IO;

namespace SmartRemont.ExportRooms.Services
{
    public class ProjectCopyResult
    {
        public bool Success { get; set; }
        public string TargetPath { get; set; }
        public string ErrorMessage { get; set; }
        public bool FileAlreadyExists { get; set; }
        public bool IsWorksharedWarning { get; set; }
    }

    public static class ProjectCopyService
    {
        public const string WorksharedUnsupportedMessage =
            "Worksharing включён: центральные модели не поддерживаются в v1. Операция может завершиться с ошибкой.";

        public static ProjectCopyResult SaveCopyAs(Document doc, string targetPath, bool overwrite)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            if (string.IsNullOrWhiteSpace(targetPath))
                return Fail(null, "Целевой путь не указан.");

            var fullPath = Path.GetFullPath(targetPath.Trim());
            var targetDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                return Fail(fullPath, "Не удалось определить папку для целевого файла.");

            try
            {
                ProjectFileNamingService.EnsureDirectoryExists(targetDirectory);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "SaveCopyAs: failed to create directory {Directory}", targetDirectory);
                return Fail(fullPath, "Не удалось создать целевую папку: " + ex.Message);
            }

            if (File.Exists(fullPath) && !overwrite)
            {
                ExportRoomsApplication._logger?.Information(
                    "SaveCopyAs skipped: file already exists at {TargetPath}", fullPath);

                return new ProjectCopyResult
                {
                    Success = false,
                    TargetPath = fullPath,
                    FileAlreadyExists = true,
                    ErrorMessage = "Файл уже существует: " + fullPath
                };
            }

            if (File.Exists(fullPath) && overwrite)
                TryClearReadOnly(fullPath);

            // IsReadOnly часто true у несохранённого шаблона — SaveAs в новый путь всё равно допустим.
            if (doc.IsReadOnly)
            {
                ExportRoomsApplication._logger?.Information(
                    "SaveCopyAs: source marked read-only (PathName={PathName}), attempting SaveAs anyway",
                    FormatPathName(doc.PathName));
            }

            var isWorksharedWarning = doc.IsWorkshared;
            if (isWorksharedWarning)
            {
                ExportRoomsApplication._logger?.Warning(
                    "SaveCopyAs: workshared document (PathName={PathName}). {Message}",
                    FormatPathName(doc.PathName),
                    WorksharedUnsupportedMessage);
            }

            try
            {
                var options = new SaveAsOptions
                {
                    OverwriteExistingFile = overwrite
                };

                ExportRoomsApplication._logger?.Information(
                    "SaveCopyAs starting: source={SourcePath}, target={TargetPath}, overwrite={Overwrite}, isReadOnly={IsReadOnly}",
                    FormatPathName(doc.PathName),
                    fullPath,
                    overwrite,
                    doc.IsReadOnly);

                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(fullPath);
                doc.SaveAs(modelPath, options);

                ExportRoomsApplication._logger?.Information(
                    "SaveCopyAs completed: target={TargetPath}, worksharedWarning={WorksharedWarning}",
                    fullPath,
                    isWorksharedWarning);

                return new ProjectCopyResult
                {
                    Success = true,
                    TargetPath = fullPath,
                    IsWorksharedWarning = isWorksharedWarning,
                    ErrorMessage = isWorksharedWarning ? WorksharedUnsupportedMessage : null
                };
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "SaveCopyAs failed: target={TargetPath}", fullPath);
                var hint = doc.IsReadOnly && string.IsNullOrEmpty(doc.PathName)
                    ? " Если открыт шаблон — сохраните проект вручную (Файл → Сохранить как) или обновите плагин."
                    : string.Empty;
                return Fail(fullPath, "SaveAs не удался: " + ex.Message + hint, isWorksharedWarning);
            }
        }

        static string FormatPathName(string pathName)
        {
            return string.IsNullOrEmpty(pathName) ? "(unsaved)" : pathName;
        }

        static ProjectCopyResult Fail(string targetPath, string message, bool isWorksharedWarning = false)
        {
            return new ProjectCopyResult
            {
                Success = false,
                TargetPath = targetPath,
                ErrorMessage = message,
                IsWorksharedWarning = isWorksharedWarning
            };
        }

        static void TryClearReadOnly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Debug(ex, "SaveCopyAs: could not clear read-only on {Path}", path);
            }
        }
    }
}
