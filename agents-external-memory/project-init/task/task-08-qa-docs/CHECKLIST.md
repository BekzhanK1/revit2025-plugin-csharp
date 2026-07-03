# Checklist — task-08-qa-docs

## Code review vs SPEC (2026-07-02)

- [x] Extensible Storage schema + metadata service
- [x] SaveAs + `{remont_id}_{resident}.rvt` naming
- [x] Init orchestrator (stamp + full sync + Save)
- [x] Hub UI + conflict blocking
- [x] Auto-bind on command start + Home continue
- [x] `dotnet build SBS.sln -c Release` — OK (0 errors, 0 warnings)
- [ ] DeployToRevit — **не запускался** (Revit может быть открыт; деплой вручную при закрытом Revit)

### Known gaps (v1, не блокируют код)

- Существующий файл с **тем же** remont_id: перезапись с confirm, без отдельного «Открыть существующий»
- Fallback имени файла: `{id}_Remont`, не `{id}_Remont_{id}` как в SPEC
- Сверка Storage ↔ SelectedRemont при отправке revit_events — не реализована (только блок init)
- Shared parameter `SR_REMONT_ID` — phase 2 (out of scope)

## Manual QA

- [ ] Шаблон RVT → выбор remont 21642 → Init → файл `21642_….rvt` создан
- [ ] Storage: remont_id=21642 читается `TryRead`
- [ ] Материалы 1395, 4742, 1981, 9771 — presence «В проекте»
- [ ] Повторный Init на том же файле — корректное поведение (skip/warn)
- [ ] Init с другим remont на том же файле — блок
- [ ] Открыть init-файл → remont auto-bind, hub OK
- [ ] Обычный RVT без Storage — старый flow OK

## Docs

- [x] `USER_FLOW_AND_SCREENS.md` — секция Project Init
- [x] `project-init/work_log/WORK_LOG.md`
- [x] Epic README DoD checkboxes

## Build

- [x] `dotnet build SBS.sln -c Release`
- [ ] `dotnet build SBS.sln -c Release -p:DeployToRevit=true` — отложено (Revit должен быть закрыт)
