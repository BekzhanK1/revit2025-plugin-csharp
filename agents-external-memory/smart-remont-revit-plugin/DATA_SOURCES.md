# Источники данных

В плагине **три независимых контура** чтения из Revit. Их нельзя смешивать при проектировании фич.

---

## 1. ДС по изменению площади (`DS_AREA_CHANGE`)

| | |
|---|---|
| **Экран** | `SelectedRemontSummaryWindow` |
| **Сервис** | `RoomAreaService` |
| **Revit** | `Room` (категория OST_Rooms) |

### Правила сбора

- Фаза: **«После монтажных работ»**, иначе первая фаза в документе
- Только помещения с `Area > 0` на выбранной фазе
- Площадь: `BuiltInParameter.ROOM_AREA` → м²
- Имя: `ROOM_NAME`, иначе `ROOM_NUMBER`
- Высота по комнате: стены, ограничивающие Room → иначе `ROOM_HEIGHT` / уровни

### Payload

```json
{
  "source": "revit",
  "version": 1,
  "wall_height": 2.7,
  "rooms": [
    { "room_name": "Кухня", "room_area_m2": 12.5 }
  ]
}
```

DTO: `DsAreaChangePayloadDto`, `RemontRoomAreaDto` (`room_name`, `room_area_m2`).

**Зависимость от ведомостей:** нет.

---

## 2. Замеры комнат (`MEASURES`)

| | |
|---|---|
| **Экран** | `RoomMeasurementsWindow` |
| **Сервис** | `RoomMeasurementsService` |
| **Маппинг** | `RoomMeasurementsScheduleMapping` (статический C#) |
| **Revit** | `ViewSchedule.GetTableData()` — тело таблицы |

### Правила сбора

1. Собрать все читаемые `ViewSchedule` (не template, не keynote…)
2. Индекс по **нормализованному имени** ведомости (`trim`, снять `<>`)
3. Для каждого `Entry` в маппинге — найти ведомость по `ScheduleNamesExact` (первое совпадение)
4. Прочитать заголовки строки 0, данные строк 1..
5. Применить `ParseMode` и фильтры комнат (`RoomNameMatcher`)

**Зависимость от ведомостей:** полная (имена, колонки, группировка таблицы).

См. [ROOM_MEASUREMENTS_MAPPING.md](ROOM_MEASUREMENTS_MAPPING.md).

### Payload

```json
{
  "source": "revit",
  "version": 1,
  "rooms": [
    {
      "room_name": "Кухня",
      "parameters": [
        { "param_code": "PLITKA_AREA", "param_name": "...", "param_value": 8.2 }
      ]
    }
  ]
}
```

Пустые `param_value` в отправку не попадают.

---

## 3. Экспорт JSON (файл)

| | |
|---|---|
| **Экран** | `ExportSmartRemontRoomsWindow` |
| **Помещения** | `Room` + пользовательские имена параметров в UI |
| **WorkItems** | Выбранные `ViewSchedule` + `ScheduleMappingConfig` |

### Помещения (`rooms[]`)

Из `Room`:

- `ROOM_AREA`, `ROOM_PERIMETER`, `ROOM_HEIGHT` → AreaM2, PerimeterM, HeightM, WallAreaM2 (= perimeter × height, **брутто**)
- Shared params: ADSK_Номер квартиры, отделки, IfcGUID…
- Опционально: контуры границ, отделка (Floor/Ceiling/GenericModel через `GetRoomAtPoint`)

Это **не** то же самое, что замеры `MEASURES` (другие величины и правила).

### WorkItems (`workItems[]`)

Пользователь в DataGrid указывает:

- какие ведомости включены;
- `Discipline`, `WorkType` (SR-разделы);
- имена колонок для материала, количества, помещения…

Сохраняется в `{outputBasename}.mapping.json`.

---

## Сопоставление «можно ли брать из Room как площади»

| param_code (замеры) | Из Room напрямую | Комментарий |
|---------------------|------------------|-------------|
| — | `ROOM_AREA` | уже используется в ДС, не в MEASURES |
| `PERIMETER_ROOF` | `ROOM_PERIMETER` | часто ≠ «периметр потолка» в ведомости |
| `WALL_AREA_MINUS` | perimeter×height | брутто, без вычета проёмов |
| `PERIMETER_FLOOR`, `MOLDING_*` | только эвристики по элементам | нет 1:1 с ведомостью |
| `PLITKA_AREA`, `DOOR_CNT`, … | сложная логика | дублировать правила ведомости |

План сверки «ведомость vs модель» без смены источника отправки: [ROADMAP.md](ROADMAP.md).

---

## Сопоставление имён помещений

`RoomNameMatcher`:

- «Кухня №2», «Спальня 2» → базовое имя «Кухня» / «Спальня»
- Используется в фильтрах `RoomBaseNamesFilter` / `Exclude` при парсинге ведомостей
- Используется при сборке строк замеров (`ParamAppliesToRoom`)
