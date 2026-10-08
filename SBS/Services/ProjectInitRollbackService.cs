using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    public static class ProjectInitRollbackService
    {
        public const string CloseWithoutSavingHint =
            "Открытый до инициализации файл не изменялся.";

        /// <summary>
        /// Удаляет файл SaveCopyAs и версионные бэкапы Revit. Метаданные и doc.Save не вызывались — откат на диске.
        /// </summary>
        public static bool TryRollbackInitCopy(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return false;

            var deletedMain = TryDeleteFile(targetPath);
            CleanupVersionBackups(targetPath);

            ExportRoomsApplication._logger?.Warning(
                "Project init rollback: deleted_copy={DeletedMain}, path={Path}",
                deletedMain,
                targetPath);

            return deletedMain;
        }

        /// <summary>
        /// Переименовывает существующий файл проекта в «имя.backup-yyyyMMdd-HHmmss.rvt» рядом с ним,
        /// чтобы init не затёр рабочий проект. Возвращает путь копии или null + текст ошибки.
        /// </summary>
        public static string TryBackupExistingFile(string targetPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                return null;

            var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(targetPath);
            var extension = Path.GetExtension(targetPath);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(directory, $"{baseName}.backup-{stamp}{extension}");
            for (var i = 2; File.Exists(backupPath); i++)
                backupPath = Path.Combine(directory, $"{baseName}.backup-{stamp}-{i}{extension}");

            try
            {
                var attributes = File.GetAttributes(targetPath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(targetPath, attributes & ~FileAttributes.ReadOnly);

                File.Move(targetPath, backupPath);
                ExportRoomsApplication._logger?.Information(
                    "Project init: existing project backed up {Target} -> {Backup}",
                    targetPath,
                    backupPath);
                return backupPath;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project init: could not back up {Path}", targetPath);
                error = "Файл проекта уже существует и занят (возможно, открыт в другом Revit): "
                        + targetPath + ". Закройте его и повторите. " + ex.Message;
                return null;
            }
        }

        /// <summary>Возвращает резервную копию на место, если нового файла там нет.</summary>
        public static bool TryRestoreBackup(string backupPath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath)
                || string.IsNullOrWhiteSpace(targetPath) || File.Exists(targetPath))
            {
                return false;
            }

            try
            {
                File.Move(backupPath, targetPath);
                ExportRoomsApplication._logger?.Information(
                    "Project init rollback: restored {Backup} -> {Target}",
                    backupPath,
                    targetPath);
                return true;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project init rollback: could not restore {Backup}", backupPath);
                return false;
            }
        }

        public static void CleanupVersionBackups(string targetPath)
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

                    TryDeleteFile(file);
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "Project init rollback: backup cleanup failed for {TargetPath}",
                    targetPath);
            }
        }

        static bool TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

                File.Delete(path);
                ExportRoomsApplication._logger?.Information("Project init rollback: deleted {Path}", path);
                return true;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project init rollback: failed to delete {Path}", path);
                return false;
            }
        }
    }
}
