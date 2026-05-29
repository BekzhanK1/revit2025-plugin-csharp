namespace SmartRemont.ExportRooms.DTO
{
    public class ParameterDto
    {
        public string Name { get; set; }
        public string Value { get; set; } // Строковое представление
        public string ValueType { get; set; } // Тип данных (String, Double, Integer, ElementId и т.д.)
        public string StorageType { get; set; } // Тип хранения
        public string GroupName { get; set; } // Группа параметра
        public bool IsReadOnly { get; set; }
        public bool IsShared { get; set; }
        public string Unit { get; set; } // Единица измерения
    }
}

