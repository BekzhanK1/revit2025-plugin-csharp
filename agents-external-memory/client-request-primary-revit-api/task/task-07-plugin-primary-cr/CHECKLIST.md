# Checklist — task-07-plugin-primary-cr

**Статус:** done (build OK) · **Depends:** task-02, task-03 (min)

- [x] Metadata / init primary = `client_request_id` (`ProjectRemontMetadataService.IsInitialized/CanUseHubWorkFeatures/ValidateMatches`, `ProjectInitService`)
- [x] File naming `{client_request_id}_…` (`ProjectFileNamingService`); legacy `{remont_id}_*` файлы всё ещё распознаются как инициализированные (`IsSavedInitializedProjectFile`)
- [x] Home search by CR (`HomeWindow` ищет по `client_request_id` по умолчанию, UI-тексты обновлены)
- [x] Materials / TK / DS URLs use CR (`Configs.RevitMaterialReadUrl/ClientMaterialTkReadUrl/DsRoomChangeReadUrl`, соответствующие сервисы и `RevitMaterialsSyncOrchestrator`)
- [x] Events: **временно скрыты** в хабе (Замеры/ДС) — позже прямая запись без буфера
- [x] Init works without remont (материалы/копия/метаданные не требуют `remont_id`; `RemontId` = 0, если ремонта нет)
- [x] Hub: primary UI = заявка; ремонт опционален
- [x] `dotnet build SBS.sln -c Release` — 0 ошибок
- [x] Docs note (этот checklist)

## Фокус сейчас

Поток проектировщика: Home поиск по `client_request_id` → хаб → Init (даже без remont) → материалы/ТК.

## Later

- Замеры/ДС без event-буфера
- Миграция старых `{remont_id}_*.rvt` не нужна (чтение совместимо)
