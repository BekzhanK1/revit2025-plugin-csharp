# Task 07 — Plugin: primary = `client_request_id`

Скопируй блок «Промпт для агента» агенту. **C# / SBS**, после готовности API P0 (task-02, task-03).

---

## Промпт для агента

```
Реализуй Task 07 эпика client-request-primary: плагин Smart Remont переходит на client_request_id как primary key.

Прочитай:
- agents-external-memory/client-request-primary/TASK.md §9
- agents-external-memory/client-request-primary/decisions/DECISIONS.md (#1, #4)
- agents-external-memory/client-request-primary/task/EPIC_TASKS.md
- SBS: Configs.cs, ProjectInitService, ProjectRemontMetadataService, ProjectFileNamingService,
  ProjectRemontBindingService, RemontService, RevitMaterialsService, RevitEventsService,
  HomeWindow, RemontHubWindow

Требования:
1. IsInitialized / CanUseHubWorkFeatures / ValidateMatches — по client_request_id > 0
2. Init пишет оба поля; conflict check по client_request_id
3. Именование файла: {client_request_id}_{name}.rvt (старые {remont_id}_* — совместимость чтения, отдельный note)
4. Home: поиск по client_request_id по умолчанию (RemontService already supports byRemontId:false)
5. Configs + services materials/tk/ds/events — передавать client_request_id; remont optional
6. Decision #4: кнопки Замеры / ДС disabled или сообщение, пока SelectedRemont.RemontId == null
7. Init + sync материалов работают при RemontId == null
8. Минимальный diff; сборка dotnet build SBS.sln -c Release
9. Не коммитить без запроса

Обнови CHECKLIST.md. При необходимости короткая заметка в external-agent-memory/.
```

---

## DoD

- [ ] Init/bind по `client_request_id`
- [ ] Имя файла на CR
- [ ] Home search по CR
- [ ] Materials API вызывается с CR
- [ ] Events UI уважает 409 / disable без remont
- [ ] Release build OK
