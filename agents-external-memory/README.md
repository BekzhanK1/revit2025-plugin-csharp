# agents-external-memory

Память для AI-агентов и разработчиков: **как устроен плагин Smart Remont Export Rooms сейчас** (не план будущего — актуальное состояние кода).

## Отличие от `external-agent-memory/`

| Папка | Назначение |
|-------|------------|
| `external-agent-memory/` | Краткие заметки сессий, история изменений |
| **`agents-external-memory/`** | Системное описание: экраны, потоки, маппинги, API, файлы |

При значимых изменениях в коде обновляйте соответствующий документ здесь.

## С чего начать

1. [smart-remont-revit-plugin/OVERVIEW.md](smart-remont-revit-plugin/OVERVIEW.md) — что за проект, сборка, глобальное состояние
2. [smart-remont-revit-plugin/USER_FLOW_AND_SCREENS.md](smart-remont-revit-plugin/USER_FLOW_AND_SCREENS.md) — цепочка окон и каждый экран
3. [smart-remont-revit-plugin/DATA_SOURCES.md](smart-remont-revit-plugin/DATA_SOURCES.md) — откуда берутся площади, замеры, экспорт JSON
4. [smart-remont-revit-plugin/ROOM_MEASUREMENTS_MAPPING.md](smart-remont-revit-plugin/ROOM_MEASUREMENTS_MAPPING.md) — замеры из ведомостей (`MEASURES`)
5. [smart-remont-revit-plugin/EXPORT_SCHEDULES_MAPPING.md](smart-remont-revit-plugin/EXPORT_SCHEDULES_MAPPING.md) — WorkItems и `*.mapping.json`
6. [smart-remont-revit-plugin/API_INTEGRATION.md](smart-remont-revit-plugin/API_INTEGRATION.md) — backend endpoints
7. [smart-remont-revit-plugin/FILE_MAP.md](smart-remont-revit-plugin/FILE_MAP.md) — карта файлов
8. [smart-remont-revit-plugin/ROADMAP.md](smart-remont-revit-plugin/ROADMAP.md) — обсуждённые направления (код, сверка с моделью, `SPECIFICATION_CODE`)

## Эпики в работе

| Эпик | Документ |
|------|----------|
| Primary key = `client_request_id` | [client-request-primary/](client-request-primary/) · [EPIC_TASKS](client-request-primary/task/EPIC_TASKS.md) |
| Project init | [project-init/](project-init/) |
| Revit materials sync | [revit-materials-sync/](revit-materials-sync/) |

## Корневые документы репозитория

- [../AGENTS.md](../AGENTS.md) — правила для агентов (сборка, деплой)
- [../README.md](../README.md) — для людей
