# Схема — Revit-поля `material_tab`

> Task-01 выполнен 2026-07-01. DDL: `sql/revit-materials-sync/01_material_tab_revit_fields.sql`

## Новые колонки

| Поле | Тип | Nullable | Default | Описание |
|------|-----|----------|---------|----------|
| `revit_file_type` | `varchar(20)` | `NOT NULL` | `'none'` | `'rfa'` — 3D-объект, `'surface'` — поверхность (общий `.rvt`/библиотека материалов), `'none'` — не нужен в Revit |
| `revit_file_url` | `varchar(500)` | `NULL` | — | Ссылка на файл. Для `'rfa'` — на конкретный `.rfa`/семейство. Для `'surface'` — на общий реестр/библиотеку (пока placeholder). Для `'none'` — всегда NULL |
| `revit_file_hash` | `varchar(64)` | `NULL` | — | Отпечаток файла (кэш). Пока `md5(revit_file_url)` — фиктивный, но стабильный |
| `revit_asset_name` | `varchar(200)` | `NULL` | — | Имя семейства в `.rfa` или имя материала в общем `.rvt`/библиотеке |

## CHECK constraints

```sql
-- material_tab_revit_file_type_check
CHECK (revit_file_type IN ('rfa', 'surface', 'none'))

-- material_tab_revit_none_fields_null_check
CHECK (
  revit_file_type <> 'none'
  OR (
    revit_file_url IS NULL
    AND revit_file_hash IS NULL
    AND revit_asset_name IS NULL
  )
)
```

**Не добавлено (решение №3 в DECISIONS.md):** `revit_category` — категория уже зашита в `.rfa`.

## Связанные таблицы

| Таблица | Роль |
|---------|------|
| `client_request_tab` | Точка входа: `client_request_id` / `remont_id` |
| `client_material_tab` (ТК) | Материалы конкретного ремонта, FK `material_id` → `material_tab` |
| `material_type_tab` | `material_type_code` — источник классификации `surface`/`rfa`/`none` (task-04) |

## Проверка на dev (до применения DDL)

MCP `user-smart-remont-dev` (2026-07-01): колонок `revit_*` в `material_tab` **нет** — 51 существующая колонка, последняя `material_name_for_ddu_uzb`.
