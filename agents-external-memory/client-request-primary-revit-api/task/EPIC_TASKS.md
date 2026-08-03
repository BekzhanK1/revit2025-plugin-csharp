# Epic: client-request-primary — таски

**Цель:** все Revit API (и затем плагин) работают от `client_request_id`; `remont_id` — optional/legacy.

**DoD эпика (API):**
- [ ] Материалы по CR (SP + endpoint), legacy `remont_id` жив
- [ ] Quick search отдаёт CR без remont
- [ ] TK read + DS room-change принимают CR
- [ ] Events: create/status принимают CR; без remont → 409
- [ ] Decisions #1–#6 зафиксированы

**Out of scope:** events write без remont (A), backfill event_log, новые rights, C#-плагин (task-07 отдельно после API).

```
task-01 [SQL by CR] ──→ task-02 [material/read query-param]
                              │
task-03 [quick_search] ───────┤ (параллельно с 01–02)
                              │
                    task-04 [tk/read] ──→ task-05 [ds/room-change]
                              │
                    task-06 [revit_events CR + 409]
                              │
                    task-07 [plugin follow-up]  (после P0–P2 API)
```

Решения: [../decisions/DECISIONS.md](../decisions/DECISIONS.md)  
Полный контракт: [../TASK.md](../TASK.md)

---

## task-01 — SQL: `read_revit_material_by_client_request`

**Кто:** Backend · **Prio:** P0 · **Оценка:** ~0.5–1d  
**Зависит от:** —  
**Папка:** [task-01-sql-by-client-request/](task-01-sql-by-client-request/)

- Новая SP `public.read_revit_material_by_client_request(cur, client_request_id_)`
- Без шага через remont; `remont_id` в ответе nullable
- Дедуп / фильтр как у `read_revit_material_by_remont`
- Старую SP оставить thin wrapper → новая (или оставить как есть + parallel)

---

## task-02 — Backend: `GET /revit/material/read/?client_request_id=`

**Кто:** Backend · **Prio:** P0 · **Оценка:** ~0.5d  
**Зависит от:** task-01  
**Папка:** [task-02-material-read-by-cr/](task-02-material-read-by-cr/)  
**Decision:** #3 — только query-param, без нового path

- Принимать `client_request_id` и/или `remont_id`
- Priority CR; оба → validate match; ни одного → 400
- Response: оба id, `remont_id` nullable

---

## task-03 — Quick search: CR без remont

**Кто:** Backend · **Prio:** P0 · **Оценка:** ~0.5d  
**Зависит от:** — (параллельно)  
**Папка:** [task-03-quick-search-cr/](task-03-quick-search-cr/)

- `POST /client_request/quick_search/` с `client_request_id` возвращает карточку при `remont_id = null`
- Не фильтровать заявки без remont
- Контракт `data[]`: `client_request_id` always, `remont_id` nullable

---

## task-04 — `client_material/tk/read` by CR

**Кто:** Backend · **Prio:** P1 · **Оценка:** ~0.5d  
**Зависит от:** — (желательно после task-02 для единого паттерна валидации)  
**Папка:** [task-04-tk-read-by-cr/](task-04-tk-read-by-cr/)

```
GET /common/client_material/tk/read/?client_request_id=
+ legacy ?remont_id=
```

---

## task-05 — `ds/room-change/read` by CR

**Кто:** Backend · **Prio:** P1 · **Оценка:** ~0.5d  
**Зависит от:** —  
**Папка:** [task-05-ds-room-change-by-cr/](task-05-ds-room-change-by-cr/)

```
GET /common/ds/room-change/read/?client_request_id=
+ legacy ?remont_id=
```

---

## task-06 — Revit events: CR + 409 без remont

**Кто:** Backend · **Prio:** P2 · **Оценка:** ~1d  
**Зависит от:** —  
**Папка:** [task-06-revit-events-cr/](task-06-revit-events-cr/)  
**Decisions:** #4 (вариант B), #5 (колонка сейчас, backfill later)

- create/status принимают `client_request_id` (+ optional `remont_id`)
- Резолв remont; нет remont → **409**
- DDL: nullable `client_request_id` в `revit_event_log`, писать при create
- Payload types не менять

---

## task-07 — Plugin: primary = `client_request_id`

**Кто:** Plugin (C#) · **Prio:** после API P0–P2 · **Оценка:** ~1–2d  
**Зависит от:** task-02, task-03 (минимум); желательно 04–06  
**Папка:** [task-07-plugin-primary-cr/](task-07-plugin-primary-cr/)

- Storage / init / naming / Home search / Configs / hub buttons
- Events UI: disable пока `remont_id == null` (decision #4)
