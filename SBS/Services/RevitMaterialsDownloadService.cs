using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
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
            string surfacesFileHash = null,
            CancellationToken cancellationToken = default)
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
                using var httpResponse = await Http.GetAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
                httpResponse.EnsureSuccessStatusCode();
                var bytes = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
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

        const int MaxConcurrentDownloads = 5;

        public static async Task<List<DownloadResult>> SyncAsync(
            IEnumerable<RevitMaterialRowDto> rows,
            IProgress<(int materialId, int done, int total, bool downloading)> progress = null,
            CancellationToken cancellationToken = default)
        {
            var rowList = (rows ?? Enumerable.Empty<RevitMaterialRowDto>())
                .Where(r => r.MaterialId.HasValue && !string.IsNullOrWhiteSpace(r.RevitFileUrl))
                .ToList();

            Directory.CreateDirectory(CacheRoot);
            var manifest = LoadManifest();
            var total = rowList.Count;
            var doneCounter = 0;
            var manifestLock = new object();

            using var gate = new SemaphoreSlim(MaxConcurrentDownloads);

            var tasks = rowList.Select(async row =>
            {
                var materialId = row.MaterialId!.Value;
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var doneBefore = Volatile.Read(ref doneCounter);
                    progress?.Report((materialId, doneBefore, total, downloading: false));
                    return await SyncOneAsync(row, manifest, manifestLock, progress, doneBefore, total, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(ex, "Revit material download failed for {MaterialId}", materialId);
                    return new DownloadResult
                    {
                        MaterialId = materialId,
                        RevitFileType = row.RevitFileType,
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                }
                finally
                {
                    var doneAfter = Interlocked.Increment(ref doneCounter);
                    progress?.Report((materialId, doneAfter, total, downloading: false));
                    gate.Release();
                }
            }).ToList();

            var results = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();

            SaveManifest(manifest);
            return results;
        }

        static async Task<DownloadResult> SyncOneAsync(
            RevitMaterialRowDto row,
            Dictionary<int, CacheManifestEntry> manifest,
            object manifestLock,
            IProgress<(int materialId, int done, int total, bool downloading)> progress,
            int done,
            int total,
            CancellationToken cancellationToken = default)
        {
            var materialId = row.MaterialId.Value;
            var revitFileType = row.RevitFileType?.Trim() ?? string.Empty;
            var requestedHash = NormalizeHash(row.RevitFileHash);

            CacheManifestEntry cached;
            lock (manifestLock)
            {
                if (!manifest.TryGetValue(materialId, out cached)
                    || string.IsNullOrWhiteSpace(cached.FilePath)
                    || !File.Exists(cached.FilePath))
                {
                    cached = null;
                }
            }

            if (cached != null)
            {
                if (HashMatches(cached.RevitFileHash, requestedHash))
                {
                    ExportRoomsApplication._logger?.Debug(
                        "RFA download cache hit: material_id={MaterialId}, path={Path}, hash={Hash}",
                        materialId,
                        cached.FilePath,
                        requestedHash ?? "—");
                    return new DownloadResult
                    {
                        MaterialId = materialId,
                        RevitFileType = revitFileType,
                        Success = true,
                        Skipped = true,
                        FilePath = cached.FilePath
                    };
                }

                ExportRoomsApplication._logger?.Information(
                    "RFA download cache invalidated by hash: material_id={MaterialId}, cached={CachedHash}, requested={RequestedHash}",
                    materialId,
                    NormalizeHash(cached.RevitFileHash) ?? "—",
                    requestedHash ?? "—");
            }

            var fileName = BuildFileName(materialId, row.RevitAssetName, row.MaterialName, revitFileType);
            var targetPath = Path.Combine(CacheRoot, fileName);
            var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                progress?.Report((materialId, done, total, downloading: true));
                var downloadUrl = UnwrapMinioConsoleShareUrl(Configs.ResolveDownloadUrl(row.RevitFileUrl));
                ExportRoomsApplication._logger?.Information(
                    "RFA download start: material_id={MaterialId}, type={Type}, host={Host}, target={Target}",
                    materialId,
                    revitFileType,
                    TryGetHost(downloadUrl),
                    targetPath);
                using var httpResponse = await Http.GetAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
                httpResponse.EnsureSuccessStatusCode();
                var bytes = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
                if (File.Exists(targetPath))
                {
                    TryClearReadOnly(targetPath);
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);

                lock (manifestLock)
                {
                    manifest[materialId] = new CacheManifestEntry
                    {
                        FilePath = targetPath,
                        RevitFileHash = requestedHash,
                        DownloadedAt = DateTime.UtcNow
                    };
                }

                ExportRoomsApplication._logger?.Information(
                    "RFA download ok: material_id={MaterialId}, bytes={Bytes}, path={Path}",
                    materialId,
                    bytes.Length,
                    targetPath);

                return new DownloadResult
                {
                    MaterialId = materialId,
                    RevitFileType = revitFileType,
                    Success = true,
                    Skipped = false,
                    FilePath = targetPath
                };
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "RFA download failed: material_id={MaterialId}, type={Type}",
                    materialId,
                    revitFileType);
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

        static string TryGetHost(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "—";
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "—";
        }

        public static bool TryGetCachedFilePath(int materialId, out string filePath)
        {
            filePath = null;
            if (materialId <= 0)
                return false;

            if (!LoadManifest().TryGetValue(materialId, out var entry)
                || string.IsNullOrWhiteSpace(entry?.FilePath)
                || !File.Exists(entry.FilePath))
            {
                return false;
            }

            filePath = entry.FilePath;
            return true;
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
