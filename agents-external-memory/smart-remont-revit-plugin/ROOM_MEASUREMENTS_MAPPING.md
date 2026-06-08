# Маппинг замеров из ведомостей (MEASURES)

**Код:** `SBS/Services/RoomMeasurementsScheduleMapping.cs`  
**Парсер:** `SBS/Services/RoomMeasurementsService.cs`  
**Модели:** `SBS/Models/RoomMeasurementsModels.cs`

---

## Принцип

Каждый измеряемый показатель = `Entry`:

| Поле | Назначение |
|------|------------|
| `ParamCode` / `ParamName` | Код и название для API (`param_code`, `param_name`) |
| `ScheduleNamesExact` | Список **точных** имён `ViewSchedule` (пробуются по порядку) |
| `Mode` | Алгоритм чтения таблицы |
| `ValueColumnsExact` | Заголовки колонки со значением (точное совпадение, без Contains) |
| `RoomColumnsExact` | Заголовки колонки «помещение» |
| `FixedRoomName` | Для режима без колонки комнаты |
| `RoomBaseNamesFilter` | Только помещения с базовым именем из списка |
| `RoomBaseNamesExclude` | Исключить базовые имена |
| `ValueIsInteger` | Целое значение (двери) |
| `IsMergedParameter` | Не парсится в основном цикле; собирается отдельно |

### Поиск ведомости

```text
NormalizeName(name) = trim, убрать символы < >
Словарь: normalizedName → ViewSchedule (при дубликатах — First)
```

Сообщение об ошибке: «точное имя без < >».

### Чтение таблицы

- `schedule.GetTableData()` → `SectionType.Body`
- Строка 0 — заголовки колонок
- Строки с текстом «общий итог» пропускаются
- Числа: regex `[-+]?\d+(?:[.,]\d+)?`, invariant culture

---

## Режимы парсинга (`ParseMode`)

### `FlatByRoomColumn`

Каждая строка: колонка комнаты + колонка значения → сумма по имени комнаты.

### `GroupedByRoomHeader`

Группировка Revit: строка только с именем комнаты (без числа) задаёт `currentRoom`; следующие строки с числом относятся к ней.  
Дополнительно: `DetectGroupRoomName` — имя комнаты из другой колонки строки-заголовка.

### `DoorsByRoom`

Подсчёт дверей по строкам:

- колонка «Помещение» обязательна;
- опционально «Тип», «Ширина полотна…»;
- qty из «Кол-во, шт» или 1 по умолчанию;
- **`DOUBLE_DOOR`:** `ParamCode == "DOUBLE_DOOR"` → только двери с шириной > 1000 мм или «Дв.»/«двуств» в типе.

### `SingleValueToFixedRoom`

Сумма всех чисел в колонке площади → одно помещение `FixedRoomName` (фартук → «Кухня»).

---

## Таблица параметров (текущий маппинг)

| param_code | param_name (кратко) | Ведомость (ScheduleNamesExact) | Mode | Value columns | Room columns | Фильтры |
|------------|---------------------|--------------------------------|------|---------------|--------------|---------|
| `PERIMETER_FLOOR` | Периметр по полу (− двери/проёмы) | Спецификация плинтуса. | GroupedByRoomHeader | Длина, м. | Помещение, Помещения | — |
| `PERIMETER_ROOF` | Периметр по потолку (полный) | Спецификация потолков. | FlatByRoomColumn | Периметр. | Помещения, Помещение | — |
| `WALL_AREA_MINUS` | Площадь стен − проёмы | *(merged, см. ниже)* | — | — | — | — |
| `PLITKA_AREA` | Плитка прихожая/кухня | Спецификация напольных плиток | GroupedByRoomHeader | Площадь, м² | Помещение, Помещения | только Прихожая, Кухня |
| `DOOR_CNT` | Межкомнатные двери | Спецификация дверей | DoorsByRoom | Кол-во, шт | Помещение | — |
| `APRON_AREA` | Фартук кухни | Спецификация фартука кухни | SingleValueToFixedRoom | Площадь, Площадь, м² | — | FixedRoomName=Кухня |
| `MOLDING_PERIMETER` | Периметр молдингов | Спецификация молдингов / …молдингов. | GroupedByRoomHeader | Длина, м., Периметр. | Помещение, Помещения | — |
| `DOUBLE_DOOR` | Двустворчатая дверь | Спецификация дверей | DoorsByRoom + doubleLeafOnly | Кол-во, шт | Помещение | только Гостиная |

---

## WALL_AREA_MINUS (составной)

`IsMergedParameter = true` в основном списке — только для UI/метаданных.

Сбор в `ExtractWallAreaMinus` из двух частей `WallAreaMinusSources`:

| Часть | Ведомость | Mode | Value | Room | Фильтр |
|-------|-----------|------|-------|------|--------|
| Interior | Спецификация поклейка обоев с покраской | GroupedByRoomHeader | Площадь, м² | Помещение, Помещения | **исключить** Балкон |
| Balcony | Спецификация краски для стен балкона | FlatByRoomColumn | Площадь, м² / Площадь | Помещение, Помещения | **только** Балкон |

Результаты **складываются** в один `ByRoom` по `param_code` `WALL_AREA_MINUS`.

В `Sources` два отдельных сообщения: «(остальные помещения)» и «(балкон)».

---

## Сборка итоговой таблицы помещений

1. После парсинга — объединение всех имён комнат из всех `ExtractResult`
2. Для каждого имени — список параметров из `All`, где `ParamAppliesToRoom`:
   - если `RoomBaseNamesFilter` — только подходящие базовые имена;
   - если `FixedRoomName` — только совпадение с фиксированным;
   - иначе — все комнаты с данными

3. Значение: из `ByRoom` (double) или `ByRoomInt` (int)

---

## UI: блок «Источники»

`RoomMeasurementSourceInfo` на каждый обработанный источник:

- `schedule_name_expected` — ожидаемые имена через ` | `
- `schedule_name_found` — фактическое имя или «—»
- `Found` — ведомость найдена и есть данные
- `Message` — колонки, фильтры, причина пустоты

---

## Известные хрупкости

1. Переименование ведомости или колонки → параметр пустой.
2. Смена группировки в Revit → неверный `ParseMode`.
3. Две ведомости с одним нормализованным именем → берётся первая.
4. `SPECIFICATION_CODE` **не используется** (обсуждалось — см. ROADMAP).
5. Одна ведомость «Спецификация дверей» для `DOOR_CNT` и `DOUBLE_DOOR` — намеренно.

---

## Файлы для изменения маппинга

| Задача | Файл |
|--------|------|
| Новый param_code / ведомость | `RoomMeasurementsScheduleMapping.cs` |
| Логика парсинга | `RoomMeasurementsService.cs` |
| Отображение / отправка | `RoomMeasurementsWindow.xaml.cs`, `RevitEventsService.cs` |
| DTO API | `RevitEventDtos.cs` |

После добавления `Compile` в `SBS.csproj` (`EnableDefaultItems=false`).
