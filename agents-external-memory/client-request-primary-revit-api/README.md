# Epic: client-request-primary

Переход привязки RVT и Revit API с `remont_id` на `client_request_id` (`client_request_tab`).

| Документ | Содержание |
|----------|------------|
| [task/EPIC_TASKS.md](task/EPIC_TASKS.md) | Список тасков, зависимости, DoD |
| [TASK.md](TASK.md) | Полный контракт API + плагин follow-up |
| [decisions/DECISIONS.md](decisions/DECISIONS.md) | Принятые решения (#1–#6) |

## Таски

| # | Папка | Prio | Кто |
|---|-------|------|-----|
| 01 | [task-01-sql-by-client-request](task/task-01-sql-by-client-request/) | P0 | Backend SQL |
| 02 | [task-02-material-read-by-cr](task/task-02-material-read-by-cr/) | P0 | Backend |
| 03 | [task-03-quick-search-cr](task/task-03-quick-search-cr/) | P0 | Backend |
| 04 | [task-04-tk-read-by-cr](task/task-04-tk-read-by-cr/) | P1 | Backend |
| 05 | [task-05-ds-room-change-by-cr](task/task-05-ds-room-change-by-cr/) | P1 | Backend |
| 06 | [task-06-revit-events-cr](task/task-06-revit-events-cr/) | P2 | Backend |
| 07 | [task-07-plugin-primary-cr](task/task-07-plugin-primary-cr/) | after API | Plugin |

**Рекомендации зафиксированы:** query-param only (#3), events 409 без remont (#4), колонка event_log без backfill (#5), текущие JWT rights (#6).
