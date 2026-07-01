# work/ — SQL task-02

| Файл | Назначение |
|------|------------|
| `02_read_revit_material_by_remont.sql` | `CREATE OR REPLACE FUNCTION public.read_revit_material_by_remont` |

Эталон для деплоя — идентичная копия в `sql/revit-materials-sync/02_read_revit_material_by_remont.sql`.

**Зависимости:** task-01 (`revit_*` колонки в `material_tab`), `utils.get_client_request_id_by_remont`.

**Порядок:** после `01_material_tab_revit_fields.sql`.

Документация функции: `agent-memory/revit-materials-sync/functions/read_revit_material_by_remont.md`.
