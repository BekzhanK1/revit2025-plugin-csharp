# Task: Primary key плагина = `client_request_id`

**Статус:** API — в работе (backend owner). Плагин — follow-up после API.  
**Дата:** 2026-08-03  
**Контекст:** сейчас init, метаданные RVT и почти все Revit-эндпоинты завязаны на `remont_id`. Данные ТК живут на `client_request_tab` / `client_material_tab`.

## Цель

Привязка RVT-проекта и все Revit API вызовы — к `client_request_tab.client_request_id`, а не к `remont_id`.  
`remont_id` остаётся **опциональным** (nullable), резолвится когда ремонт уже создан.

## Контекст (MCP, smart-remont-dev)

| Факт | Значение |
|------|----------|
| `client_material_tab` | FK = `client_request_id` (не remont) |
| `remont_tab` → CR | сейчас **1:1** (~13k / 13k) |
| Remont без CR | 0 |
| CR без remont | ~680k заявок |
| Init без remont сегодня | невозможен (материалы только по `remont_id`) |

---

## 0. Общий контракт API

### Правила

1. **Primary key запроса** — `client_request_id` (обязателен, где применимо).
2. `remont_id` — **optional**: принимать для backward compat, но не требовать.
3. Если переданы оба — сверять: `remont.client_request_id == client_request_id`, иначе `400`.
4. Если передан только `remont_id` — резолвить CR через `utils.get_client_request_id_by_remont` (legacy).
5. Если передан только `client_request_id` — работать без remont; в ответе `remont_id: null`, если ремонта нет.
6. Ответы всегда возвращают оба поля: `client_request_id`, `remont_id` (nullable).

### Ошибки

| Ситуация | Ответ |
|----------|--------|
| Нет ни `client_request_id`, ни `remont_id` | 400 / `{status:false, error:"Не указан client_request_id"}` |
| CR не найден | 404 или `{status:false, error:"..."}` как принято в office_api |
| Оба переданы и не совпадают | 400 mismatch |
| Для events: CR есть, remont нет | см. §3 |

---

## 1. Материалы Revit (P0 — блокирует init)

### Сейчас

```
GET /revit/material/read/?remont_id={id}
SP: public.read_revit_material_by_remont(cur, remont_id_)
  → get_client_request_id_by_remont → client_material_tab
```

Файлы (ориентир): `revit/ex_services/revit_material_services.py`, `revit/ex_views/revit_material_views.py`, SP `read_revit_material_by_remont`.

### Нужно

**A. Новый SP (предпочтительно):**

```sql
public.read_revit_material_by_client_request(cur refcursor, client_request_id_ integer)
```

Логика как у текущей SP, но **без** шага через remont:

1. `client_request_id_` обязателен
2. `remont_id` в ответе = `(SELECT remont_id FROM remont_tab WHERE client_request_id = … LIMIT 1)` или `NULL`
3. `data` из `client_material_tab` + `material_tab` (дедуп `DISTINCT ON material_id`, `revit_file_type <> 'none'`)

Старую SP можно оставить thin wrapper → новая по CR.

