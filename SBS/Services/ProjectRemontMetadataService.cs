using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.ProjectRemont;
using System;
using System.IO;
using System.Reflection;

namespace SmartRemont.ExportRooms.Services
{
    public static class ProjectRemontMetadataService
    {
        public static ProjectInfo GetProjectInformationElement(Document doc)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            return doc.ProjectInformation;
        }

        public static ProjectRemontMetadata TryRead(Document doc)
        {
            if (doc == null)
                return null;

            var projectInfo = doc.ProjectInformation;
            if (projectInfo == null)
                return null;

            var schema = ProjectRemontSchema.GetOrCreateSchema();
            var entity = projectInfo.GetEntity(schema);
            if (!entity.IsValid())
                return null;

            var metadata = new ProjectRemontMetadata
            {
                RemontId = entity.Get<int>(ProjectRemontSchema.FieldRemontId),
                ClientRequestId = entity.Get<int>(ProjectRemontSchema.FieldClientRequestId),
                InitializedAt = entity.Get<string>(ProjectRemontSchema.FieldInitializedAt),
                PluginVersion = entity.Get<string>(ProjectRemontSchema.FieldPluginVersion)
            };

            ExportRoomsApplication._logger?.Information(
                "Project remont metadata read: remont_id={RemontId}, client_request_id={ClientRequestId}, initialized_at={InitializedAt}, plugin_version={PluginVersion}",
                metadata.RemontId,
                metadata.ClientRequestId,
                metadata.InitializedAt,
                metadata.PluginVersion);

            return metadata;
        }

        public static void Write(Document doc, ProjectRemontMetadata metadata)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            var initializedAt = string.IsNullOrWhiteSpace(metadata.InitializedAt)
                ? DateTime.UtcNow.ToString("o")
                : metadata.InitializedAt;

            var pluginVersion = string.IsNullOrWhiteSpace(metadata.PluginVersion)
                ? GetAssemblyVersion()
                : metadata.PluginVersion;

            var schema = ProjectRemontSchema.GetOrCreateSchema();
            var projectInfo = GetProjectInformationElement(doc);

            using var tx = new Transaction(doc, "Smart Remont: project remont metadata");
            tx.Start();

            var entity = new Entity(schema);
            entity.Set(ProjectRemontSchema.FieldRemontId, metadata.RemontId);
            entity.Set(ProjectRemontSchema.FieldClientRequestId, metadata.ClientRequestId);
            entity.Set(ProjectRemontSchema.FieldInitializedAt, initializedAt);
            entity.Set(ProjectRemontSchema.FieldPluginVersion, pluginVersion);

            projectInfo.SetEntity(entity);
            tx.Commit();

            ExportRoomsApplication._logger?.Information(
                "Project remont metadata written: remont_id={RemontId}, client_request_id={ClientRequestId}, initialized_at={InitializedAt}, plugin_version={PluginVersion}",
                metadata.RemontId,
                metadata.ClientRequestId,
                initializedAt,
                pluginVersion);
        }

        public static bool IsInitialized(Document doc)
        {
            var metadata = TryRead(doc);
            return metadata != null && metadata.ClientRequestId > 0;
        }

        /// <summary>
        /// Шаблон или несохранённый RVT может содержать client_request_id в Storage после неудачной init —
        /// для меню хаба «инициализирован» только файл из SmartRemont\Projects.
        /// </summary>
        public static bool CanUseHubWorkFeatures(Document doc)
        {
            var metadata = TryRead(doc);
            if (metadata == null || metadata.ClientRequestId <= 0)
                return false;

            return IsSavedInitializedProjectFile(doc, metadata.ClientRequestId, metadata.RemontId);
        }

        public static bool ValidateMatches(Document doc, int expectedClientRequestId)
        {
            var metadata = TryRead(doc);
            return metadata != null && metadata.ClientRequestId == expectedClientRequestId;
        }

        /// <summary>
        /// Новые проекты именуются {client_request_id}_{name}.rvt; старые — {remont_id}_{name}.rvt
        /// (совместимость чтения, remontId опционален).
        /// </summary>
        static bool IsSavedInitializedProjectFile(Document doc, int clientRequestId, int remontId)
        {
            var pathName = doc?.PathName;
            if (string.IsNullOrWhiteSpace(pathName))
                return false;

            string fullPath;
            string projectsFolder;
            try
            {
                fullPath = Path.GetFullPath(pathName.Trim());
                projectsFolder = Path.GetFullPath(ProjectFileNamingService.GetDefaultProjectsFolder());
            }
            catch
            {
                return false;
            }

            if (!IsUnderDirectory(fullPath, projectsFolder))
                return false;

            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(fileName))
                return false;

            if (!fileName.EndsWith(ProjectFileNamingService.ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
                return false;

            var matchesPrimary = fileName.StartsWith(clientRequestId + "_", StringComparison.OrdinalIgnoreCase);
            var matchesLegacy = remontId > 0
                && fileName.StartsWith(remontId + "_", StringComparison.OrdinalIgnoreCase);
            if (!matchesPrimary && !matchesLegacy)
                return false;

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var parentDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(parentDirectory))
                return false;

            var parentFullPath = Path.GetFullPath(parentDirectory);
            var parentFolderName = Path.GetFileName(parentFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Новая структура: Projects\{baseName}\{baseName}.rvt
            if (string.Equals(parentFolderName, baseName, StringComparison.OrdinalIgnoreCase))
                return IsUnderDirectory(fullPath, projectsFolder);

            // Старые проекты: Projects\{baseName}.rvt
            return string.Equals(parentFullPath, projectsFolder, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsUnderDirectory(string filePath, string directoryPath)
        {
            var comparison = StringComparison.OrdinalIgnoreCase;
            if (filePath.Equals(directoryPath, comparison))
                return false;

            var directoryPrefix = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return filePath.StartsWith(directoryPrefix, comparison);
        }

        static string GetAssemblyVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "unknown";
        }
    }
}
