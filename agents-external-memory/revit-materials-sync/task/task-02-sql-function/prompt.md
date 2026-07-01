# Task 02 — SQL: `public.read_revit_material_by_remont`

Скопируй блок «Промпт для агента» агенту. **Только SQL**, без Python/Vue.

---

## Промпт для агента

```
Реализуй Task 02 фичи revit-materials-sync: SQL-функция read_revit_material_by_remont.

Прочитай ВСЕ файлы из секции «Контекст» (в порядке таблицы).

Требования:
1. SQL писать в **два места** (одинаковое содержимое):
   - `agent-memory/revit-materials-sync/task/task-02-sql-function/work/02_read_revit_material_by_remont.sql`
   - `sql/revit-materials-sync/02_read_revit_material_by_remont.sql`
   CREATE OR REPLACE FUNCTION public.read_revit_material_by_remont(
     cur refcursor,
     remont_id_ integer
   ) RETURNS refcursor

   Логика (по образцу public.read_client_material_by_remont):
   - client_request_id_ := utils.get_client_request_id_by_remont(remont_id_)
   - если NULL → OPEN cur FOR SELECT remont_id_, NULL::integer AS client_request_id, '[]'::jsonb AS data; RETURN cur
   - иначе: SELECT DISTINCT ON (m.material_id) m.material_id, m.material_name, m.material_type_id,
     mt.material_type_code, m.revit_file_type, m.revit_file_url, m.revit_file_hash, m.revit_asset_name
     FROM client_material_tab cm
     JOIN material_tab m ON m.material_id = cm.material_id
     JOIN material_type_tab mt ON mt.material_type_id = m.material_type_id
     WHERE cm.client_request_id = client_request_id_ AND m.revit_file_type <> 'none'
     ORDER BY m.material_id
   - агрегировать в jsonb массив (тот же паттерн items_ := items_ || jsonb_build_array(...), либо
     json_agg — выбрать то, что проще читается и совпадает по духу с соседними функциями)
   - OPEN cur FOR SELECT remont_id_ AS remont_id, client_request_id_ AS client_request_id, items_ AS data
2. sql/revit-materials-sync/README.md — дополнить описанием новой функции + порядком применения
   (после 01_material_tab_revit_fields.sql)
3. sql/README.md — обновить блок фичи
4. НЕ применять SQL к живой БД без явной просьбы пользователя
5. agent-memory/revit-materials-sync/functions/read_revit_material_by_remont.md — назначение,
   сигнатура, пример вызова через MCP, пример ответа

Обнови task-02-sql-function/CHECKLIST.md и work_log/WORK_LOG.md.
```

---

## Контекст

### Спецификация

| Файл | Зачем |
|------|-------|
| `agent-memory/revit-materials-sync/SPEC.md` §3 | Псевдокод тела функции |
| `agent-memory/revit-materials-sync/decisions/DECISIONS.md` | №5, №6, №7 |

### Эталон

| Файл | Зачем |
|------|-------|
| `sql/client-material/read_client_material_by_remont.sql` | Паттерн resolve remont → client_request_id, refcursor, jsonb-агрегация |
| `sql/client-material/README.md` | Формат README и HTTP-контракта для аналогичной фичи |

### Правила

| Файл | Зачем |
|------|-------|
| `.cursor/rules/sql-artifacts.mdc` | Размещение SQL, README |
| `.cursor/rules/no-autonomous-database-writes.mdc` | Не выполнять CREATE FUNCTION на dev/prod без явной просьбы (создание функции безопасно, но следуем правилу буквально — только по запросу) |

### Вне scope

- Backend endpoint → task-03
- Изменение `read_client_material_by_remont` (не трогаем — используется UI)

---

## Артефакты

| Создать | Путь |
|---------|------|
| SQL (work) | `agent-memory/revit-materials-sync/task/task-02-sql-function/work/02_read_revit_material_by_remont.sql` |
| SQL (deploy) | `sql/revit-materials-sync/02_read_revit_material_by_remont.sql` |
| Документация функции | `agent-memory/revit-materials-sync/functions/read_revit_material_by_remont.md` |

## DoD

- [ ] Функция создаёт валидный SQL (проверить синтаксис локально/через MCP на dev при разрешении)
- [ ] Дедуп по `material_id` подтверждён тест-кейсом (материал в 2+ комнатах ТК → 1 строка в ответе)
- [ ] Ремонт без Revit-материалов (все `'none'`) → `data: []`
- [ ] Несуществующий `remont_id` → `client_request_id: null`
- [ ] README обновлены (папка + `sql/README.md`)
- [ ] `functions/read_revit_material_by_remont.md`
- [ ] WORK_LOG обновлён
