using System.Collections.Generic;

namespace SBS.DTO
{
    public class RevitElementDto
    {
        public int Id { get; set; }
        public string Category { get; set; } // Категория элемента (Стены, Двери, Окна и т.д.)
        public string FamilyName { get; set; } // Имя семейства
        public string TypeName { get; set; } // Имя типа
        public string UniqueId { get; set; } // Уникальный ID
        public Dictionary<string, ParameterDto> Parameters { get; set; } // Все параметры
        public List<GeometryLineDto> Geometry { get; set; } // Геометрия
        public BoundingBoxDto BoundingBox { get; set; } // Габариты
    }
}

