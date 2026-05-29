---
name: schedule-material-export
overview: "Расширить существующее окно экспорта помещений: добавить секцию выбора спецификаций (checkbox), настраиваемый маппинг колонок и категорий SR, читать данные напрямую из Revit API, экспортировать в единый JSON. Маппинг персистировать в `*.mapping.json` рядом с файлом экспорта."
todos:
  - id: dto-mapping
    content: Создать SBS/DTO/ScheduleMappingConfig.cs + добавить WorkItems в SmartRemontRoomsExportDto
    status: completed
  - id: vm-row
    content: Добавить ScheduleMappingRowVm в ExportSmartRemontRoomsWindow.xaml.cs
    status: in_progress
  - id: xaml-section
    content: Добавить секцию DataGrid в XAML между "Фильтры" и "Файл"
    status: pending
  - id: logic-export
    content: Реализовать чтение ViewSchedule через GetTableData(), сборку WorkItems, загрузку/сохранение mapping.json
    status: pending
  - id: verify-build
    content: Собрать проект, проверить ошибки
    status: pending
isProject: false
---

# Расширение ExportSmartRemontRoomsWindow: спецификации + маппинг

## Архитектурное решение

Единый JSON на выходе: `rooms[]` + `workItems[]` (плоский список), в каждом `WorkItemDto` поля `Discipline` (SR-раздел) и `WorkType` (тип внутри раздела). Маппинг "какая спецификация куда" — внешний JSON-конфиг.

Данные берём **из Revit API напрямую** через `ViewSchedule.GetTableData()` — не из CSV, нет проблем с кодировкой.

```
mermaid
flowchart TD
    Command["ExportSmartRemontRoomsCommand"] --> Window["ExportSmartRemontRoomsWindow"]
    Window -->|"load/save"| MappingJson["*.mapping.json\n(рядом с output)"]
    Window -->|"GetTableData()"| RevitAPI["Revit ViewSchedule API"]
    Window -->|"FilteredElementCollector"| Rooms["Room elements"]
    Window -->|"serialize"| OutputJson["SmartRemont_Rooms_....json\n{ rooms[], workItems[] }"]
```

## Изменяемые файлы

### 1. Новый DTO конфига маппинга — `SBS/DTO/ScheduleMappingConfig.cs` (новый файл)

```csharp
public class ScheduleMappingConfig
{
    public List<ScheduleMapping> Schedules { get; set; } = new();
}

public class ScheduleMapping
{
    public string ScheduleName  { get; set; }  // имя ViewSchedule в Revit
    public bool   IsEnabled     { get; set; }  // включена ли в экспорт
    public string Discipline    { get; set; }  // SR-раздел: "Floors","Ceilings","WallPaint","Wallpaper","FloorTile","WallTile","Baseboard","Molding","Adhesives","Grout","Primer","Doors","Windows","Electrical","Plumbing"
    public string WorkType      { get; set; }  // произвольная метка (можно пусто)
    public string ColMaterialName { get; set; }   // название колонки → MaterialName
    public string ColMaterialCode { get; set; }   // → MaterialCode
    public string ColQuantity     { get; set; }   // → Quantity
    public string ColUnit         { get; set; }   // → Unit
    public string ColRoomName     { get; set; }   // → RoomName (опционально)
    public string ColRoomNumber   { get; set; }   // → RoomNumber (опционально)
    public string ColApartment    { get; set; }   // → ApartmentNumber (опционально)
}
```

### 2. Расширить итоговый DTO — `SBS/DTO/SmartRemontRoomsExportDto.cs`

Добавить в `SmartRemontRoomsExportDto`:

```csharp
public List<SmartRemontWorkItemDto> WorkItems { get; set; } = new();
```

`SmartRemontWorkItemDto` уже есть в `SmartRemontScheduleExportDto.cs` — вынести в общий файл или добавить `using`.

### 3. Новый VM для строки маппинга — в `ExportSmartRemontRoomsWindow.xaml.cs`

