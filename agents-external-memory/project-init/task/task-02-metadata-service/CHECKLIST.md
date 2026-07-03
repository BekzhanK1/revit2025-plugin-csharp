# Checklist — task-02-metadata-service

- [x] `SBS/Services/ProjectRemontMetadataService.cs`
- [x] DTO `ProjectRemontMetadata` (RemontId, ClientRequestId, InitializedAt, PluginVersion)
- [x] `TryRead(Document doc)` — Entity на ProjectInformation
- [x] `Write(Document doc, ProjectRemontMetadata)` — Transaction Manual
- [x] `IsInitialized(doc)`, `ValidateMatches(doc, expectedRemontId)`
- [x] Лог Info при write/read
- [x] `dotnet build SBS.sln -c Release`
- [ ] Manual: write/read в Revit через временную debug-команду или unit-less smoke
