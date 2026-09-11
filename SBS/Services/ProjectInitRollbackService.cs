using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    public static class ProjectInitRollbackService
    {
        public const string CloseWithoutSavingHint =
            "Закройте проект без сохранения (Файл → Закрыть → не сохранять), затем откройте исходный шаблон.";

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
