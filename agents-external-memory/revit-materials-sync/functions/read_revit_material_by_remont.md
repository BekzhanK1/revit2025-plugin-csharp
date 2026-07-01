# `public.read_revit_material_by_remont`

**Файлы:** `sql/revit-materials-sync/02_read_revit_material_by_remont.sql` · work-копия в `task/task-02-sql-function/work/`

## Назначение

Материалы ремонта для Revit-плагина: по `remont_id` → `client_request_id` → строки ТК (`client_material_tab`) с полями `material_tab.revit_*`.

Отличия от `public.read_client_material_by_remont`:

- **Дедуп** по `material_id` (`DISTINCT ON`) — один материал в нескольких комнатах → одна строка (решение №6).
- **Фильтр** `revit_file_type <> 'none'` — только материалы, нужные в Revit.
- **Узкий контракт** — без grant-блоков и HTML-полей ТК; только Revit-релевантные поля + `material_type_code` (решение №5, №7).

## Сигнатура

```sql
public.read_revit_material_by_remont(cur refcursor, remont_id_ integer)
RETURNS refcursor
```

## Логика

1. `remont_id_ IS NULL` → `RAISE EXCEPTION '{Не указан ремонт}'`
2. `client_request_id_ := utils.get_client_request_id_by_remont(remont_id_)`
3. `client_request_id_ IS NULL` → одна строка: `remont_id`, `client_request_id = NULL`, `data = '[]'`
4. Иначе — выборка `DISTINCT ON (m.material_id)` из `client_material_tab` + `material_tab` + `material_type_tab`, агрегация в `jsonb` массив `data`

## Поля элемента `data[]`

| Поле | Тип | Описание |
|------|-----|----------|
| `material_id` | integer | PK материала |
| `material_name` | string | Название |
| `material_type_id` | integer | FK типа |
| `material_type_code` | string | Код типа (`LAMINAT`, `TILE`, …) |
| `revit_file_type` | string | `'rfa'` \| `'surface'` |
| `revit_file_url` | string \| null | URL файла (пока placeholder) |
| `revit_file_hash` | string \| null | Отпечаток для кэша плагина |
| `revit_asset_name` | string \| null | Имя семейства / материала в Revit |

## Пример вызова (MCP dev, read-only после deploy)

```sql
BEGIN;
SELECT * FROM public.read_revit_material_by_remont('cur', :remont_id);
FETCH ALL FROM cur;
COMMIT;
```

Подставить реальный `remont_id` с материалами в ТК. До task-04 все `revit_file_type = 'none'` → `data: []`.

**Проверка дедупа:** remont, где один `material_id` в 2+ комнатах ТК и `revit_file_type <> 'none'` — в `data` одна строка на материал.

## Пример ответа (SPEC §4)

```json
{
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

Ремонт не найден:

```json
{
  "remont_id": 999999999,
  "client_request_id": null,
  "data": []
}
```

## HTTP API

После task-03: `GET /revit/material/read/?remont_id={remont_id}` — см. `SPEC.md` §4, `sql/revit-materials-sync/README.md`.
