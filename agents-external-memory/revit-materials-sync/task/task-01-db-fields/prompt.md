# Task 01 — БД: поля Revit в `material_tab`

Скопируй блок «Промпт для агента» агенту. **Только SQL/DDL**, без Python/Vue.

---

## Промпт для агента

```
Реализуй Task 01 фичи revit-materials-sync: 4 новых поля в material_tab.

Прочитай ВСЕ файлы из секции «Контекст» (в порядке таблицы).

Требования:
1. SQL писать в **два места** (одинаковое содержимое):
   - `agent-memory/revit-materials-sync/task/task-01-db-fields/work/01_material_tab_revit_fields.sql` — рабочая копия таски
   - `sql/revit-materials-sync/01_material_tab_revit_fields.sql` — эталон для деплоя
   Содержимое:
   ALTER TABLE public.material_tab
     ADD COLUMN IF NOT EXISTS revit_file_type   varchar(20) NOT NULL DEFAULT 'none',
     ADD COLUMN IF NOT EXISTS revit_file_url    varchar(500),
     ADD COLUMN IF NOT EXISTS revit_file_hash   varchar(64),
     ADD COLUMN IF NOT EXISTS revit_asset_name  varchar(200);

   + CHECK constraint revit_file_type IN ('rfa','surface','none')
   + CHECK: если revit_file_type = 'none', то revit_file_url/hash/asset_name IS NULL (опционально,
     обсудить целесообразность — не блокировать таску, если усложняет миграцию существующих данных)
2. sql/revit-materials-sync/README.md — порядок применения, описание полей (таблица как в SPEC.md §2)
3. sql/README.md — добавить короткий блок с командой psql -f
4. НЕ применять SQL к живой БД без явной просьбы пользователя (no-autonomous-database-writes)
5. agent-memory/revit-materials-sync/schemas/material_tab_revit_fields.md — итоговая схема полей

Обнови task-01-db-fields/CHECKLIST.md и work_log/WORK_LOG.md.
```

---

## Контекст

### Спецификация

| Файл | Зачем |
|------|-------|
| `agent-memory/revit-materials-sync/SPEC.md` §2 | Точные поля, типы, CHECK |
| `agent-memory/revit-materials-sync/decisions/DECISIONS.md` | №3 (без `revit_category`), №8 (фиктивные данные — отдельная таска 04) |

### Эталон

| Файл | Зачем |
|------|-------|
| MCP `user-smart-remont-dev`: `information_schema.columns` для `material_tab` | Убедиться в актуальном списке колонок перед ALTER (не дублировать существующее поле) |

### Правила

| Файл | Зачем |
|------|-------|
| `.cursor/rules/sql-artifacts.mdc` | Размещение SQL, README |
| `.cursor/rules/no-autonomous-database-writes.mdc` | Не выполнять ALTER на dev/prod без явной просьбы |

### Вне scope

- Заполнение реальных значений полей → task-04
- SQL-функция чтения → task-02
- Backend/frontend

---

## Артефакты

| Создать | Путь |
|---------|------|
| DDL (work) | `agent-memory/revit-materials-sync/task/task-01-db-fields/work/01_material_tab_revit_fields.sql` |
| DDL (deploy) | `sql/revit-materials-sync/01_material_tab_revit_fields.sql` |
| README | `sql/revit-materials-sync/README.md` |
| Схема (память) | `agent-memory/revit-materials-sync/schemas/material_tab_revit_fields.md` |

## DoD

- [ ] DDL-файл с `ADD COLUMN IF NOT EXISTS` (идемпотентно)
- [ ] CHECK на допустимые значения `revit_file_type`
- [ ] README в папке SQL
- [ ] `sql/README.md` обновлён
- [ ] `schemas/material_tab_revit_fields.md`
- [ ] WORK_LOG обновлён
- [ ] Миграция **не выполнена** на dev/prod без явного запроса пользователя в этом же диалоге
