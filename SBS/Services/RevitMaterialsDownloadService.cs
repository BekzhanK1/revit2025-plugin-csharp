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

        public static string SurfacesLibraryCachePath => Path.Combine(CacheRoot, "surfaces.rvt");

        public static async Task<DownloadResult> EnsureSurfacesLibraryAsync()
        {
            if (!Configs.HasSurfacesRvtUrl)
            {
                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = false,
                    ErrorMessage = "Не задана ссылка на surfaces.rvt (Configs.SurfacesRvtUrl)"
                };
            }

            Directory.CreateDirectory(CacheRoot);
            var targetPath = SurfacesLibraryCachePath;

            if (File.Exists(targetPath))
            {
                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = true,
                    Skipped = true,
                    FilePath = targetPath
                };
            }

            var tempPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");

            try
            {
                var bytes = await Http.GetByteArrayAsync(Configs.SurfacesRvtUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
                if (File.Exists(targetPath))
                {
                    TryClearReadOnly(targetPath);
                    File.Delete(targetPath);
                }

                File.Move(tempPath, targetPath);

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
                ExportRoomsApplication._logger?.Warning(ex, "Surfaces library download failed");
                return new DownloadResult
                {
                    MaterialId = 0,
                    RevitFileType = "surface",
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
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
                var bytes = await Http.GetByteArrayAsync(row.RevitFileUrl).ConfigureAwait(false);
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
    }
}
