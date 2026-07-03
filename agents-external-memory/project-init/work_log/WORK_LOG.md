# WORK_LOG — Project Init

| Дата | Task | Заметки |
|------|------|---------|
| 2026-07-02 | Epic created | SPEC, PLAN, EPIC_TASKS, 8 tasks — по запросу продукта |
| 2026-07-02 | task-01-storage-schema | `ProjectRemontSchema.cs` — GUID `171500a5-1d6b-4f5d-8253-e53b5a8275c3`, 4 поля, lazy `GetOrCreateSchema()`, Release build OK |
| 2026-07-02 | task-02-metadata-service | `ProjectRemontMetadataService` + DTO — TryRead/Write/IsInitialized/ValidateMatches на ProjectInformation, Serilog Info, Release build OK |
| 2026-07-02 | task-03-file-naming | `ProjectFileNamingService` — BuildFileName/BuildFullPath, SanitizeResidentName, EnsureDirectoryExists, default `Documents\SmartRemont\Projects`, max 80 chars base name |
| 2026-07-02 | task-04-saveas-copy | `ProjectCopyService.SaveCopyAs` — SaveAsOptions.OverwriteExistingFile, FileAlreadyExists/IsWorksharedWarning flags, EnsureDirectoryExists, read-only guard, Release build OK |
| 2026-07-02 | task-05-init-orchestrator | `RevitMaterialsSyncOrchestrator.SyncAllAsync` — download RFA + surfaces, import families/materials; `ProjectInitService.InitializeProjectAsync` — validate/conflict, SaveCopyAs, metadata stamp, sync, Save; `RevitMaterialsWindow` refactored to orchestrator, Release build OK |
| 2026-07-02 | task-06-hub-init-ui | Hub: кнопка «Инициализировать проект» (первая в меню), confirm с preview path, progress в StatusTextBlock, badge «Инициализирован #id», блок при remont mismatch через AppMessageDialog, Release build OK |
| 2026-07-02 | task-07-auto-bind | `ProjectRemontBindingService.TryBindFromDocument` — чтение Storage → SelectedRemont; Home banner «Продолжить»; Hub badge «Проект инициализирован»; optional quick_search enrich, Release build OK |
| 2026-07-02 | task-08-qa-docs | Code review vs SPEC — compile OK, gaps documented (open-existing UX, API cross-check); `USER_FLOW_AND_SCREENS.md` + README DoD + WORK_LOG; DeployToRevit skipped |

## Epic summary (task-08)

**Реализовано (tasks 01–07):**

| Компонент | Файлы |
|-----------|-------|
| Schema | `SBS/ProjectRemont/ProjectRemontSchema.cs` |
| Metadata R/W | `SBS/Services/ProjectRemontMetadataService.cs`, `SBS/DTO/ProjectRemontMetadata.cs` |
| File naming | `SBS/Services/ProjectFileNamingService.cs` → `Documents\SmartRemont\Projects\{id}_{ЖК}.rvt` |
| SaveAs | `SBS/Services/ProjectCopyService.cs` |
| Init flow | `SBS/Services/ProjectInitService.cs` |
| Materials sync | `SBS/Services/RevitMaterialsSyncOrchestrator.cs` (shared with `RevitMaterialsWindow`) |
| Auto-bind | `SBS/Services/ProjectRemontBindingService.cs` + `ExportSmartRemontRoomsCommand`, `HomeWindow` |
| Hub UI | `SBS/Views/RemontHubWindow.xaml(.cs)` — кнопка Init, badges, progress, conflict dialog |

**Acceptance (код):** Storage stamp, SaveAs naming, full sync, remont conflict block, auto-bind on reopen.

**Осталось:** ручной smoke в Revit 2025 (remont 21642, материалы 1395/4742/1981/9771, повторный init, mismatch block).
