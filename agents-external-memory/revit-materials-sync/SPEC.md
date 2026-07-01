# SPEC — ближний скоуп (без MinIO/S3, фиктивные ссылки)

> Цель спринта: по `client_request_id` (или `remont_id`) отдать JSON материалов ремонта с полями
> для Revit (`revit_file_type`, `revit_file_url`, `revit_file_hash`, `revit_asset_name`).
> Реальное хранилище (MinIO) и `.rfa`-семейства — **позже**, отдельная фаза (см. `PLAN.md` §5–7).
> Сейчас ссылки — **фиктивные** (placeholder), но по реальным правилам классификации, чтобы
> контракт API/плагина не менялся при переходе на MinIO.

## 1. Флоу (как в презентации, уточнённые термины)

```
1. client_request_id / remont_id  → точка входа
2. ТК (client_material_tab)       → список материалов ремонта (НЕ "по договору")
3. material_tab.revit_*           → тип файла, url, hash, имя ассета
4. Плагин сверяет hash с локальным кэшем → скачивает только изменившееся
5. Подгрузка в открытый Revit-проект (клиентская логика, БД не касается)
```

## 2. Новые поля `material_tab`

| Поле | Тип | Nullable | Описание |
|------|-----|----------|----------|
| `revit_file_type` | `varchar(20)` | `NOT NULL DEFAULT 'none'` | `'rfa'` — 3D-объект, `'surface'` — поверхность (общий `.rvt`/библиотека материалов), `'none'` — не нужен в Revit |
| `revit_file_url` | `varchar(500)` | NULL | Ссылка на файл. Для `'rfa'` — на конкретный `.rfa`/семейство. Для `'surface'` — на общий реестр/библиотеку (пока placeholder). Для `'none'` — всегда NULL |
| `revit_file_hash` | `varchar(64)` | NULL | Отпечаток файла (кэш, шаг 4). Пока `md5(revit_file_url)` — фиктивный, но стабильный |
| `revit_asset_name` | `varchar(200)` | NULL | Имя семейства в `.rfa` или имя материала в общем `.rvt`/библиотеке |

**Category в `.rfa` уже зашита внутри файла** — отдельное поле `revit_category` не нужно (решение зафиксировано в `decisions/DECISIONS.md`).

### Классификация по `material_type_id` (для заполнения, task-04)

| Группа | `revit_file_type` | `material_type_code` (примеры) |
|--------|--------------------|----------------------------------|
| Поверхности | `surface` | `TILE`, `PAINT`, `LAMINAT`, `PAPER` |
| 3D-объекты | `rfa` | `INTERNAL_DOOR`, `SINK`, `TOILET`, `BATH_FURNITURE`, `FAUCET`, `SWITCH`, `SHOWER CABINS`, `HEATED TOWEL RAIL`, `BATH`, `MEBEL`, `MOLDING`, `PLINTUS`, `OVERALL_LAMPS`, `BUILT-IN_LIGHTING` |
| Не нужны | `none` | `SERVICE`, `ROUGHING`, `TEXTILE`, бытовая техника (`FRIDGE`, `TV`, `LAPTOP`) — уточнить полный список при реализации |

Точную границу списков зафиксировать в SQL task-04 через `material_type_tab.material_type_code`, не хардкодить `material_type_id` в нескольких местах.

## 3. Новая SQL-функция

`public.read_revit_material_by_remont(cur refcursor, remont_id_ integer) RETURNS refcursor`

Отличия от `read_client_material_by_remont`:
- Источник строк — `client_material_tab` по `client_request_id`, но **дедуп по `material_id`** (плагину не нужны дубликаты по комнатам — семейство/материал грузится один раз).
- Фильтр `material_tab.revit_file_type <> 'none'`.
- Отдаёт только Revit-релевантные поля + минимальный контекст (`material_id`, `material_name`, `material_type_id`, `material_type_code`).
- Тот же паттерн resolve `remont_id → client_request_id` через `utils.get_client_request_id_by_remont`, тот же shape ответа (`remont_id`, `client_request_id`, `data`).

```sql
-- псевдокод тела
SELECT DISTINCT ON (cm.material_id)
  m.material_id,
  m.material_name,
  m.material_type_id,
  mt.material_type_code,
  m.revit_file_type,
  m.revit_file_url,
  m.revit_file_hash,
  m.revit_asset_name
FROM client_material_tab cm
JOIN material_tab m ON m.material_id = cm.material_id
JOIN material_type_tab mt ON mt.material_type_id = m.material_type_id
WHERE cm.client_request_id = client_request_id_
  AND m.revit_file_type <> 'none'
ORDER BY cm.material_id;
```

## 4. Новый API-эндпоинт

`GET /revit/material/read/?remont_id={remont_id}`

Паттерн — как `RevitEventView` (`common/ex_views/revit_event_views.py`): `ViewSet` + `call_an_sp`.

### Ответ (по аналогии с `client_material/tk/read`)

```json
{
  "status": true,
  "error": null,
  "remont_id": 12345,
  "client_request_id": 2995240,
  "data": [
    {
      "material_id": 5001,
      "material_name": "Ламинат Quick-Step",
      "material_type_id": 11,
      "material_type_code": "LAMINAT",
      "revit_file_type": "surface",
      "revit_file_url": "https://placeholder.smartremont.kz/revit/surface/LAMINAT.rvt",
      "revit_file_hash": "e99a18c428cb38d5f260853678922e03",
      "revit_asset_name": "LAMINAT_5001"
    }
  ]
}
```

### Ошибки — как у `RevitEventView.read_status`

| Ситуация | HTTP |
|----------|------|
| Нет `remont_id` | 400 |
| `remont_id` не integer / ≤ 0 | 400 |
| Ремонт не найден | 200, `data: []`, `client_request_id: null` |
| Нет JWT | 401 |

## 5. Не входит в этот спринт

- MinIO/S3 хранилище реальных файлов
- Параметрические `.rfa` семейства (см. `PLAN.md` Фаза 4)
- Проверка хэша на стороне плагина / кэш-логика C# (клиентский код, не эта фича)
- Изменение существующего `client_material/tk/read/` (используется UI ТК, не трогаем)
