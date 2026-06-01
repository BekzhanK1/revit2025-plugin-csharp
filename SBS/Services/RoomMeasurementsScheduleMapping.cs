using System.Collections.Generic;

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
            public string ParamCode { get; init; }
            public string ParamName { get; init; }
            /// <summary>Первое имя — для подсказки в UI; все варианты пробуются при поиске.</summary>
            public IReadOnlyList<string> ScheduleNamesExact { get; init; }
            public ParseMode Mode { get; init; }
            public IReadOnlyList<string> ValueColumnsExact { get; init; }
            public IReadOnlyList<string> RoomColumnsExact { get; init; }
            public string FixedRoomName { get; init; }
            public bool ValueIsInteger { get; init; }
            /// <summary>Если задано — параметр только для помещений с этим базовым именем (Кухня №2 → Кухня).</summary>
            public IReadOnlyList<string> RoomBaseNamesFilter { get; init; }
            /// <summary>Исключить помещения с этим базовым именем (например балкон из обоев).</summary>
            public IReadOnlyList<string> RoomBaseNamesExclude { get; init; }
            /// <summary>Составной параметр — читается отдельно (WALL_AREA_MINUS).</summary>
            public bool IsMergedParameter { get; init; }
        }

        /// <summary>WALL_AREA_MINUS: обои — все комнаты кроме балкона; балкон — отдельная ведомость.</summary>
        public static class WallAreaMinusSources
        {
            public static Entry Interior { get; } = new Entry
            {
                ScheduleNamesExact = new[] { "Спецификация поклейка обоев с покраской" },
                Mode = ParseMode.GroupedByRoomHeader,
                ValueColumnsExact = new[] { "Площадь, м²" },
                RoomColumnsExact = new[] { "Помещение", "Помещения" },
                RoomBaseNamesExclude = new[] { "Балкон" }
            };

            public static Entry Balcony { get; } = new Entry
            {
                ScheduleNamesExact = new[] { "Спецификация краски для стен балкона" },
                Mode = ParseMode.FlatByRoomColumn,
                ValueColumnsExact = new[] { "Площадь, м²", "Площадь" },
                RoomColumnsExact = new[] { "Помещение", "Помещения" },
                RoomBaseNamesFilter = new[] { "Балкон" }
            };
        }

        public static IReadOnlyList<Entry> All { get; } = new List<Entry>
        {
            new Entry
            {
                ParamCode = "PERIMETER_FLOOR",
                ParamName = "Периметр по полу (за минусом дверей и проемов)",
                ScheduleNamesExact = new[] { "Спецификация плинтуса." },
                Mode = ParseMode.GroupedByRoomHeader,
                ValueColumnsExact = new[] { "Длина, м." },
                RoomColumnsExact = new[] { "Помещение", "Помещения" }
            },
            new Entry
            {
                ParamCode = "PERIMETER_ROOF",
                ParamName = "Периметр по потолку (полный)",
                ScheduleNamesExact = new[] { "Спецификация потолков." },
                Mode = ParseMode.FlatByRoomColumn,
                ValueColumnsExact = new[] { "Периметр." },
                RoomColumnsExact = new[] { "Помещения", "Помещение" }
            },
            new Entry
            {
                ParamCode = "WALL_AREA_MINUS",
                ParamName = "Площадь стен за минусом площади дверей, проемов и окон",
                ScheduleNamesExact = new[]
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
                ScheduleNamesExact = new[] { "Спецификация напольных плиток" },
                Mode = ParseMode.GroupedByRoomHeader,
                ValueColumnsExact = new[] { "Площадь, м²" },
                RoomColumnsExact = new[] { "Помещение", "Помещения" },
                RoomBaseNamesFilter = new[] { "Прихожая", "Кухня" }
            },
            new Entry
            {
                ParamCode = "DOOR_CNT",
                ParamName = "Количество межкомнатных дверей",
                ScheduleNamesExact = new[] { "Спецификация дверей" },
                Mode = ParseMode.DoorsByRoom,
                ValueColumnsExact = new[] { "Кол-во, шт" },
                RoomColumnsExact = new[] { "Помещение" },
                ValueIsInteger = true
            },
            new Entry
            {
                ParamCode = "APRON_AREA",
                ParamName = "Площадь фартука на кухне",
                ScheduleNamesExact = new[] { "Спецификация фартука кухни" },
                Mode = ParseMode.SingleValueToFixedRoom,
                FixedRoomName = "Кухня",
                ValueColumnsExact = new[] { "Площадь", "Площадь, м²" }
            },
            new Entry
            {
                ParamCode = "MOLDING_PERIMETER",
                ParamName = "Периметр молдингов",
                ScheduleNamesExact = new[] { "Спецификация молдингов", "Спецификация молдингов." },
                Mode = ParseMode.GroupedByRoomHeader,
                ValueColumnsExact = new[] { "Длина, м.", "Периметр." },
                RoomColumnsExact = new[] { "Помещение", "Помещения" }
            },
            new Entry
            {
                ParamCode = "DOUBLE_DOOR",
                ParamName = "Двустворчатая дверь",
                ScheduleNamesExact = new[] { "Спецификация дверей" },
                Mode = ParseMode.DoorsByRoom,
                ValueColumnsExact = new[] { "Кол-во, шт" },
                RoomColumnsExact = new[] { "Помещение" },
                ValueIsInteger = true,
                RoomBaseNamesFilter = new[] { "Гостиная" }
            }
        };
    }
}
