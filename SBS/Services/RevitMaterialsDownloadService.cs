using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class DownloadResult
    {
        public int MaterialId { get; init; }
        public bool Success { get; init; }
        public bool Skipped { get; init; }
        public string FilePath { get; init; }
        public string RevitFileType { get; init; }
        public string ErrorMessage { get; init; }
    }

    public static class RevitMaterialsDownloadService
    {
        const string CacheFolderName = "revit-materials-cache";
        const string ManifestFileName = "cache_manifest.json";
        const string SurfacesManifestFileName = "surfaces_manifest.json";

        static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        static string CacheRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartRemont",
                CacheFolderName);

        static string ManifestPath => Path.Combine(CacheRoot, ManifestFileName);

        static string SurfacesManifestPath => Path.Combine(CacheRoot, SurfacesManifestFileName);

        public static async Task<DownloadResult> EnsureSurfacesLibraryAsync(
            int remontId,
            string surfacesFileUrl,
            string surfacesFileHash = null)
        {
            if (remontId <= 0)
            {
                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = false,
                    ErrorMessage = "Не указан ID ремонта"
                };
            }

            if (string.IsNullOrWhiteSpace(surfacesFileUrl))
            {
                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = false,
                    ErrorMessage = "API не вернул surfaces_file_url"
                };
            }

            Directory.CreateDirectory(CacheRoot);
            var targetPath = GetSurfacesCachePath(remontId);
            var normalizedHash = NormalizeHash(surfacesFileHash);
            var surfacesManifest = LoadSurfacesManifest();

            if (surfacesManifest.TryGetValue(remontId, out var cached) &&
                !string.IsNullOrWhiteSpace(cached.FilePath) &&
                File.Exists(cached.FilePath) &&
                HashMatches(cached.SurfacesFileHash, normalizedHash))
            {
                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = true,
                    Skipped = true,
                    FilePath = cached.FilePath
                };
            }

            var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");
            var downloadUrl = UnwrapMinioConsoleShareUrl(Configs.ResolveDownloadUrl(surfacesFileUrl.Trim()));

            try
            {
                var bytes = await Http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
                if (File.Exists(targetPath))
                {
                    TryClearReadOnly(targetPath);
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);

                surfacesManifest[remontId] = new SurfacesManifestEntry
                {
                    FilePath = targetPath,
                    SurfacesFileHash = normalizedHash,
                    DownloadedAt = DateTime.UtcNow
                };
                SaveSurfacesManifest(surfacesManifest);

                ExportRoomsApplication._logger?.Information(
                    "Surfaces library downloaded for remont {RemontId}, hash={Hash}",
                    remontId,
                    normalizedHash ?? "—");

                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = true,
                    Skipped = false,
                    FilePath = targetPath
                };
            }
            catch (Exception ex)
            {
                TryDelete(tempPath);
                var friendlyMessage = BuildSurfacesDownloadErrorMessage(ex, surfacesFileUrl);
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "Surfaces library download failed for remont {RemontId}: {FriendlyMessage}",
                    remontId,
                    friendlyMessage);
                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = false,
                    ErrorMessage = friendlyMessage
                };
            }
        }

        const string MinioConsoleSharePathSegment = "/api/v1/download-shared-object/";

        /// <summary>
        /// Backend отдаёт surfaces_file_url в виде ссылки на публичный (анонимный) proxy-эндпоинт
        /// MinIO Console — "http://host/api/v1/download-shared-object/{base64(presigned S3 URL)}".
        /// Сам эндпоинт не требует авторизации, но проксирование через Console может упасть 403,
        /// если у неё не настроен доступ к реальному S3-хосту (MINIO_SERVER_URL и т.п. — backend-инфра).
        /// Чтобы не зависеть от этого прокси, декодируем base64 и скачиваем напрямую с реального
        /// presigned S3 URL, который лежит внутри.
        /// </summary>
        static string UnwrapMinioConsoleShareUrl(string url)
        {
            try
            {
                var idx = url.IndexOf(MinioConsoleSharePathSegment, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return url;

                var encoded = url.Substring(idx + MinioConsoleSharePathSegment.Length).Trim('/');

                var queryIdx = encoded.IndexOf('?');
                if (queryIdx >= 0)
                    encoded = encoded.Substring(0, queryIdx);

                encoded = Uri.UnescapeDataString(encoded);

                var decoded = DecodeBase64Flexible(encoded);
                if (string.IsNullOrWhiteSpace(decoded))
                    return url;

                if (!Uri.TryCreate(decoded, UriKind.Absolute, out var innerUri)
                    || (innerUri.Scheme != Uri.UriSchemeHttp && innerUri.Scheme != Uri.UriSchemeHttps))
                {
                    return url;
                }

                ExportRoomsApplication._logger?.Information(
                    "Surfaces library: unwrapped MinIO console share URL, using direct S3 host {Host}",
                    innerUri.Host);

                return decoded;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Surfaces library: failed to unwrap console share URL, using original");
                return url;
            }
        }

        static string DecodeBase64Flexible(string value)
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            var padding = normalized.Length % 4;
            if (padding != 0)
                normalized += new string('=', 4 - padding);

            var bytes = Convert.FromBase64String(normalized);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// 403 на свежий presigned MinIO/S3 URL (перегенерируется на каждый запрос) обычно значит,
        /// что объект surfaces.rvt отсутствует в бакете или у него не выставлены права на чтение —
        /// это проблема данных/доступа на backend, а не ошибка авторизации плагина.
        /// </summary>
        static string BuildSurfacesDownloadErrorMessage(Exception ex, string surfacesFileUrl)
        {
            if (ex is HttpRequestException httpEx)
            {
                if (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return "Библиотека surfaces.rvt недоступна на сервере (403 Forbidden). "
                        + "Похоже, файл отсутствует в хранилище или для него не настроен доступ — "
                        + "сообщите об этом backend-команде (surfaces_file_url для этого ремонта). "
                        + "Материалы с типом surface не будут загружены, RFA-материалы продолжат синхронизацию.";
                }

                if (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return "Библиотека surfaces.rvt не найдена на сервере (404). "
                        + "Файл, вероятно, ещё не загружен в хранилище — сообщите backend-команде.";
                }

                return $"Не удалось скачать surfaces.rvt: {httpEx.Message} (HTTP {(int?)httpEx.StatusCode})";
            }

            return "Не удалось скачать surfaces.rvt: " + ex.Message;
        }

        static string GetSurfacesCachePath(int remontId) =>
            Path.Combine(CacheRoot, $"surfaces_{remontId}.rvt");

        static string NormalizeHash(string hash) =>
            string.IsNullOrWhiteSpace(hash) ? null : hash.Trim();

        static bool HashMatches(string cachedHash, string requestedHash)
        {
            if (string.IsNullOrWhiteSpace(requestedHash))
                return true;

            return string.Equals(
                NormalizeHash(cachedHash),
                requestedHash,
                StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<List<DownloadResult>> SyncAsync(
            IEnumerable<RevitMaterialRowDto> rows,
            IProgress<(int materialId, int done, int total, bool downloading)> progress = null)
        {
            var rowList = (rows ?? Enumerable.Empty<RevitMaterialRowDto>())
                .Where(r => r.MaterialId.HasValue && !string.IsNullOrWhiteSpace(r.RevitFileUrl))
                .ToList();

            Directory.CreateDirectory(CacheRoot);
            var manifest = LoadManifest();
            var results = new List<DownloadResult>();
            var total = rowList.Count;
            var done = 0;

            foreach (var row in rowList)
            {
                var materialId = row.MaterialId.Value;

                try
                {
                    progress?.Report((materialId, done, total, downloading: false));
                    var result = await SyncOneAsync(row, manifest, progress, done, total).ConfigureAwait(false);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(ex, "Revit material download failed for {MaterialId}", materialId);
                    results.Add(new DownloadResult
                    {
                        MaterialId = materialId,
                        RevitFileType = row.RevitFileType,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }

                done++;
                progress?.Report((materialId, done, total, downloading: false));
            }

            SaveManifest(manifest);
            return results;
        }

        static async Task<DownloadResult> SyncOneAsync(
            RevitMaterialRowDto row,
            Dictionary<int, CacheManifestEntry> manifest,
            IProgress<(int materialId, int done, int total, bool downloading)> progress,
            int done,
            int total)
        {
            var materialId = row.MaterialId.Value;
            var revitFileType = row.RevitFileType?.Trim() ?? string.Empty;

            // TODO: инвалидация по revit_file_hash, когда backend начнёт его заполнять (сейчас всегда NULL)
            if (manifest.TryGetValue(materialId, out var cached) &&
                !string.IsNullOrWhiteSpace(cached.FilePath) &&
                File.Exists(cached.FilePath))
            {
                return new DownloadResult
                {
                    MaterialId = materialId,
                    RevitFileType = revitFileType,
                    Success = true,
                    Skipped = true,
                    FilePath = cached.FilePath
                };
            }

            var fileName = BuildFileName(materialId, row.RevitAssetName, row.MaterialName, revitFileType);
            var targetPath = Path.Combine(CacheRoot, fileName);
            var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                progress?.Report((materialId, done, total, downloading: true));
                var downloadUrl = UnwrapMinioConsoleShareUrl(Configs.ResolveDownloadUrl(row.RevitFileUrl));
                var bytes = await Http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
                if (File.Exists(targetPath))
                {
                    TryClearReadOnly(targetPath);
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);

                manifest[materialId] = new CacheManifestEntry
                {
                    FilePath = targetPath,
                    RevitFileHash = string.IsNullOrWhiteSpace(row.RevitFileHash) ? null : row.RevitFileHash.Trim(),
                    DownloadedAt = DateTime.UtcNow
                };

                return new DownloadResult
                {
                    MaterialId = materialId,
                    RevitFileType = revitFileType,
                    Success = true,
                    Skipped = false,
                    FilePath = targetPath
                };
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        static string BuildFileName(int materialId, string assetName, string materialName, string revitFileType)
        {
            string baseName;
            if (!string.IsNullOrWhiteSpace(assetName))
                baseName = SanitizeFileName(assetName.Trim());
            else if (!string.IsNullOrWhiteSpace(materialName))
                baseName = SanitizeFileName(materialName.Trim());
            else
                baseName = materialId.ToString();

            if (baseName.Length > 100)
                baseName = baseName.Substring(0, 100).TrimEnd('_', ' ');

            var ext = revitFileType.ToLowerInvariant() switch
            {
                "rfa" => "rfa",
                "surface" => "rvt",
                _ => "bin"
            };

            return $"{materialId}_{baseName}.{ext}";
        }

        static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        static Dictionary<int, CacheManifestEntry> LoadManifest()
        {
            if (!File.Exists(ManifestPath))
                return new Dictionary<int, CacheManifestEntry>();

            try
            {
                var json = File.ReadAllText(ManifestPath);
                var raw = JsonConvert.DeserializeObject<Dictionary<string, CacheManifestEntry>>(json);
                if (raw == null)
                    return new Dictionary<int, CacheManifestEntry>();

                var result = new Dictionary<int, CacheManifestEntry>();
                foreach (var pair in raw)
                {
                    if (int.TryParse(pair.Key, out var materialId))
                        result[materialId] = pair.Value;
                }

                return result;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось прочитать cache_manifest.json");
                return new Dictionary<int, CacheManifestEntry>();
            }
        }

        static void SaveManifest(Dictionary<int, CacheManifestEntry> manifest)
        {
            var raw = manifest.ToDictionary(
                pair => pair.Key.ToString(),
                pair => pair.Value);

            var json = JsonConvert.SerializeObject(raw, Formatting.Indented);
            var tempPath = ManifestPath + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(ManifestPath))
                    File.Delete(ManifestPath);
                File.Move(tempPath, ManifestPath);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось сохранить cache_manifest.json");
                TryDelete(tempPath);
            }
        }

        /// <summary>
        /// Защита кэша от случайного редактирования — вызывать после успешной валидации/загрузки.
        /// </summary>
        public static void MarkCacheFileReadOnly(string filePath) => MarkReadOnly(filePath);

        static void MarkReadOnly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) == 0)
                    File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось установить ReadOnly для {Path}", path);
            }
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
            catch
            {
                // ignore
            }
        }

        static void TryDelete(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;

                TryClearReadOnly(path);
                File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        sealed class CacheManifestEntry
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }

            [JsonProperty("revit_file_hash")]
            public string RevitFileHash { get; set; }

            [JsonProperty("downloaded_at")]
            public DateTime DownloadedAt { get; set; }
        }

        sealed class SurfacesManifestEntry
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }

            [JsonProperty("surfaces_file_hash")]
            public string SurfacesFileHash { get; set; }

            [JsonProperty("downloaded_at")]
            public DateTime DownloadedAt { get; set; }
        }

        static Dictionary<int, SurfacesManifestEntry> LoadSurfacesManifest()
        {
            if (!File.Exists(SurfacesManifestPath))
                return new Dictionary<int, SurfacesManifestEntry>();

            try
            {
                var json = File.ReadAllText(SurfacesManifestPath);
                var raw = JsonConvert.DeserializeObject<Dictionary<string, SurfacesManifestEntry>>(json);
                if (raw == null)
                    return new Dictionary<int, SurfacesManifestEntry>();

                var result = new Dictionary<int, SurfacesManifestEntry>();
                foreach (var pair in raw)
                {
                    if (int.TryParse(pair.Key, out var remontId))
                        result[remontId] = pair.Value;
                }

                return result;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось прочитать surfaces_manifest.json");
                return new Dictionary<int, SurfacesManifestEntry>();
            }
        }

        static void SaveSurfacesManifest(Dictionary<int, SurfacesManifestEntry> manifest)
        {
            var raw = manifest.ToDictionary(
                pair => pair.Key.ToString(),
                pair => pair.Value);

            var json = JsonConvert.SerializeObject(raw, Formatting.Indented);
            var tempPath = SurfacesManifestPath + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(SurfacesManifestPath))
                    File.Delete(SurfacesManifestPath);
                File.Move(tempPath, SurfacesManifestPath);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось сохранить surfaces_manifest.json");
                TryDelete(tempPath);
            }
        }
    }
}
