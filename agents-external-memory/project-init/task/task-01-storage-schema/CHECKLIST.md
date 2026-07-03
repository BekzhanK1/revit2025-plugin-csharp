# Checklist — task-01-storage-schema

- [x] `SBS/ProjectRemont/ProjectRemontSchema.cs` — Guid, field names constants
- [x] `GetOrCreateSchema()` — thread-safe lazy
- [x] Поля: `remont_id` (int), `client_request_id` (int), `initialized_at` (string), `plugin_version` (string)
- [x] GUID записан в `decisions/DECISIONS.md`
- [x] `SBS.csproj` — Compile Include
- [x] `dotnet build SBS.sln -c Release`
