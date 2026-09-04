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

## Наборы (flatten, 2026-08)

Эталон сверки — **шапка + состав** (`items_json` / `set_items`). Иначе SR_ID розеток/рамок (`12133` и т.п.) в Revit помечались extra, хотя это слоты набора `5549`.

- `ClientMaterialTkService.FlattenSets` + parse `items_json`/`items`/`set_items`
- Overlay ДС по `(client_material_id, material_id)`; qty элемента набора — массивы `cnt_material_id_arr` / `cnt_material_cnt_arr`
- JSON-экспорт: блок `tk` (развёрнутый)
- Gold 3071538 локально: `%USERPROFILE%\Desktop\tk_gold_3071538.json` (не в git)

## Заметки по данным

- Электрика без колонки «ID материала» → источник `ELECTRICS` пишет message в status/JSON; сверка присутствия всё равно идёт по SR_ID типов.
- LED: комната может быть в колонке ID (grouped header) — поддержано.
- Состав набора: plugin `tk/read` может не отдать `items_json`; тогда состав берётся из `GET …/ds/{ds}/tk_material/` при привязанной ДС.
