using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// param_code → param_name → ведомость Revit → колонки (точное совпадение заголовков).
    /// </summary>
    public static class RoomMeasurementsScheduleMapping
    {
        public enum ParseMode
        {
            FlatByRoomColumn,
            GroupedByRoomHeader,
            DoorsByRoom,
            /// <summary>Одна или несколько строк без комнаты — значение на фиксированное помещение (фартук → Кухня).</summary>
            SingleValueToFixedRoom
        }

        public sealed class Entry
        {
            public string ParamCode { get; set; }
            public string ParamName { get; set; }
            /// <summary>Первое имя — для подсказки в UI; все варианты пробуются при поиске.</summary>
            public List<string> ScheduleNamesExact { get; set; }
            
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public ParseMode Mode { get; set; }
            public List<string> ValueColumnsExact { get; set; }
            public List<string> RoomColumnsExact { get; set; }
            public string FixedRoomName { get; set; }
            public bool ValueIsInteger { get; set; }
            /// <summary>Если задано — параметр только для помещений с этим базовым именем (Кухня №2 → Кухня).</summary>
            public List<string> RoomBaseNamesFilter { get; set; }
            /// <summary>Исключить помещения с этим базовым именем (например балкон из обоев).</summary>
            public List<string> RoomBaseNamesExclude { get; set; }
            /// <summary>Составной параметр — читается отдельно (WALL_AREA_MINUS).</summary>
            public bool IsMergedParameter { get; set; }
        }

        /// <summary>WALL_AREA_MINUS: обои — все комнаты кроме балкона; балкон — отдельная ведомость.</summary>
        public static class WallAreaMinusSources
        {
            public static Entry Interior => new Entry
            {
                ScheduleNamesExact = new List<string> { "Спецификация поклейка обоев с покраской" },
                Mode = ParseMode.GroupedByRoomHeader,
                ValueColumnsExact = new List<string> { "Площадь, м²" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                RoomBaseNamesExclude = new List<string> { "Балкон" }
            };

            public static Entry Balcony => new Entry
            {
                ScheduleNamesExact = new List<string> { "Спецификация краски для стен балкона" },
                Mode = ParseMode.GroupedByRoomHeader,
                ValueColumnsExact = new List<string> { "Площадь, м²", "Площадь" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                RoomBaseNamesFilter = new List<string> { "Балкон" }
            };

            public static Entry Bathroom => new Entry
            {
                ScheduleNamesExact = new List<string> { "Условное обозначение плитки в ванной", "Условное обозначение плитки в с/у" },
                Mode = ParseMode.GroupedByRoomHeader,
                ValueColumnsExact = new List<string> { "Площадь, м²", "Площадь" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                RoomBaseNamesFilter = new List<string> { "Ванная", "Санузел", "С/у" }
            };
        }

        private static List<Entry> _cachedEntries;
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartRemont", "RevitPlugin", "schedule_mappings.json");

        public static IReadOnlyList<Entry> All
        {
            get
            {
                if (_cachedEntries == null)
                {
                    _cachedEntries = LoadOrCreateConfig();
                }
                return _cachedEntries;
            }
        }

        private static List<Entry> LoadOrCreateConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };
                    var loaded = JsonSerializer.Deserialize<List<Entry>>(json, options);
                    if (loaded != null && loaded.Count > 0)
                    {
                        foreach (var e in loaded)
                        {
                            e.ScheduleNamesExact ??= new List<string>();
                            e.ValueColumnsExact ??= new List<string>();
                            e.RoomColumnsExact ??= new List<string>();
                        }
                        return loaded;
                    }
                }
            }
            catch
            {
                // Если не смогли прочитать — фоллбэк на дефолтные и перезапись.
            }

            var defaultEntries = CreateDefaultEntries();
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(defaultEntries, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // Игнорируем ошибку записи, чтобы плагин продолжил работу
            }

            return defaultEntries;
        }

        private static List<Entry> CreateDefaultEntries()
        {
            return new List<Entry>
            {
                new Entry
                {
                    ParamCode = "PERIMETER_FLOOR",
                    ParamName = "Периметр по полу (за минусом дверей и проемов)",
                    ScheduleNamesExact = new List<string> { "Спецификация плинтуса", "Спецификация плинтуса." },
                    Mode = ParseMode.GroupedByRoomHeader,
                    ValueColumnsExact = new List<string> { "Длина, м.", "Длина, м" },
                    RoomColumnsExact = new List<string> { "Помещение", "Помещения" }
                },
                new Entry
                {
                    ParamCode = "PERIMETER_ROOF",
                    ParamName = "Периметр по потолку (полный)",
                    ScheduleNamesExact = new List<string> { "Спецификация потолков", "Спецификация потолков." },
                    Mode = ParseMode.GroupedByRoomHeader,
                    ValueColumnsExact = new List<string> { "Периметр.", "Периметр, м" },
                    RoomColumnsExact = new List<string> { "Помещения", "Помещение" }
                },
                new Entry
                {
                    ParamCode = "WALL_AREA_MINUS",
                    ParamName = "Площадь стен за минусом площади дверей, проемов и окон",
                    ScheduleNamesExact = new List<string>
                    {
                        "Спецификация поклейка обоев с покраской",
                        "Спецификация краски для стен балкона"
                    },
                    IsMergedParameter = true
                },
                new Entry
                {
                    ParamCode = "PLITKA_AREA",
                    ParamName = "Плитка в прихожей или кухне",
                    ScheduleNamesExact = new List<string> { "Спецификация напольных плиток" },
                    Mode = ParseMode.GroupedByRoomHeader,
                    ValueColumnsExact = new List<string> { "Площадь, м²" },
                    RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                    RoomBaseNamesFilter = new List<string> { "Прихожая", "Кухня" }
                },
                new Entry
                {
                    ParamCode = "DOOR_CNT",
                    ParamName = "Количество межкомнатных дверей (одностворчатые)",
                    ScheduleNamesExact = new List<string> { "Спецификация дверей", "Спецификация дверей." },
                    Mode = ParseMode.DoorsByRoom,
                    ValueColumnsExact = new List<string> { "Кол-во, шт", "Кол-во", "Количество" },
                    RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                    ValueIsInteger = true
                },
                new Entry
                {
                    ParamCode = "DOUBLE_DOOR",
                    ParamName = "Двустворчатая дверь (ширина > 1000 мм)",
                    ScheduleNamesExact = new List<string> { "Спецификация дверей", "Спецификация дверей." },
                    Mode = ParseMode.DoorsByRoom,
                    ValueColumnsExact = new List<string> { "Кол-во, шт", "Кол-во", "Количество" },
                    RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                    ValueIsInteger = true
                },
                new Entry
                {
                    ParamCode = "APRON_AREA",
                    ParamName = "Площадь фартука на кухне",
                    ScheduleNamesExact = new List<string> { "Спецификация фартука кухни", "Условное обозначение фартука" },
                    Mode = ParseMode.SingleValueToFixedRoom,
                    FixedRoomName = "Кухня",
                    ValueColumnsExact = new List<string> { "Площадь", "Площадь, м²" }
                },
                new Entry
                {
                    ParamCode = "MOLDING_PERIMETER",
                    ParamName = "Периметр молдингов",
                    ScheduleNamesExact = new List<string> { "Спецификация молдинга", "Спецификация молдингов", "Спецификация молдингов." },
                    Mode = ParseMode.GroupedByRoomHeader,
                    ValueColumnsExact = new List<string> { "Длина, м.", "Длина, м", "Периметр.", "Периметр, м", "Длина" },
                    RoomColumnsExact = new List<string> { "Помещение", "Помещения" }
                }
            };
        }
    }
}
