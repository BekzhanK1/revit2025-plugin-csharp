# Checklist — task-07-plugin-primary-cr

**Статус:** done (build OK) · **Depends:** task-02, task-03 (min)

- [x] Metadata / init primary = `client_request_id` (`ProjectRemontMetadataService.IsInitialized/CanUseHubWorkFeatures/ValidateMatches`, `ProjectInitService`)
- [x] File naming `{client_request_id}_…` (`ProjectFileNamingService`); legacy `{remont_id}_*` файлы всё ещё распознаются как инициализированные (`IsSavedInitializedProjectFile`)
- [x] Home search by CR (`HomeWindow` ищет по `client_request_id` по умолчанию, UI-тексты обновлены)
- [x] Materials / TK / DS URLs — единый неймспейс `/revit/plugin/*` по CR (`Configs.RevitMaterialReadUrl/TkReadUrl/DsRoomChangeReadUrl/MeasuresReadUrl`, PLUGIN_API.md)
- [x] Замеры и ДС «изменение площади» — прямая запись (`MeasuresService.ApplyAsync`, `DsRoomChangeService.ApplyAsync`), event-буфер (`revit_events`) удалён из плагина
- [x] Кнопки Замеры/ДС/Сравнение/ТипПараметров разблокированы в хабе (доступны при `client_request_id > 0`; ДС apply требует `remont_id`, гейтится внутри окна)
- [x] Init works without remont (материалы/копия/метаданные не требуют `remont_id`; `RemontId` = 0, если ремонта нет)
- [x] Hub: primary UI = заявка; ремонт опционален
- [x] `dotnet build SBS.sln -c Release` — 0 ошибок
- [x] Docs note (этот checklist)

## Фокус сейчас

Поток проектировщика: Home поиск по `client_request_id` → хаб → Init (даже без remont) → материалы/ТК/замеры → ДС (если есть ремонт).

## Как матчатся комнаты для apply

`MeasuresService.ReadAsync` возвращает `room_id` по системным именам комнат заявки. Плагин сопоставляет их с именами комнат Revit по базовому имени (`RoomNameMatcher.GetBaseName`) и передаёт `room_id` в `measures/apply` и `ds/room-change/apply`. Несопоставленные помещения попадают в `skipped[]` / отдельный список в UI, а не блокируют всю отправку (кроме ДС, где применяются только помещения с найденным `room_id`).

## Later

- Миграция старых `{remont_id}_*.rvt` не нужна (чтение совместимо)
