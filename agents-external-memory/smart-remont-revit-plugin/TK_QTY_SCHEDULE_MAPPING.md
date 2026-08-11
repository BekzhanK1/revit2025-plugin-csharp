# ТК qty из ведомостей

Конфиг как у замеров: ведомость → `material_id` + qty по комнате → сверка с `material_cnt` ТК.

## Файлы

| Файл | Назначение |
|------|------------|
| `SBS/Services/TkQtyScheduleMapping.cs` | Конфиг + дефолты |
| `SBS/Services/TkQtyScheduleService.cs` | Парсер ViewSchedule |
| `%AppData%\SmartRemont\RevitPlugin\tk_qty_schedule_mappings.json` | Runtime-конфиг |

## Entry

```json
{
  "Code": "DOORS",
  "Title": "Двери",
  "ScheduleNamesExact": ["Спецификация дверей"],
  "Mode": "FlatByRoomColumn",
  "MaterialIdColumnsExact": ["ID материала"],
  "MaterialNameColumnsExact": ["Наименование"],
  "QuantityColumnsExact": ["Кол-во, шт"],
  "RoomColumnsExact": ["Помещение"],
  "QuantityUnit": "шт",
  "QuantityScale": 1,
  "Enabled": true
}
```

`Mode`: `FlatByRoomColumn` | `GroupedByRoomHeader`  
`QuantityScale`: мм→м = `0.001`

## Сверка

`DsTkCompareService.Compare(..., scheduleQty)`:

- `tk_qty` ← сумма `material_cnt` по комнате+material_id
- `schedule_qty` ← сумма из ведомостей
- статусы qty: `qty_match` / `qty_mismatch` / `qty_tk_only` / `qty_schedule_only`
- ТК-строки с `material_cnt = 0` без SR_ID → `not_expected` (не «нет в Revit»)

UI: колонки «ТК qty», «Вед. qty», «Объём». JSON сводки — те же поля + блок `schedule_qty.sources`.
