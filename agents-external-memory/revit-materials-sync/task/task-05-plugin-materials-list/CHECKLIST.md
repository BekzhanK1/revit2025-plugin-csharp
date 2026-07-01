# Checklist — task-05-plugin-materials-list

- [x] `SBS/DTO/RevitMaterialDtos.cs` — `RevitMaterialReadResponse` + `RevitMaterialRowDto`
- [x] `SBS/Configs.cs` — `RevitMaterialReadUrl(remontId)`
- [x] `SBS/Services/RevitMaterialsService.cs` — `ReadAsync(remontId)`, обработка 401/400/500, пустой `data` — не ошибка
- [x] `SBS/Views/RevitMaterialsWindow.xaml` + `.xaml.cs` — DataGrid, состояния загрузка/пусто/ошибка
- [x] `SBS/Views/RemontHubWindow.xaml` + `.xaml.cs` — кнопка «Материалы (Revit)»
- [x] `SBS/SBS.csproj` — новые файлы зарегистрированы (`Compile`, `Page`)
- [x] `dotnet build SBS.sln -c Release` — успешно
- [ ] Ручной тест на `remont_id=21838` (2 материала, кириллица не битая)
- [ ] Деплой в Revit Addins — НЕ выполнен без явного запроса пользователя
- [x] WORK_LOG обновлён
