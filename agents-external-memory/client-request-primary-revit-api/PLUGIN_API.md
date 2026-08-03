 ё# Revit Plugin API — display + apply

Документ для агента плагина (C# / SBS). Версия: 2026-08-03.

**Backend-ветка:** `feature/revit-plugin-direct-apply` (ещё не в prod; на dev после merge PR).

**Primary key везде:** `client_request_id`. `remont_id` — опционально в ответах (nullable), не передаётся в apply.

**Staging (`revit_events`) — не использовать.** Плагин пишет изменения напрямую через `POST /revit/plugin/*/apply/`. Office UI продолжает работать со старым import-флоу независимо.

---

## Base URL

| Окружение | URL |
|-----------|-----|
| Dev (office-testapi) | `https://office-testapi.smartremont.kz` |
| Prod | `https://myspace-api.smartremont.kz` |

Все пути ниже — относительно base URL.

---

## Auth

```http
POST /auth/revit/login/
Content-Type: application/json

{ "login": "...", "password": "..." }
```

Ответ: JWT (`access`, `refresh`). Дальше:

```http
Authorization: Bearer {access}
Accept: application/json
```

---

## Обёртка ответа MySpace

Успех:
```json
{ "status": true, "error": null, ...поля... }
```

Ошибка (400):
```json
{ "status": false, "error": "текст ошибки" }
```

---

## Права (grants JWT-пользователя)

| Действие | Grant |
|----------|-------|
| Материалы Revit (read/upload) | `OA__RevitMaterialsShow`, `OA__RevitMaterialsUpload` |
| Поиск заявки | `OA__RemontFormQuickSearch` |
| ТК read | `OA__RemontFormTabulation` |
| ДС ROOM_CHANGE read | `OA__RemontFormDSRoomChangeShow` |
| Замеры read | `OA__RemontFormMeasureBlock` |
| Замеры apply | `OA__RemontFormMeasureSave` |
| ДС apply (создание) | `OA__RemontFormDSAdd` |
| ДС apply (обновление) | `OA__RemontFormDSEdit` |

Новых Revit-специфичных grants нет — те же, что у office UI.

---

## Тестовые ID (dev)

| Сценарий | client_request_id | remont_id |
|----------|-------------------|-----------|
| CR + remont + ДС | 3042046 | 21841 |
| CR без remont | 3043201 | — |

---

# 1. Home / Init

## 1.1 Поиск заявки

```http
POST /client_request/quick_search/
Content-Type: application/json

{ "client_request_id": 3042046 }
```

Grant: `OA__RemontFormQuickSearch`.

Ответ: `data[]` — карточки заявок. В каждой: `client_request_id`, `remont_id` (nullable), `client_name`, `flat_num`, `preset_name`, статусы и т.д.

Работает для CR **без remont** (после fix task-03).

---

## 1.2 Материалы Revit (init / sync)

**Рекомендуемый path (единый неймспейс):**
```http
GET /revit/plugin/material/read/?client_request_id=3042046
```

**Legacy (тот же контракт):**
```http
GET /revit/material/read/?client_request_id=3042046
```

Grant: `OA__RevitMaterialsShow`.

Ответ:
```json
{
  "status": true,
  "error": null,
  "client_request_id": 3042046,
  "remont_id": 21841,
  "surfaces_file_url": "/documents/.../surface.rvt",
  "surfaces_file_hash": "abc123...",
  "data": [
    {
      "material_id": 123,
      "material_name": "...",
      "material_type_id": 1,
      "material_type_code": "...",
      "revit_file_type": "rfa",
      "revit_file_url": "...",
      "revit_file_hash": "...",
      "revit_asset_name": "..."
    }
  ]
}
```

Работает **без remont** — материалы из ТК по CR.

### Доп. material-эндпоинты (init, не под `/plugin/`)

| Method | Path | Назначение |
|--------|------|------------|
| GET | `/revit/material/surfaces/read/` | URL/hash общего surface.rvt |
| POST | `/revit/material/surfaces/upload/` | Загрузка surface.rvt |
| POST | `/revit/material/surfaces/clear/` | Сброс surface.rvt |
| POST | `/revit/material/rfa/upload/` | Загрузка .rfa материала |
| POST | `/revit/material/type/update/` | Смена revit_file_type без файла |
| GET | `/revit/material/preset_kit/read/?preset_kit_id=` | Материалы preset kit |
| GET | `/revit/material/preset_kit/grades/?city_id=` | Грейды города |
| GET | `/revit/material/preset_kit/list/?city_id=&grade_id=` | Список kit |
| GET | `/revit/material/preset_kit/summary/?city_id=&grade_id=` | Summary |

Grant upload: `OA__RevitMaterialsUpload`.

---

# 2. Display — единый неймспейс `/revit/plugin/`

Все GET принимают **только** `?client_request_id={id}` (> 0).

## 2.1 ТК (текстовый конструктор)

```http
GET /revit/plugin/tk/read/?client_request_id=3042046
```

Grant: `OA__RemontFormTabulation`.

```json
{
  "status": true,
  "client_request_id": 3042046,
  "remont_id": 21841,
  "data": [ /* ClientMaterialRow[] — см. sql/client-material/README.md */ ]
}
```

Ключевые поля строки: `client_material_id`, `room_id`, `room_name`, `material_id`, `material_name`, `work_set_name`, `item_cnt`, ...

---

## 2.2 ДС «изменение площади» (ROOM_CHANGE)

```http
GET /revit/plugin/ds/room-change/read/?client_request_id=3042046
```

Grant: `OA__RemontFormDSRoomChangeShow`.

```json
{
  "status": true,
  "client_request_id": 3042046,
  "remont_id": 21841,
  "ds_id": 43326,
  "data": {
    "data": [
      {
        "ds_room_change_id": 1,
        "room_id": 501,
        "room_name": "Спальня 1",
        "room_area": 12.4,
        "prev_room_area": 11.8,
        "action_code": "EDITED",
        "order_num": 1
      }
    ],
    "sum": { "ds_sum": ..., "material_diff": ..., "work_diff": ..., "service_diff": ... },
    "wall_height": 2.7,
    "ds_info": { /* заголовок ДС */ }
  },
  "header": { /* ds_tab + ds_type_name, ds_date, ... */ }
}
```

Если ДС нет: `ds_id: null`, `data: null`, `header: null` (не 500).

---

## 2.3 Замеры — комнаты планировки (lite)

```http
GET /revit/plugin/measures/read/?client_request_id=3042046
```

Grant: `OA__RemontFormMeasureBlock`.

```json
{
  "status": true,
  "client_request_id": 3042046,
  "data": [
    {
      "room_id": 501,
      "room_name": "Спальня 1 (3)",
      "planirovka_room_id": 12345,
      "planirovka_name": "...",
      "is_measure_confirm": 0
    }
  ]
}
```

`planirovka_room_id > 0` — комната добавлена в планировку и доступна для замеров.  
Для текущих значений параметров комнаты (если нужен diff перед apply):

```http
GET /client_request/{client_request_id}/measures/planirovka_rooms/{planirovka_room_id}/read/
```

Ответ: `{ header, measures[] }` где `measures[].param_code`, `param_value`, `param_id`.

---

# 3. Apply — прямая запись из Revit

**Без staging.** Один POST = изменения сразу в MySpace.

Оба apply **идемпотентны**: повторная отправка того же payload безопасна (upsert, не накопление).

---

## 3.1 Замеры

```http
POST /revit/plugin/measures/apply/
Content-Type: application/json

{
  "client_request_id": 3042046,
  "rooms": [
    {
      "room_id": 501,
      "room_name": "Спальня 1",
      "params": [
        { "param_code": "ROOM_PERIMETER", "param_value": "14.2" },
        { "param_code": "ROOM_AREA", "param_value": "12.4" }
      ]
    }
  ]
}
```

Grant: `OA__RemontFormMeasureSave`.

**Remont не требуется** — работает и для CR без remont (если есть планировка).

### Маппинг комнат (server-side)

1. Сначала по `room_id`
2. Если не найдено — по `room_name` (trim, case-insensitive)
3. Комната должна быть в планировке (`planirovka_room_id > 0`)

### param_code

Должен совпадать с `param_tab.param_code` для данной комнаты (рекомендуется UPPERCASE, как в office UI). Недоступные параметры → `skipped[]`.

### Ответ

```json
{
  "status": true,
  "error": null,
  "data": {
    "applied_rooms": 1,
    "applied_params": 2,
    "skipped": [
      { "room_id": 999, "room_name": null, "reason": "Комната не найдена в планировке" }
    ]
  }
}
```

Частичный успех — норма: применённые комнаты записаны, проблемные в `skipped`.

---

## 3.2 ДС «изменение площади» (ROOM_CHANGE)

```http
POST /revit/plugin/ds/room-change/apply/
Content-Type: application/json

{
  "client_request_id": 3042046,
  "wall_height": 2.7,
  "rooms": [
    { "room_id": 501, "new_area": 12.4 },
    { "room_id": 502, "new_area": 8.1 }
  ]
}
```

Grants:
- ДС ещё нет → `OA__RemontFormDSAdd` (создаёт ROOM_CHANGE ДС)
- ДС уже есть → `OA__RemontFormDSEdit` (обновляет площади)

### Требования

- **`remont_id` обязателен** — если ремонта нет → `400`:
  `"Для заявки ещё нет ремонта — ДС «Изменение площади» недоступна"`
- `rooms[]` — обязателен, непустой
- Каждая строка: `room_id` + `new_area` (оба обязательны)
- `wall_height` — опционален; **применяется только при создании новой ДС** (при update существующей шапка ДС не трогается — MVP-ограничение)

### Ответ

```json
{
  "status": true,
  "error": null,
  "data": {
    "ds_id": 43326,
    "remont_id": 21841,
    "created": false,
    "applied_rooms": 2,
    "skipped": [],
    "wall_height_changed": false
  }
}
```

| Поле | Смысл |
|------|-------|
| `created` | `true` если ДС ROOM_CHANGE создана впервые |
| `applied_rooms` | сколько комнат записано |
| `wall_height_changed` | `true` только если wall_height записан при create |

---

# 4. Рекомендуемый flow плагина

```
┌─────────────┐
│ Home        │  POST /client_request/quick_search/  →  client_request_id, remont_id?
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Init/Sync   │  GET /revit/plugin/material/read/  →  materials + surfaces
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Hub UI      │
│  ├─ ТК      │  GET /revit/plugin/tk/read/
│  ├─ ДС      │  GET /revit/plugin/ds/room-change/read/
│  └─ Замеры  │  GET /revit/plugin/measures/read/
└──────┬──────┘
       │ пользователь подтверждает изменения в Revit
       ▼
┌─────────────┐
│ Apply       │  POST /revit/plugin/measures/apply/
│             │  POST /revit/plugin/ds/room-change/apply/  (только если remont_id != null)
└─────────────┘
```

### Disable-логика в UI

| Кнопка | Условие enabled |
|--------|-----------------|
| Init / Materials / ТК read | `client_request_id > 0` |
| Замеры apply | `client_request_id > 0` + grant |
| ДС apply | `client_request_id > 0` **и** `remont_id != null` + grant |

---

# 5. Полная таблица эндпоинтов

## Display

| Method | Path | Grant | remont нужен? |
|--------|------|-------|---------------|
| POST | `/auth/revit/login/` | — | — |
| POST | `/client_request/quick_search/` | QuickSearch | нет |
| GET | `/revit/plugin/material/read/?client_request_id=` | RevitMaterialsShow | нет |
| GET | `/revit/plugin/tk/read/?client_request_id=` | Tabulation | нет |
| GET | `/revit/plugin/ds/room-change/read/?client_request_id=` | DSRoomChangeShow | нет |
| GET | `/revit/plugin/measures/read/?client_request_id=` | MeasureBlock | нет |
| GET | `/client_request/{id}/measures/planirovka_rooms/{planirovka_room_id}/read/` | MeasureBlock/RoomDetail | нет |
| GET | `/revit/material/surfaces/read/` | RevitMaterialsShow | — |

## Apply (новое)

| Method | Path | Grant | remont нужен? |
|--------|------|-------|---------------|
| POST | `/revit/plugin/measures/apply/` | MeasureSave | **нет** |
| POST | `/revit/plugin/ds/room-change/apply/` | DSAdd / DSEdit | **да** |

## Upload (init)

| Method | Path | Grant |
|--------|------|-------|
| POST | `/revit/material/rfa/upload/` | RevitMaterialsUpload |
| POST | `/revit/material/surfaces/upload/` | RevitMaterialsUpload |
| POST | `/revit/material/surfaces/clear/` | RevitMaterialsUpload |
| POST | `/revit/material/type/update/` | RevitMaterialsUpload |

## Не использовать в новом плагине

| Path | Причина |
|------|---------|
| `POST /common/revit_events/create/` | staging — заменён apply |
| `GET /common/revit_events/status/` | staging |
| `GET/POST .../measures/revit_event/*` | office import UI |

---

# 6. Примеры curl (dev)

```bash
TOKEN=$(curl -sS -X POST "https://office-testapi.smartremont.kz/auth/revit/login/" \
  -H "Content-Type: application/json" \
  -d '{"login":"...","password":"..."}' | jq -r .access)

CR=3042046

# Display
curl -sS -H "Authorization: Bearer $TOKEN" \
  "https://office-testapi.smartremont.kz/revit/plugin/tk/read/?client_request_id=$CR"

curl -sS -H "Authorization: Bearer $TOKEN" \
  "https://office-testapi.smartremont.kz/revit/plugin/ds/room-change/read/?client_request_id=$CR"

curl -sS -H "Authorization: Bearer $TOKEN" \
  "https://office-testapi.smartremont.kz/revit/plugin/measures/read/?client_request_id=$CR"

# Apply measures
curl -sS -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "https://office-testapi.smartremont.kz/revit/plugin/measures/apply/" \
  -d "{\"client_request_id\":$CR,\"rooms\":[{\"room_id\":501,\"params\":[{\"param_code\":\"ROOM_AREA\",\"param_value\":\"12.4\"}]}]}"

# Apply DS (requires remont)
curl -sS -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "https://office-testapi.smartremont.kz/revit/plugin/ds/room-change/apply/" \
  -d "{\"client_request_id\":$CR,\"wall_height\":2.7,\"rooms\":[{\"room_id\":501,\"new_area\":12.4}]}"
```

---

# 7. Out of scope (отдельный эпик)

- **DS TK_CHANGE apply** — изменение материалов ТК через ДС
- Update `wall_height` для уже существующей ROOM_CHANGE ДС
- Apply без remont для ДС
