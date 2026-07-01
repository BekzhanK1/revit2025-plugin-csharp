# Checklist — task-07-plugin-load-family

- [x] `RevitMaterialsWindow` принимает `Document` (от `RemontHubWindow`)
- [x] `SBS/Services/RevitFamilyImportService.cs` — `LoadFamiliesIntoDocument`, одна транзакция на батч
- [x] Ошибка `LoadFamily` на одном файле не прерывает остальные
- [x] `surface` — явный статус "не поддерживается", без падений
- [x] Кнопка «Синхронизировать» — скачивание + импорт одним кликом
- [x] Итоговая статистика в UI
- [x] `SBS/SBS.csproj` — новый файл зарегистрирован
- [x] `dotnet build SBS.sln -c Release` — успешно
- [ ] Ручной тест в открытом Revit на `remont_id=21838`
- [x] WORK_LOG обновлён
