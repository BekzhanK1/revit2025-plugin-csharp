# Checklist — task-06-plugin-download-cache

- [x] `SBS/Services/RevitMaterialsDownloadService.cs` — `SyncAsync`, `DownloadResult`
- [x] Кэш-папка `%LOCALAPPDATA%\SmartRemont\revit-materials-cache\` + `cache_manifest.json`
- [x] Ключ кэша — `material_id` (не URL/hash, см. decisions №11)
- [x] Атомарная запись файлов (временный файл + `Move`)
- [x] Кнопка «Синхронизировать» + столбец статуса в `RevitMaterialsWindow`
- [x] Ошибка одного файла не прерывает остальные
- [x] `SBS/SBS.csproj` — новый файл зарегистрирован
- [x] `dotnet build SBS.sln -c Release` — успешно
- [ ] Ручной тест на `remont_id=21838`
- [x] WORK_LOG обновлён
