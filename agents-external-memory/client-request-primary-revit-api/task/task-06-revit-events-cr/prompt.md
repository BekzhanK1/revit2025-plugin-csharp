# Task 06 — Revit events: CR + 409 без remont

Скопируй блок «Промпт для агента» агенту.

---

## Промпт для агента

```
Реализуй Task 06 эпика client-request-primary: revit_events create/status по client_request_id.

Прочитай:
- agents-external-memory/client-request-primary/TASK.md §0, §3
- agents-external-memory/client-request-primary/decisions/DECISIONS.md (#4, #5)
- common/ex_views/revit_event_views.py, common/ex_services/revit_event_services.py
- таблица revit_event_log (сейчас колонка remont_id)

Decisions:
- #4 вариант B: без remont → 409 «Для заявки ещё нет ремонта»
- #5: добавить nullable client_request_id в revit_event_log, писать при create; mass backfill — НЕ делать

Требования:
1. POST /common/revit_events/create/ — body: client_request_id (preferred) и/или remont_id; type; payload
2. GET /common/revit_events/status/?client_request_id=&type= (+ legacy ?remont_id=)
3. Резолв: CR → remont_id; если remont нет → 409, status:false
4. Если оба id переданы → validate match, иначе 400
5. DDL: ALTER revit_event_log ADD client_request_id integer NULL (если ещё нет); заполнять при create
6. Payload types DS_AREA_CHANGE / MEASURES — НЕ менять
7. Response create/status: оба id где применимо
8. SQL-миграцию в репо; apply на dev только после явного approve

Обнови CHECKLIST.md.
```

---

## DoD

- [ ] create/status принимают `client_request_id`
- [ ] CR без remont → **409**
- [ ] Legacy `remont_id` работает
- [ ] mismatch → 400
- [ ] Колонка `client_request_id` в log, пишется при create
- [ ] Backfill не делаем
- [ ] Payload контракт без изменений
