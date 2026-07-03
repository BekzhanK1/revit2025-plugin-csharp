# Checklist — task-07-auto-bind

- [x] `ExportSmartRemontRoomsCommand` — после auth, `TryRead(doc)` → set SelectedRemont
- [x] Заполнить RemontOption: RemontId, ClientRequestId из Storage; Name/ResidentName — из quick_search или placeholder
- [x] `HomeWindow` — banner «Ремонт привязан к проекту #21642», кнопка «Продолжить в hub» без поиска
- [x] `RemontHubWindow.BindRemontInfo` — если Storage есть, показать badge «Проект инициализирован»
- [x] Flow без Storage — без изменений
- [x] `dotnet build SBS.sln -c Release`

## Manual

- [ ] Открыть `{21642_…}.rvt` → плагин знает remont без Home-поиска
