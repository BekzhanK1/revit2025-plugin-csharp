using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace SmartRemont.ExportRooms.Services
{
    public class PluginSettings
    {
        public Dictionary<int, DateTime> LastMaterialSyncTimes { get; set; } = new();
    }

    public static class LocalSettingsService
    {
        static readonly string SettingsFilePath;

        static LocalSettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "SmartRemont", "RevitPlugin");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            
            SettingsFilePath = Path.Combine(folder, "settings.json");
        }

        public static PluginSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    return new PluginSettings();

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<PluginSettings>(json);
                return settings ?? new PluginSettings();
            }
            catch
            {
                return new PluginSettings();
            }
        }

        public static void Save(PluginSettings settings)
        {
            try
            {
                if (settings == null) return;
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // ignore
            }
        }

        public static void SetLastMaterialSyncTime(int clientRequestId, DateTime time)
        {
            var settings = Load();
            settings.LastMaterialSyncTimes[clientRequestId] = time;
            Save(settings);
        }

        public static DateTime? GetLastMaterialSyncTime(int clientRequestId)
        {
            var settings = Load();
            if (settings.LastMaterialSyncTimes.TryGetValue(clientRequestId, out var time))
                return time;
            return null;
        }
    }
}