```csharp
public class ScheduleMappingRowVm : INotifyPropertyChanged
{
    public string ScheduleName { get; set; }   // readonly, из Revit
    public bool   IsEnabled    { get; set; }
    public string Discipline   { get; set; }   // ComboBox: список SR-разделов
    public string WorkType     { get; set; }
    // Колонки (TextBox-ы):
    public string ColMaterialName { get; set; }
    public string ColMaterialCode { get; set; }
    public string ColQuantity     { get; set; }
    public string ColUnit         { get; set; }
    public string ColRoomName     { get; set; }
    public string ColApartment    { get; set; }
}
```

### 4. XAML — `ExportSmartRemontRoomsWindow.xaml`

Добавить новую секцию (карточку `Card`) **между "Фильтры" и "Файл"**:

- Заголовок "Спецификации и маппинг"
- `DataGrid` с колонками:
  - `CheckBox` IsEnabled
  - `TextBlock` ScheduleName (readonly)
  - `ComboBox` Discipline (список из 15 SR-разделов)
  - `TextBox` WorkType
  - `TextBox` ColMaterialName / ColQuantity / ColUnit / ColRoomName / ColApartment (5 колонок)
- Кнопка "Выбрать все / Снять все"
- Нота: "Заголовки колонок берутся из первой строки спецификации"

### 5. Логика в `ExportSmartRemontRoomsWindow.xaml.cs`

**Загрузка:**

- При открытии окна: загрузить все `ViewSchedule` (IsExportable), создать `ScheduleMappingRowVm` для каждой
- Проверить наличие `*.mapping.json` рядом с выходным путём и подгрузить сохранённые значения

**Сохранение конфига:**

```
При клике "Экспортировать":
  1. Сериализовать все ScheduleMappingRowVm → ScheduleMappingConfig → файл <output_basename>.mapping.json
  2. Собрать помещения (как сейчас)
  3. Для каждой включённой спецификации:
     - ViewSchedule.GetTableData().GetSectionData(SectionType.Body)
     - Прочитать строку заголовков (нулевая строка тела)
     - Построить headerIndex: Dictionary<string,int>
     - Пройти строки, пропустить пустые и итоги
     - Создать SmartRemontWorkItemDto, заполнить по маппингу
     - Quantity: парсить с CultureInfo("ru-RU") + fallback InvariantCulture
     - RawValues: все ячейки → { заголовок: значение }
  4. Объединить rooms[] + workItems[] в один SmartRemontRoomsExportDto → JSON
```

**Маппинг конфига к файлу:**

- Имя: `Path.ChangeExtension(outputPath, null) + ".mapping.json"` — т.е. `SmartRemont_Rooms_20260318.mapping.json` рядом с `SmartRemont_Rooms_20260318.json`
- При следующем открытии окна: ищем файл маппинга в той же папке и автоматически применяем настройки

## Файлы и их роли

- `SBS/DTO/ScheduleMappingConfig.cs` — **новый**: конфиг маппинга
- `SBS/DTO/SmartRemontRoomsExportDto.cs` — добавить `WorkItems` в корневой DTO
- `SBS/Views/ExportSmartRemontRoomsWindow.xaml` — добавить секцию DataGrid со спецификациями
- `SBS/Views/ExportSmartRemontRoomsWindow.xaml.cs` — `ScheduleMappingRowVm`, загрузка/сохранение маппинга, чтение `ViewSchedule`, сборка `WorkItemDto`
- `SBS/Commands/ExportSmartRemontRoomsCommand.cs` — **не меняется**

## Edge cases

- Одна и та же спецификация → несколько строк с одинаковым именем колонки: берём первую
- Quantity содержит пробелы или единицы (`"3,5 м²"`): парсим только числовую часть через regex
- Строки-заголовки групп (italic/bold): пропускать строки, где нет значения в колонке MaterialName и Quantity одновременно
- Если маппинга нет — Discipline и колонки пустые, пользователь заполняет вручную и сохраняет

