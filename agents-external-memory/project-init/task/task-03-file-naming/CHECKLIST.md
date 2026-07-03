# Checklist — task-03-file-naming

- [x] `SBS/Services/ProjectFileNamingService.cs`
- [x] `BuildFileName(remontId, residentName)` → `21642_ЖК_Алатау.rvt`
- [x] `BuildFullPath(remontId, residentName, baseFolder?)`
- [x] Sanitize invalid chars, trim, max 80 chars base name
- [x] Default folder: `Environment.GetFolderPath(MyDocuments)\SmartRemont\Projects`
- [x] Unit-testable pure functions (можно без test project — manual verify)
- [x] `dotnet build SBS.sln -c Release`
