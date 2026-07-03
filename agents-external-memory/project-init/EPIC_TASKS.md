# Epic: Project Init — инициализация RVT под ремонт

**DoD:** см. [README.md](README.md)

```
task-01 [Extensible Storage schema] ──→ task-02 [Read/Write service]
task-03 [File naming] ──→ task-04 [SaveAs copy]
task-02 + task-04 ──→ task-05 [Init orchestrator]
task-05 ──→ task-06 [Hub UI + progress]
task-02 ──→ task-07 [Auto-bind on document open]
task-08 [Manual QA + docs]
```

---

## task-01 — Extensible Storage schema

**Файлы:** `SBS/ProjectRemont/ProjectRemontSchema.cs` (новая папка)

**DoD:**
- [ ] Фиксированный `SchemaGuid` + `SchemaBuilder` (remont_id, client_request_id, initialized_at, plugin_version)
- [ ] Документирован GUID в `decisions/DECISIONS.md`
- [ ] Schema регистрируется один раз (lazy static)

**Checklist:** [task/task-01-storage-schema/CHECKLIST.md](task/task-01-storage-schema/CHECKLIST.md)

---

## task-02 — ProjectRemontMetadataService

**Файлы:** `SBS/Services/ProjectRemontMetadataService.cs`

**DoD:**
- [ ] `TryRead(Document)` → `ProjectRemontMetadata?`
- [ ] `Write(Document, metadata)` в Transaction
- [ ] `IsInitialized(doc)`, `ValidateMatches(doc, remontId)`
- [ ] Логирование через Serilog

**Checklist:** [task/task-02-metadata-service/CHECKLIST.md](task/task-02-metadata-service/CHECKLIST.md)

---

## task-03 — Именование файла проекта

**Файлы:** `SBS/Services/ProjectFileNamingService.cs`

**DoD:**
- [ ] `BuildProjectFilePath(remontId, residentName, baseFolder?)`
- [ ] Sanitize кириллицы и invalid chars
- [ ] Default folder: `Documents\SmartRemont\Projects\`
- [ ] Max length имени файла

**Checklist:** [task/task-03-file-naming/CHECKLIST.md](task/task-03-file-naming/CHECKLIST.md)

---

## task-04 — SaveAs копия проекта

**Файлы:** `SBS/Services/ProjectCopyService.cs`

**DoD:**
- [ ] `SaveCopyAs(Document doc, string targetPath, overwrite: bool)`
- [ ] Проверка: doc.IsModified, read-only, worksharing warning (v1: message only)
- [ ] Если файл exists → throw typed exception или result enum для UI
- [ ] `SBS.csproj` — новые файлы

**Checklist:** [task/task-04-saveas-copy/CHECKLIST.md](task/task-04-saveas-copy/CHECKLIST.md)

---

## task-05 — ProjectInitService (orchestrator)

**Файлы:** `SBS/Services/ProjectInitService.cs`

**DoD:**
- [ ] `InitializeProjectAsync(Document doc, RemontOption remont, UIProgress?)` 
- [ ] Шаги: validate → SaveAs → Write Storage → materials sync → Save
- [ ] Переиспользует download/import из materials sync (refactor shared helper если нужно)
- [ ] Result DTO: success, newFilePath, errors[], materialsLoaded count
- [ ] Transaction boundaries согласованы с Revit API

**Checklist:** [task/task-05-init-orchestrator/CHECKLIST.md](task/task-05-init-orchestrator/CHECKLIST.md)

---

## task-06 — UI: Hub «Инициализировать проект»

**Файлы:** `RemontHubWindow.xaml`, `.xaml.cs`, опционально `ProjectInitProgressDialog.xaml`

**DoD:**
- [ ] Новая карточка **«Инициализировать проект»** (первая в списке или после sync materials)
- [ ] Состояния: not init / init OK / remont mismatch
- [ ] Progress: SaveAs → stamp → sync → save
- [ ] Success dialog с путём к файлу
- [ ] Ошибки — `AppMessageDialog`

**Checklist:** [task/task-06-hub-init-ui/CHECKLIST.md](task/task-06-hub-init-ui/CHECKLIST.md)

---

## task-07 — Auto-bind remont при открытии документа

**Файлы:** `ExportSmartRemontRoomsCommand.cs`, `HomeWindow.xaml.cs`, `ExportRoomsApplication.cs`

**DoD:**
- [ ] При старте команды: `TryRead(activeDoc)` → заполнить `SelectedRemont` (из Storage + optional API refresh name)
- [ ] Home: если remont в doc — показать banner, клик → hub без поиска
- [ ] Hub: если remont в doc ≠ SelectedRemont — warning
- [ ] Не ломать flow без Storage (как сейчас)

**Checklist:** [task/task-07-auto-bind/CHECKLIST.md](task/task-07-auto-bind/CHECKLIST.md)

---

## task-08 — Manual QA + documentation

**DoD:**
- [ ] Чеклист на remont 21642
- [ ] USER_FLOW_AND_SCREENS.md — init flow
- [ ] WORK_LOG

**Checklist:** [task/task-08-qa-docs/CHECKLIST.md](task/task-08-qa-docs/CHECKLIST.md)

---

## Опционально (backlog)

| ID | Задача |
|----|--------|
| B-01 | Shared parameter SR_REMONT_ID в Project Information |
| B-02 | `OpenAndActivateDocument` после SaveAs |
| B-03 | Backend event «revit_project_initialized» |
| B-04 | Worksharing-aware init |
