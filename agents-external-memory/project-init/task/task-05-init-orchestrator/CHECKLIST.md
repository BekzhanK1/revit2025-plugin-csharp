# Checklist — task-05-init-orchestrator

- [x] `SBS/Services/ProjectInitService.cs`
- [x] `ProjectInitResult` — Success, NewFilePath, MaterialsLoaded, Errors
- [x] Pipeline:
  1. Validate remont has RemontId
  2. Validate doc not conflicting Storage (Decision #4)
  3. Build target path (task-03)
  4. SaveAs (task-04)
  5. Write Storage (task-02) — stamp **после** SaveAs на resulting doc
  6. Materials full sync (refactor from RevitMaterialsWindow)
  7. doc.Save()
- [x] Вынести `RevitMaterialsSyncOrchestrator` из window (Decision #7)
- [x] `dotnet build SBS.sln -c Release`

## Manual smoke

- [ ] Init на remont 21642 → файл создан, Storage читается, материалы в проекте
