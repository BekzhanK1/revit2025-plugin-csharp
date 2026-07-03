# PLAN — Project Init

## Фазы

```
Phase A — Metadata (Storage read/write)     task-01, task-02
Phase B — SaveAs + naming                   task-03, task-04
Phase C — Init orchestration + UI           task-05, task-06
Phase D — Auto-bind on open               task-07
Phase E — QA + docs                       task-08
```

## Phase A (1d)

1. Schema GUID + `ProjectRemontMetadata` DTO
2. `ProjectRemontMetadataService`: Read, Write, Validate, IsInitialized
3. Unit-less smoke: write/read в тестовом doc через Revit (manual)

## Phase B (1d)

1. `ProjectFileNamingService`: BuildFileName, Sanitize, default folder
2. `ProjectCopyService`: SaveAs с опциями, проверка существующего файла
3. Диалоги: overwrite / open existing

## Phase C (1.5d)

1. `ProjectInitService`: orchestration (copy → stamp → sync → save)
2. Hub кнопка + progress UI
3. Интеграция с `RevitMaterialsWindow` sync logic (вынести общий `RevitMaterialsSyncOrchestrator`?)

## Phase D (0.5d)

1. `ExportSmartRemontRoomsCommand` / `HomeWindow`: read Storage on load
2. Skip search если remont уже в doc

## Phase E (0.5d)

1. Manual QA checklist
2. USER_FLOW update

## Риски

| Риск | Митигация |
|------|-----------|
| SaveAs не переключает active document | Документировать «откройте новый файл»; v2 `OpenAndActivateDocument` |
| Worksharing | v1: только non-central / file-based; предупреждение |
| Storage schema migration | Версия в schema name или `plugin_version` field |
| Длинные имена ЖК | Truncate + hash suffix |

## Оценка

**~4–5 рабочих дней** (1 dev)
