# Экспорт спецификаций в JSON (WorkItems)

**Окно:** `ExportSmartRemontRoomsWindow`  
**DTO конфига:** `SBS/DTO/ScheduleMappingConfig.cs`  
**Итоговый JSON:** `SBS/DTO/SmartRemontRoomsExportDto.cs`

> Этот контур **не связан** с отправкой `MEASURES` на API. Разные цели, разный маппинг.

---

## Назначение

Локальный файл для backend/интеграций:

- `rooms[]` — карточки помещений из модели
- `workItems[]` — плоский список материалов/работ из **выбранных пользователем** ведомостей

---

## Поток

1. При открытии окна: `LoadSchedules()` — все exportable `ViewSchedule` → строки `ScheduleMappingRowVm`
2. `LoadMappingIfExists()` — если рядом с путём выхода есть `{basename}.mapping.json`, подставить настройки
3. По экспорту: `SaveMapping()` → записать JSON маппинга
4. `ExportWorkItemsFromSelectedSchedules()` — только `IsEnabled == true`
5. Для каждой строки: `ReadScheduleWorkItems(schedule, row)`

Чтение таблицы: `GetTableData()`, `SectionType.Body`, первая строка — заголовки.

---

## ScheduleMappingConfig

```csharp
public class ScheduleMappingConfig
{
    public List<ScheduleMapping> Schedules { get; set; }
}

public class ScheduleMapping
{
    public string ScheduleName;      // имя ViewSchedule
    public bool IsEnabled;
    public string Discipline;        // SR-раздел: Floors, Ceilings, WallPaint, ...
    public string WorkType;          // произвольная метка
    public string ColMaterialName;
    public string ColMaterialCode;
    public string ColQuantity;
    public string ColUnit;
    public string ColRoomName;
    public string ColRoomNumber;
    public string ColApartment;
}
```

Ключ при загрузке: **имя ведомости** (case-insensitive).

---

## ScheduleMappingRowVm (UI)

Поля зеркалят `ScheduleMapping` + `ScheduleName` для отображения.

Дефолты при первом открытии (если нет mapping.json):

- `ColQuantity` часто «Площадь, м²»
- Discipline / WorkType — пустые, пользователь заполняет

---

## SmartRemontWorkItemDto (в rooms export)

Типичные поля (см. `SmartRemontRoomsExportDto.cs`):

- привязка к помещению: RoomName, RoomNumber, ApartmentNumber
- MaterialName, MaterialCode, Quantity, Unit
- Discipline, WorkType
- ScheduleName (источник)

Правило из плана: одна спецификация → несколько строк с одинаковым заголовком колонки — **берётся первая**.

---

## Отличие от RoomMeasurementsScheduleMapping

| | MEASURES (замеры) | Export WorkItems |
|---|-------------------|------------------|
| Конфиг | Статический C# | `*.mapping.json` + UI |
| Выбор ведомостей | Жёсткий список имён | Checkbox в DataGrid |
| Выход | API `revit_events` | JSON файл |
| param_code | Фиксированный набор | Нет; Discipline/WorkType |
| Парсинг | 4 специализированных режима | Универсальные колонки материала/количества |

---

## Статус в продукте

`ExportSmartRemontRoomsCommand` **не открывает** это окно — только Home → RemontHub.

Окно и логика **сохранены в репозитории** для полного экспорта / будущего возврата в ленту.

План из `.cursor/plans/mapping_specification.md` — исторический чеклист реализации UI.
