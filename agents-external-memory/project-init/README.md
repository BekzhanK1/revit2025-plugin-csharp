# Project Init — полная инициализация RVT под ремонт

**Цель:** при выборе ремонта создать копию проекта, проштамповать `remont_id`, синхронизировать материалы, сохранить файл `{remont_id}_{ЖК}.rvt`. При повторном открытии — remont подставляется из модели, без ручного ввода.

**Статус:** ✅ код готов · ⏳ ручной smoke в Revit 2025

## Документы

| Файл | Содержание |
|------|------------|
| [SPEC.md](SPEC.md) | Требования, flow, ограничения Revit API |
| [PLAN.md](PLAN.md) | Фазы, порядок, риски |
| [EPIC_TASKS.md](EPIC_TASKS.md) | task-01…08 |
| [decisions/DECISIONS.md](decisions/DECISIONS.md) | Архитектурные решения |

## DoD эпика

- [x] Extensible Storage: `remont_id`, `client_request_id`, `initialized_at` на `ProjectInformation`
- [x] Чтение/валидация метаданных при старте плагина и в hub
- [x] SaveAs копии с именем `{remont_id}_{SanitizedResidentName}.rvt`
- [x] Кнопка «Инициализировать проект» → stamp + full sync материалов
- [x] Если файл уже инициализирован — remont из Storage, поиск на Home опционален/пропускается
- [x] Обработка edge cases: unsaved doc, worksharing, файл уже существует
- [x] `dotnet build SBS.sln -c Release`
- [ ] `dotnet build SBS.sln -c Release` + ручной smoke в Revit 2025

## Связь с другими эпиками

- Материалы: `agents-external-memory/revit-materials-sync/` (sync уже есть)
- UI hub: `agents-external-memory/plugin-ui-redesign/`
