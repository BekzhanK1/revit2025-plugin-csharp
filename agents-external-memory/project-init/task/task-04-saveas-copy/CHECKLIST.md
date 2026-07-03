# Checklist — task-04-saveas-copy

- [x] `SBS/Services/ProjectCopyService.cs`
- [x] `ProjectCopyResult` — Success, TargetPath, ErrorMessage, FileAlreadyExists
- [x] `SaveCopyAs(Document doc, string targetPath, bool overwrite)`
- [x] `SaveAsOptions` — correct for Revit 2025
- [x] Проверка `doc.IsReadOnly`, пустой PathName OK для template
- [x] Worksharing: если `doc.IsWorkshared` → return warning (не fail silently)
- [x] Создать целевую папку если нет
- [x] `dotnet build SBS.sln -c Release`
