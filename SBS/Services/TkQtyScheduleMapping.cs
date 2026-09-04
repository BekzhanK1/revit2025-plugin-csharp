using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// Конфиг ведомостей → (комната, material_id, qty) для сверки объёмов с ТК.
    /// Файл: %AppData%\SmartRemont\RevitPlugin\tk_qty_schedule_mappings.json
    /// </summary>
    public static class TkQtyScheduleMapping
    {
        public enum ParseMode
        {
            /// <summary>Строка: колонка комнаты + ID материала + qty.</summary>
            FlatByRoomColumn,
            /// <summary>Группировка Revit: строка-комната, затем строки материалов.</summary>
            GroupedByRoomHeader
        }

        public sealed class Entry
        {
            /// <summary>Код источника (для логов/UI), напр. DOORS, FLOORS.</summary>
            public string Code { get; set; }

            public string Title { get; set; }

            public List<string> ScheduleNamesExact { get; set; }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public ParseMode Mode { get; set; }

            public List<string> MaterialIdColumnsExact { get; set; }
            public List<string> MaterialNameColumnsExact { get; set; }
            public List<string> QuantityColumnsExact { get; set; }
            public List<string> RoomColumnsExact { get; set; }

            /// <summary>Ед. изм. для JSON/UI (шт, м², м, л).</summary>
            public string QuantityUnit { get; set; }

            /// <summary>Множитель к числу из ведомости (мм→м: 0.001).</summary>
            public double QuantityScale { get; set; } = 1d;

            public bool Enabled { get; set; } = true;
        }

        static List<Entry> _cached;
        public static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartRemont", "RevitPlugin", "tk_qty_schedule_mappings.json");

        public static IReadOnlyList<Entry> All
        {
            get
            {
                _cached ??= LoadOrCreateConfig();
                return _cached;
            }
        }

        public static void Reload() => _cached = LoadOrCreateConfig();

        static List<Entry> LoadOrCreateConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };
                    var loaded = JsonSerializer.Deserialize<List<Entry>>(json, options);
                    if (loaded is { Count: > 0 })
                    {
                        foreach (var e in loaded)
                            NormalizeEntry(e);

                        // Подмешиваем новые коды из дефолтов (старый AppData-конфиг не теряет правки пользователя).
                        var merged = MergeMissingDefaults(loaded, CreateDefaultEntries());
                        return merged;
                    }
                }
            }
            catch
            {
                // fallback to defaults
            }

            var defaults = CreateDefaultEntries();
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // ignore write errors
            }

            return defaults;
        }

        /// <summary>
        /// Добавляет в loaded только отсутствующие Code из defaults (без перезаписи пользовательских).
        /// </summary>
        static List<Entry> MergeMissingDefaults(List<Entry> loaded, List<Entry> defaults)
        {
            var codes = new HashSet<string>(
                loaded.Where(e => !string.IsNullOrWhiteSpace(e?.Code))
                    .Select(e => e.Code.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var changed = false;
            foreach (var d in defaults)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Code))
                    continue;
                if (codes.Contains(d.Code))
                    continue;
                NormalizeEntry(d);
                loaded.Add(d);
                codes.Add(d.Code);
                changed = true;
            }

            if (MergeMissingScheduleNames(loaded, defaults))
                changed = true;

            if (changed)
            {
                try
                {
                    var json = JsonSerializer.Serialize(loaded, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ConfigPath, json);
                }
                catch
                {
                    // ignore
                }
            }

            return loaded;
        }

        /// <summary>
        /// Подмешивает новые alias имён ведомостей из дефолтов, не трогая остальные правки AppData.
        /// </summary>
        static bool MergeMissingScheduleNames(List<Entry> loaded, List<Entry> defaults)
        {
            var byCode = defaults
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Code))
                .GroupBy(e => e.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var changed = false;
            foreach (var entry in loaded)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Code))
                    continue;
                if (!byCode.TryGetValue(entry.Code.Trim(), out var def))
                    continue;

                entry.ScheduleNamesExact ??= new List<string>();
                foreach (var name in def.ScheduleNamesExact ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (entry.ScheduleNamesExact.Any(x =>
                            string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    entry.ScheduleNamesExact.Add(name);
                    changed = true;
                }
            }

            return changed;
        }

        static void NormalizeEntry(Entry e)
        {
            if (e == null) return;
            e.ScheduleNamesExact ??= new List<string>();
            e.MaterialIdColumnsExact ??= new List<string>();
            e.MaterialNameColumnsExact ??= new List<string>();
            e.QuantityColumnsExact ??= new List<string>();
            e.RoomColumnsExact ??= new List<string>();
            if (e.QuantityScale <= 0) e.QuantityScale = 1d;
            if (string.IsNullOrWhiteSpace(e.QuantityUnit)) e.QuantityUnit = "—";
        }

        public static List<Entry> CreateDefaultEntries() => new()
        {
            new Entry
            {
                Code = "DOORS",
                Title = "Двери",
                ScheduleNamesExact = new List<string> { "Спецификация дверей", "Спецификация дверей." },
                Mode = ParseMode.FlatByRoomColumn,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Кол-во, шт", "Кол-во, шт.", "Кол-во" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "шт"
            },
            new Entry
            {
                Code = "ELECTRICS",
                Title = "Электрические приборы (с ID)",
                ScheduleNamesExact = new List<string>
                {
                    "Спецификация электрических приборов..",
                    "Спецификация электрических приборов.",
                    "Спецификация электрических приборов"
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование", "Описание" },
                QuantityColumnsExact = new List<string> { "Кол-во, шт.", "Кол-во, шт", "Число, шт", "Кол-во" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения", "Комната" },
                QuantityUnit = "шт"
            },
            new Entry
            {
                Code = "SANITARY",
                Title = "Сантех. оборудование",
                ScheduleNamesExact = new List<string> { "Спецификация сантех. оборудования" },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Число, шт", "Кол-во, шт", "Кол-во" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "шт"
            },
            new Entry
            {
                Code = "FLOORS",
                Title = "Площадь напольных покрытий",
                ScheduleNamesExact = new List<string>
                {
                    "Спецификация площади напольных покрытий",
                    "Спецификация напольных плиток"
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала", "ID" },
                MaterialNameColumnsExact = new List<string> { "Наименование", "Тип" },
                QuantityColumnsExact = new List<string> { "Площадь, м²", "Площадь" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м²"
            },
            new Entry
            {
                Code = "CEILINGS",
                Title = "Потолки / потолочные покрытия",
                ScheduleNamesExact = new List<string>
                {
                    "Спецификация потолков",
                    "Спецификация площади потолочных покрытий",
                    "Спецификация площади потолочных покрытий_кухня"
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала", "ID" },
                MaterialNameColumnsExact = new List<string> { "Наименование", "Тип" },
                QuantityColumnsExact = new List<string> { "Площадь, м²", "Площадь" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м²"
            },
            new Entry
            {
                Code = "WALLS_PAINT",
                Title = "Обои / покраска стен",
                ScheduleNamesExact = new List<string>
                {
                    "Спецификация поклейка обоев с покраской",
                    "Спецификация краски для стен балкона"
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала", "ID" },
                MaterialNameColumnsExact = new List<string> { "Наименование", "Тип" },
                QuantityColumnsExact = new List<string> { "Площадь, м²", "Площадь" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м²"
            },
            new Entry
            {
                Code = "BASEBOARD",
                Title = "Плинтус",
                ScheduleNamesExact = new List<string> { "Спецификация плинтуса", "Спецификация плинтуса." },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Длина, м.", "Длина, м", "Длина" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м"
            },
            new Entry
            {
                Code = "MOLDING",
                Title = "Молдинг",
                ScheduleNamesExact = new List<string>
                {
                    "Спецификация молдинга",
                    "Спецификация молдингов",
                    "Спецификация молдингов."
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Длина, м.", "Длина, м", "Длина" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м"
            },
            new Entry
            {
                Code = "T_PROFILE",
                Title = "Т-профиль",
                ScheduleNamesExact = new List<string>
                {
                    "Спецификация Т-профилей",
                    "Спецификация Т-профиля",
                    "Спецификация Т-профиль"
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                // Предпочитаем мм; если в модели только «шт» — scale сбросится в парсере.
                QuantityColumnsExact = new List<string> { "Длина, мм.", "Длина, мм", "Длина, м.", "Длина, м", "Кол-во, шт." },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м",
                QuantityScale = 0.001
            },
            new Entry
            {
                Code = "LED",
                Title = "LED-лента",
                ScheduleNamesExact = new List<string> { "Спецификация LED-ленты", "Спецификация LED лент" },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Длина, мм.", "Длина, мм", "Длина, м.", "Длина, м" },
                RoomColumnsExact = new List<string> { "Комната, имя", "Помещение", "Помещения" },
                QuantityUnit = "м",
                QuantityScale = 0.001
            },
            new Entry
            {
                Code = "APRON",
                Title = "Фартук кухни",
                ScheduleNamesExact = new List<string>
                {
                    "Спецификация фартука кухни",
                    "Условное обозначение фартука"
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Площадь, м²", "Площадь", "Кол-во, шт" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м²"
            },
            new Entry
            {
                Code = "BATH_TILE",
                Title = "Плитка в ванной",
                ScheduleNamesExact = new List<string>
                {
                    "Условное обозначение плитки в ванной",
                    "Условное обозначение плитки в с/у"
                },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Площадь, м²", "Площадь", "Кол-во, шт" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "м²"
            },
            new Entry
            {
                Code = "LIGHTING",
                Title = "Осветительные приборы",
                ScheduleNamesExact = new List<string> { "Спецификация осветительных приборов" },
                Mode = ParseMode.GroupedByRoomHeader,
                MaterialIdColumnsExact = new List<string> { "ID материала" },
                MaterialNameColumnsExact = new List<string> { "Наименование" },
                QuantityColumnsExact = new List<string> { "Кол-во, шт", "Кол-во", "Число, шт" },
                RoomColumnsExact = new List<string> { "Помещение", "Помещения" },
                QuantityUnit = "шт"
            }
        };
    }
}
