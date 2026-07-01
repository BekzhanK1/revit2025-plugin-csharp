# revit-materials-sync

Синхронизация материалов ремонта (по `client_request_id` / `remont_id`) в проект Revit проектировщика.

## Точки входа

| Что | Документ |
|-----|----------|
| **Долгий план (весь скоуп, .rfa/.adsklib, S3, плагин)** | [`PLAN.md`](PLAN.md) — референс, горизонт 4 фазы |
| **Ближний скоуп (этот спринт)** | [`SPEC.md`](SPEC.md) |
| **Задачи по фазам** | [`task/EPIC_TASKS.md`](task/EPIC_TASKS.md) |
| **Решения** | [`decisions/DECISIONS.md`](decisions/DECISIONS.md) |
| **Дневник** | [`work_log/WORK_LOG.md`](work_log/WORK_LOG.md) |

## Структура

```
revit-materials-sync/
├── PLAN.md             # полный план (референс, 4 фазы, S3/ACC, .rfa семейства)
├── SPEC.md             # ближний скоуп: поля material_tab + 2 SQL-функции + endpoint
├── task/
│   ├── EPIC_TASKS.md
│   ├── task-01-db-fields/
│   │   ├── prompt.md, CHECKLIST.md
│   │   └── work/           # SQL этой таски (копия → sql/revit-materials-sync/)
│   ├── task-02-sql-function/work/
│   └── ...
├── decisions/DECISIONS.md
├── functions/          # .md на каждую новую SP (после реализации)
└── work_log/WORK_LOG.md
```

## Уже готово (эталон, не переизобретать)

- `sql/client-material/read_client_material_by_remont.sql` — `public.read_client_material_by_remont(cur, remont_id_)`, изначально написана «для Revit-плагина».
- `GET /common/client_material/tk/read/?remont_id=` — `client_request/ex_urls/client_material_urls.py` → `common/...` (уже отдаёт ТК-строки по `remont_id`, без обёртки grant-блоков).
- Паттерн ViewSet + `call_an_sp`: `common/ex_views/revit_event_views.py`, `common/ex_services/revit_event_services.py`.

Ближний скоуп **не меняет** существующий ТК-эндпоинт (он используется UI), а добавляет **новый**, узкий под нужды плагина: дедуп по `material_id`, только материалы с `revit_file_type <> 'none'`, + сами Revit-поля.

## Статус

- Презентация флоу — обсуждена, термины уточнены (ТК = `client_material_tab`, не "по договору")
- БД-исследование (`material_tab`, `client_material_tab`, `client_request_tab`) — done
- SPEC ближнего скоупа — done (этот файл + `SPEC.md`)
- Реализация (task-01…04) — **pending**
