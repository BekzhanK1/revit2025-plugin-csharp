using System.Collections.Generic;

namespace SmartRemont.ExportRooms.DTO
{
    public class WallInfoDto
    {
        public int Id { get; set; } //айди стены в проекте
        public string BaseConstraint { get; set; } //Зависимость снизу
        public string TopConstraint { get; set; } //Зависимость сверху
        public double BaseOffset { get; set; } //Смещение снизу
        public double TopOffset { get; set; } //Смещение сверху
        public string BiLevel { get; set; } //BI_этаж
        public string Mark { get; set; } //Марка
        public double UnconnectedHeight { get; set; } //Неприсоединенная высота
        public double Length { get; set; } //Длина
        public double Area { get; set; } //Площадь
        public double Volume { get; set; } //Объем
        public string TypeName { get; set; } //Имя типа
        public string WallKind { get; set; } //Тип стены
        public string ModelGroup { get; set; } //Группа модели
        public double WallWidth { get; set; } //Общая толщина стены
        public List<WallStructureDto> Layers { get; set; } //Структура стены
        public List<WallLinesDto> WallLines { get; set; } //Составляющие линии стены
    }
}
