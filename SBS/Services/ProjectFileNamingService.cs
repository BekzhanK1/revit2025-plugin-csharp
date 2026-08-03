using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartRemont.ExportRooms.Services
{
    public static class ProjectFileNamingService
    {
        public const int MaxBaseNameLength = 80;
        public const string DefaultResidentFallback = "Zayavka";
        public const string ProjectFileExtension = ".rvt";

        public static string GetDefaultProjectsFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "SmartRemont",
                "Projects");
        }

        public static string BuildBaseName(int clientRequestId, string residentName)
        {
            if (clientRequestId <= 0)
                throw new ArgumentOutOfRangeException(nameof(clientRequestId), "clientRequestId must be positive.");

            var sanitizedResident = SanitizeResidentName(residentName);
            if (string.IsNullOrEmpty(sanitizedResident))
                sanitizedResident = DefaultResidentFallback;

            var baseName = $"{clientRequestId}_{sanitizedResident}";
            return TruncateBaseName(baseName, clientRequestId, sanitizedResident);
        }

        public static string BuildFileName(int clientRequestId, string residentName) =>
            BuildBaseName(clientRequestId, residentName) + ProjectFileExtension;

        public static string BuildProjectDirectory(int clientRequestId, string residentName, string baseFolder = null)
        {
            var folder = string.IsNullOrWhiteSpace(baseFolder)
                ? GetDefaultProjectsFolder()
                : baseFolder.Trim();

            return Path.Combine(folder, BuildBaseName(clientRequestId, residentName));
        }

        /// <summary>
        /// Documents\SmartRemont\Projects\{client_request_id}_{name}\{client_request_id}_{name}.rvt
        /// </summary>
        public static string BuildFullPath(int clientRequestId, string residentName, string baseFolder = null)
        {
            var projectDirectory = BuildProjectDirectory(clientRequestId, residentName, baseFolder);
            return Path.Combine(projectDirectory, BuildFileName(clientRequestId, residentName));
        }

        public static void EnsureDirectoryExists(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Folder path is required.", nameof(folderPath));

            Directory.CreateDirectory(folderPath);
        }

        public static string SanitizeResidentName(string residentName)
        {
            if (string.IsNullOrWhiteSpace(residentName))
                return string.Empty;

            var sanitized = residentName.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(sanitized.Length);

            foreach (var c in sanitized)
            {
                if (Array.IndexOf(invalidChars, c) >= 0 || char.IsControl(c))
                    builder.Append('_');
                else
                    builder.Append(c);
            }

            sanitized = CollapseWhitespaceToUnderscores(builder.ToString());
            sanitized = CollapseRepeatedUnderscores(sanitized);
            return sanitized.Trim('_', ' ');
        }

        static string TruncateBaseName(string baseName, int clientRequestId, string sanitizedResident)
        {
            if (baseName.Length <= MaxBaseNameLength)
                return baseName;

            var prefix = $"{clientRequestId}_";
            var maxResidentLength = MaxBaseNameLength - prefix.Length;
            if (maxResidentLength <= 0)
                return clientRequestId.ToString();

            var truncatedResident = sanitizedResident.Length <= maxResidentLength
                ? sanitizedResident
                : sanitizedResident.Substring(0, maxResidentLength).TrimEnd('_', ' ');

            if (string.IsNullOrEmpty(truncatedResident))
                truncatedResident = DefaultResidentFallback;

            return $"{clientRequestId}_{truncatedResident}";
        }

        static string CollapseWhitespaceToUnderscores(string value)
        {
            return Regex.Replace(value, @"\s+", "_");
        }

        static string CollapseRepeatedUnderscores(string value)
        {
            return Regex.Replace(value, @"_+", "_");
        }
    }
}
