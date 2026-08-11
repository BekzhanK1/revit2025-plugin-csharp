# ДС изменение ТК — сверка + bind черновика

## V1 bind

- `DsTkChangeService` — list/create/read office DS API (`TK_CHANGE`)
- Окно сверки: бейдж ДС, **Создать ДС**, **Выбрать…** (`DsTkPickWindow`)
- Хаб: бейдж статуса TK_CHANGE (не создана / черновик / согласование / …)

Правки материалов/qty в ДС — следующий этап.

## Контекст

Сверка `SR_ID` ↔ `material_id` + опционально объёмы из ведомостей vs `material_cnt` ТК.

## Итерации правок (harden)

1. **`qty_tk_only` больше не «проблема»** — иначе фильтр «Только расхождения» забивался всеми материалами без строки в ведомости.
2. **Orphan-строки только из ведомости** (нет ТК и нет SR_ID) не попадают в diff.
3. **Допуск qty**: абсолютный `0.05` + относительный `1%` (площади/длины).
4. **Парсер ведомостей**: нормализация заголовков, skip «Итого»/Total, авто scale мм→м / сброс scale для «шт», reload конфига на каждый Collect, merge новых Code в существующий AppData JSON.
5. **UI**: бейдж `qty ≠`, подсветка mismatch, подсказки по failed sources в статусе, `QtyUnit` в строке/JSON.

## Файлы

| Файл | Назначение |
|------|------------|
| `SBS/Services/DsTkCompareService.cs` | Diff + qty |
| `SBS/Services/TkQtyScheduleMapping.cs` | Конфиг `%AppData%\...\tk_qty_schedule_mappings.json` |
| `SBS/Services/TkQtyScheduleService.cs` | Чтение ViewSchedule |
| `SBS/Views/DsTkChangeWindow.xaml(.cs)` | UI |

## Заметки по данным

- Электрика без колонки «ID материала» → источник `ELECTRICS` пишет message в status/JSON; сверка присутствия всё равно идёт по SR_ID типов.
- LED: комната может быть в колонке ID (grouped header) — поддержано.
