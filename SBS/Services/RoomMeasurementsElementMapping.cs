using System.Collections.Generic;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// param_code → алгоритм чтения из модели Revit (не из ведомостей).
    /// </summary>
    public static class RoomMeasurementsElementMapping
    {
        public sealed class Entry
        {
            public string ParamCode { get; init; }
            public string ParamName { get; init; }
            public string SourceDescription { get; init; }
        }

        public static Entry PerimeterFloor { get; } = new Entry
        {
            ParamCode = "PERIMETER_FLOOR",
            ParamName = "Периметр по полу (за минусом дверей и проемов)",
            SourceDescription = "контур Finish (или ROOM_PERIMETER) − ширина дверей FromRoom/ToRoom"
        };

        public static Entry PerimeterRoof { get; } = new Entry
        {
            ParamCode = "PERIMETER_ROOF",
            ParamName = "Периметр по потолку (полный)",
            SourceDescription = "контур Finish или ROOM_PERIMETER (без вычета дверей)"
        };

        public static Entry DoorCnt { get; } = new Entry
        {
            ParamCode = "DOOR_CNT",
            ParamName = "Количество межкомнатных дверей",
            SourceDescription = "двери FromRoom/ToRoom; без проёмов (тип «Проем» в семействе)"
        };

        public static Entry DoubleDoor { get; } = new Entry
        {
            ParamCode = "DOUBLE_DOOR",
            ParamName = "Двустворчатая дверь",
            SourceDescription = "только Гостиная: ширина > 1000 мм или «Дв.» в типе; без проёмов"
        };

        public static Entry PlitkaArea { get; } = new Entry
        {
            ParamCode = "PLITKA_AREA",
            ParamName = "Плитка в прихожей или кухне",
            SourceDescription = "полы (Floors): Модель = «Напольная плитка»; только Прихожая, Кухня"
        };

        public static Entry ApronArea { get; } = new Entry
        {
            ParamCode = "APRON_AREA",
            ParamName = "Площадь фартука на кухне",
            SourceDescription = "стены/витражи (Walls): ERBO_Помещения = «Кухня»"
        };

        public static Entry WallAreaMinus { get; } = new Entry
        {
            ParamCode = "WALL_AREA_MINUS",
            ParamName = "Площадь стен за минусом площади дверей, проемов и окон",
            SourceDescription = "ROOM_PERIMETER × высота − ERBO_Площадь (двери/окна)"
        };

        public static IReadOnlyList<Entry> All { get; } = new[]
        {
            PerimeterFloor,
            PerimeterRoof,
            DoorCnt,
            DoubleDoor,
            PlitkaArea,
            ApronArea,
            WallAreaMinus
        };
    }
}