**B. Endpoint (backward compatible, decision #3 — без нового path):**

```
GET /revit/material/read/?client_request_id={id}
GET /revit/material/read/?remont_id={id}                         -- legacy
GET /revit/material/read/?client_request_id=&remont_id=           -- оба, с валидацией
```

Priority: если задан `client_request_id` — он primary. Отдельный `read_by_cr/` не делаем.

**C. Response (без ломания контракта):**

```json
{
  "status": true,
  "client_request_id": 3042029,
  "remont_id": 21838,
  "surfaces_file_url": "...",
  "surfaces_file_hash": "...",
  "data": []
}
```

`remont_id` может быть `null`.

---

## 2. Quick search (P0)

### Сейчас

```
POST /client_request/quick_search/
body: { "remont_id"?: int, "client_request_id"?: int }
```

Плагин уже умеет оба параметра; UI ищет только по remont.

### Нужно проверить / дожать

| # | Требование |
|---|------------|
| 1 | Поиск **только** по `client_request_id` возвращает карточку даже если `remont_id = null` |
| 2 | В `data[]` всегда есть `client_request_id`; `remont_id` nullable |
| 3 | Поля для UI: `client_name`, `resident_name` / `prop_fio`, `flat_num`, `preset_name`, статусы |
| 4 | Право доступа то же (`OA__RemontFormQuickSearch` или аналог для CR) |

Если сейчас quick_search отфильтровывает заявки без remont — **убрать этот фильтр** (или сделать явный флаг).

---

## 3. Revit events — замеры / ДС площадей (P2)

### Сейчас

```
POST /common/revit_events/create/
body: { "remont_id": int, "type": "DS_AREA_CHANGE"|"MEASURES", "payload": {...} }

GET /common/revit_events/status/?remont_id={id}&type={type}
```

Таблица `revit_event_log` — колонка `remont_id`.

### Решение (зафиксировано)

**Вариант B (MVP):** API принимает `client_request_id`, внутри резолвит `remont_id`.  
Если remont нет → `409` / `{status:false, error:"Для заявки ещё нет ремонта"}`.  
Плагин дизейблит «Замеры» / «ДС» пока `remont_id == null`.

Также: nullable `client_request_id` в `revit_event_log` при create; mass backfill — later.

Типы payload (`DS_AREA_CHANGE`, `MEASURES`) — **не менять**.

→ [decisions/DECISIONS.md](decisions/DECISIONS.md) #4, #5.

---

## 4. DS room-change read (P1)

### Сейчас

```
GET /common/ds/room-change/read/?remont_id={id}
```

Ответ уже содержит `remont_id` + `client_request_id`.

### Нужно

```
GET /common/ds/room-change/read/?client_request_id={id}
+ legacy ?remont_id=
```

Внутри: CR → rooms. Если remont нужен для бизнес-логики — резолвить; если нет remont и данных нет — пустой список / понятная ошибка.

---

## 5. Client material TK read (P1)

### Сейчас

```
GET /common/client_material/tk/read/?remont_id={id}
```

Данные фактически по CR.

### Нужно

```
GET /common/client_material/tk/read/?client_request_id={id}
+ legacy ?remont_id=
```

Response: оба id + тот же контракт ТК.

---

## 6. Не трогать (или later)

| Endpoint | Почему |
|----------|--------|
| `POST /auth/revit/login/` | не связан с CR/remont |
| `POST /common/catalog/validate_material_ids/` | валидация по `material_id` |
| Старые UI-эндпоинты ТК (не `/revit/`) | не ломать office UI |

---

## 7. Приоритет API

| Prio | Работа | Блокирует |
|------|--------|-----------|
| **P0** | `GET /revit/material/read/?client_request_id=` + SP by CR | Init проекта |
| **P0** | Quick search: CR без remont в выдаче | Поиск/выбор на Home |
| **P1** | `client_material/tk/read` by CR | Экран ТК в хабе |
| **P1** | `ds/room-change/read` by CR | Экран ДС |
| **P2** | `revit_events` create/status by CR (+ колонка в log) | Замеры / площади без remont |
| **P3** | Deprecate query/body `remont_id` (оставить 1–2 релиза) | — |

---

## 8. Acceptance criteria (API)

- [ ] Материалы: `?client_request_id=X` → тот же `data[]`, что сейчас через remont этой заявки
- [ ] Материалы: CR без remont → `status:true`, `remont_id:null`, `data` из ТК (не 500)
- [ ] Материалы: `?remont_id=` всё ещё работает
- [ ] Материалы: оба параметра с mismatch → 400
- [ ] Quick search по CR возвращает карточку с `client_request_id`, `remont_id` nullable
- [ ] Events (P2): create/status принимают `client_request_id`; documented поведение без remont
- [ ] Документ / пример curl или Postman на testapi

### Примеры

```http
GET /revit/material/read/?client_request_id=3042029
Authorization: Bearer <token>
```

```http
POST /client_request/quick_search/
{"client_request_id": 3042029}
```

```http
POST /common/revit_events/create/
{
  "client_request_id": 3042029,
  "remont_id": null,
  "type": "MEASURES",
  "payload": { "source": "revit", "version": 1, "rooms": [] }
}
```

*(P2 — контракт согласовать до реализации, см. open questions)*

---

## 9. Плагин (follow-up после API)

Не блокирует старт API-работ. После готовности P0:

1. **Primary в Storage:** `client_request_id` обязателен; `remont_id` optional (schema уже оба поля — `ProjectRemontSchema`).
2. **`IsInitialized` / `CanUseHubWorkFeatures`:** проверять `client_request_id > 0`.
3. **Именование файла:** `{client_request_id}_{name}.rvt` (миграция старых `{remont_id}_*` — отдельный подтаск).
4. **Home:** поиск по `client_request_id` (default), опционально toggle remont.
5. **Configs / services:** URL на CR; remont только для legacy.
6. **Хабы/кнопки:** init + материалы работают без remont; events — по решению §3 A/B.
7. **Conflict check:** сравнивать `client_request_id`, не remont.

Ключевые файлы плагина:

| Область | Файлы |
|---------|--------|
| Configs | `SBS/Configs.cs` |
| Init | `ProjectInitService.cs`, `ProjectFileNamingService.cs`, `ProjectRemontMetadataService.cs` |
| Binding | `ProjectRemontBindingService.cs` |
| Materials | `RevitMaterialsService.cs`, `RevitMaterialsSyncOrchestrator.cs` |
| Events | `RevitEventsService.cs`, DTO `RevitEventDtos.cs` |
| UI | `HomeWindow`, `RemontHubWindow`, `ProjectInitPreviewWindow` |

---

## 10. Решения (вместо open questions)

Все рекомендации приняты — см. [decisions/DECISIONS.md](decisions/DECISIONS.md):

| # | Решение |
|---|---------|
| #3 | Только `?client_request_id=` на существующем `/revit/material/read/` |
| #4 | Events без remont → **409** (MVP) |
| #5 | Колонка в `revit_event_log` сейчас, backfill later |
| #6 | Текущие JWT / existing rights, без новых UserRightItemTab |

Таски: [task/EPIC_TASKS.md](task/EPIC_TASKS.md)

---

## Связанные документы

- [task/EPIC_TASKS.md](task/EPIC_TASKS.md)
- [revit-materials-sync/api/backend.md](../revit-materials-sync/api/backend.md)
- [revit-materials-sync/functions/read_revit_material_by_remont.md](../revit-materials-sync/functions/read_revit_material_by_remont.md)
- [project-init/SPEC.md](../project-init/SPEC.md) — текущий init на `remont_id`
- [smart-remont-revit-plugin/API_INTEGRATION.md](../smart-remont-revit-plugin/API_INTEGRATION.md)
