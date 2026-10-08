using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class ProjectTemplateFile
    {
        public int GradeId { get; init; }
        public string LocalPath { get; init; }
        public string TemplateName { get; init; }
        public string VersionName { get; init; }
        public string FileHash { get; init; }
    }

    public static class ProjectTemplateService
    {
        public const string MissingTemplateMessage = "Для этого грейда шаблон ещё не загружен";
        const long MaxTemplateBytes = 500L * 1024L * 1024L;

        static readonly HttpClient DownloadHttp = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        public static async Task EnsureGradeHasTemplateAsync(
            int gradeId,
            CancellationToken cancellationToken = default)
        {
            await CheckAsync(gradeId, fileHash: null, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<ProjectTemplateFile> EnsureLocalFileAsync(
            int gradeId,
            IProgress<string> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (gradeId <= 0)
                throw new InvalidOperationException("У заявки не указан грейд. Найдите заявку заново.");

            var cached = TryReadCache(gradeId);
            progress?.Report("Проверка шаблона проекта...");
            var check = await CheckAsync(gradeId, cached?.FileHash, cancellationToken).ConfigureAwait(false);

            if (check.IsCurrent
                && cached != null
                && File.Exists(cached.LocalPath)
                && SizeMatches(cached.LocalPath, check.FileSizeBytes)
                && HashMatches(cached.LocalPath, cached.FileHash))
            {
                ExportRoomsApplication._logger?.Information(
                    "Project template cache hit: grade_id={GradeId}, path={Path}",
                    gradeId,
                    cached.LocalPath);
                return cached;
            }

            if (string.IsNullOrWhiteSpace(check.FileUrl))
                throw new InvalidOperationException(MissingTemplateMessage);

            if (!IsRteFileName(check.FileName))
                throw new InvalidOperationException("Сервер вернул не шаблон .rte.");

            progress?.Report("Скачивание шаблона проекта...");
            var downloaded = await DownloadAsync(gradeId, check, cancellationToken).ConfigureAwait(false);
            ExportRoomsApplication._logger?.Information(
                "Project template downloaded: grade_id={GradeId}, version={Version}, path={Path}",
                gradeId,
                check.VersionName,
                downloaded.LocalPath);
            return downloaded;
        }

        static async Task<ProjectTemplateCheckResponse> CheckAsync(
            int gradeId,
            string fileHash,
            CancellationToken cancellationToken)
        {
            if (gradeId <= 0)
                throw new InvalidOperationException("У заявки не указан грейд. Найдите заявку заново.");

            var session = ExportRoomsApplication.CurrentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                throw new InvalidOperationException("Требуется авторизация");

            var url = Configs.ProjectTemplateCheckUrl(gradeId, fileHash);
            using var response = await AuthApiClient.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, url),
                cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = TryReadErrorMessage(responseBody)
                    ?? $"Не удалось проверить шаблон проекта ({(int)response.StatusCode})";
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    message = "Сессия истекла. Выйдите и войдите снова.";
                throw new InvalidOperationException(message);
            }

            var parsed = JsonConvert.DeserializeObject<ProjectTemplateCheckResponse>(responseBody);
            if (parsed == null)
                throw new InvalidOperationException("Сервер вернул некорректный ответ проверки шаблона");

            if (parsed.Status == false || !string.IsNullOrWhiteSpace(parsed.Error))
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(parsed.Error) ? MissingTemplateMessage : parsed.Error);

            if (string.IsNullOrWhiteSpace(parsed.FileUrl))
                throw new InvalidOperationException(MissingTemplateMessage);

            return parsed;
        }

        static async Task<ProjectTemplateFile> DownloadAsync(
            int gradeId,
            ProjectTemplateCheckResponse check,
            CancellationToken cancellationToken)
        {
            var hash = NormalizeHash(check.FileHash);
            if (hash == null)
                throw new InvalidOperationException("Сервер не вернул хеш шаблона.");

            if (check.FileSizeBytes is > MaxTemplateBytes)
                throw new InvalidOperationException("Размер шаблона не должен превышать 500 МБ.");

            var directory = CacheDirectory(gradeId);
            Directory.CreateDirectory(directory);
            var targetPath = Path.Combine(directory, hash + ".rte");
            if (File.Exists(targetPath) && HashMatches(targetPath, hash))
            {
                var existing = new ProjectTemplateFile
                {
                    GradeId = gradeId,
                    LocalPath = targetPath,
                    TemplateName = check.TemplateName,
                    VersionName = check.VersionName,
                    FileHash = hash
                };
                WriteCache(gradeId, existing, check.FileSizeBytes);
                return existing;
            }

            var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");
            var downloadUrl = Configs.ResolveDownloadUrl(check.FileUrl.Trim());

            try
            {
                using var httpResponse = await DownloadHttp
                    .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                httpResponse.EnsureSuccessStatusCode();

                var length = httpResponse.Content.Headers.ContentLength;
                if (length is > MaxTemplateBytes)
                    throw new InvalidOperationException("Размер шаблона не должен превышать 500 МБ.");

                await using (var source = await httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1024 * 128];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > MaxTemplateBytes)
                            throw new InvalidOperationException("Размер шаблона не должен превышать 500 МБ.");
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }

                    if (total <= 0)
                        throw new InvalidOperationException("Скачанный шаблон пустой.");
                }

                var actualHash = ComputeMd5(tempPath);
                if (!string.Equals(actualHash, hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Скачанный шаблон не совпал с хешем на сервере.");

                // Имя файла = хеш. Если такой файл уже есть и цел (например, его скачал второй Revit),
                // берём его: он может быть занят, и удалить его не получится.
                if (File.Exists(targetPath) && HashMatches(targetPath, hash))
                {
                    TryDelete(tempPath);
                }
                else
                {
                    if (File.Exists(targetPath))
                    {
                        TryClearReadOnly(targetPath);
                        File.Delete(targetPath);
                    }

                    File.Move(tempPath, targetPath);
                    TryMarkReadOnly(targetPath);
                }

                var result = new ProjectTemplateFile
                {
                    GradeId = gradeId,
                    LocalPath = targetPath,
                    TemplateName = check.TemplateName,
                    VersionName = check.VersionName,
                    FileHash = hash
                };
                WriteCache(gradeId, result, check.FileSizeBytes);
                CleanupOldVersions(directory, targetPath);
                return result;
            }
            catch (Exception ex) when (ex is not InvalidOperationException and not OperationCanceledException)
            {
                TryDelete(tempPath);
                throw new InvalidOperationException("Не удалось скачать шаблон проекта: " + ex.Message, ex);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        static ProjectTemplateFile TryReadCache(int gradeId)
        {
            var manifestPath = ManifestPath(gradeId);
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                var parsed = JsonConvert.DeserializeObject<TemplateCacheManifest>(File.ReadAllText(manifestPath));
                var hash = NormalizeHash(parsed?.FileHash);
                if (parsed == null || hash == null || string.IsNullOrWhiteSpace(parsed.LocalPath))
                    return null;

                var fullPath = Path.GetFullPath(parsed.LocalPath);
                var directory = Path.GetFullPath(CacheDirectory(gradeId));
                if (!fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                    return null;
                if (!File.Exists(fullPath))
                    return null;

                return new ProjectTemplateFile
                {
                    GradeId = gradeId,
                    LocalPath = fullPath,
                    TemplateName = parsed.TemplateName,
                    VersionName = parsed.VersionName,
                    FileHash = hash
                };
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project template cache manifest is unreadable");
                return null;
            }
        }

        static void WriteCache(int gradeId, ProjectTemplateFile file, long? fileSizeBytes)
        {
            var manifest = new TemplateCacheManifest
            {
                FileHash = file.FileHash,
                LocalPath = file.LocalPath,
                TemplateName = file.TemplateName,
                VersionName = file.VersionName,
                FileSizeBytes = fileSizeBytes
            };
            try
            {
                File.WriteAllText(ManifestPath(gradeId), JsonConvert.SerializeObject(manifest));
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project template: could not write cache manifest");
            }
        }

        static void CleanupOldVersions(string directory, string keepPath)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var name = Path.GetFileName(file);
                    var isTemplate = name.EndsWith(".rte", StringComparison.OrdinalIgnoreCase);
                    var isTemp = name.Contains(".rte.tmp.", StringComparison.OrdinalIgnoreCase);
                    if (!isTemplate && !isTemp)
                        continue;
                    if (string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (isTemp && File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddHours(-1))
                        continue;

                    TryClearReadOnly(file);
                    TryDelete(file);
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Debug(ex, "Project template: old versions cleanup failed in {Directory}", directory);
            }
        }

        static bool HashMatches(string path, string expectedHash)
        {
            var expected = NormalizeHash(expectedHash);
            if (expected == null)
                return false;

            try
            {
                return string.Equals(ComputeMd5(path), expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project template: could not hash {Path}", path);
                return false;
            }
        }

        static bool SizeMatches(string path, long? expectedSize)
        {
            if (expectedSize is not > 0)
                return true;

            try
            {
                return new FileInfo(path).Length == expectedSize.Value;
            }
            catch
            {
                return false;
            }
        }

        static bool IsRteFileName(string fileName)
        {
            return string.Equals(Path.GetExtension(fileName ?? string.Empty), ".rte", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return null;

            var trimmed = hash.Trim().ToLowerInvariant();
            return Regex.IsMatch(trimmed, "^[a-f0-9]{32}$") ? trimmed : null;
        }

        static string ComputeMd5(string path)
        {
            using var stream = File.OpenRead(path);
            var hash = MD5.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        static string CacheDirectory(int gradeId) =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartRemont",
                "revit-project-templates",
                "grade-" + gradeId);

        static string ManifestPath(int gradeId) =>
            Path.Combine(CacheDirectory(gradeId), "current.json");

        static string TryReadErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var parsed = JsonConvert.DeserializeObject<ProjectTemplateCheckResponse>(responseBody);
                if (!string.IsNullOrWhiteSpace(parsed?.Error))
                    return parsed.Error;
            }
            catch
            {
                // ignore parse errors
            }

            return null;
        }

        static void TryClearReadOnly(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Debug(ex, "Project template: could not clear read-only on {Path}", path);
            }
        }

        static void TryMarkReadOnly(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Debug(ex, "Project template: could not mark read-only {Path}", path);
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Debug(ex, "Project template: could not delete {Path}", path);
            }
        }

        sealed class TemplateCacheManifest
        {
            [JsonProperty("file_hash")]
            public string FileHash { get; set; }

            [JsonProperty("local_path")]
            public string LocalPath { get; set; }

            [JsonProperty("template_name")]
            public string TemplateName { get; set; }

            [JsonProperty("version_name")]
            public string VersionName { get; set; }

            [JsonProperty("file_size_bytes")]
            public long? FileSizeBytes { get; set; }
        }
    }
}
