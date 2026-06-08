# Интеграция с backend API

Базовый URL: `Configs.ApiOriginUrl` из `app.config` → `apiOriginUrl`.

Авторизация: `Authorization: Bearer {access_token}` из `ExportRoomsApplication.CurrentSession`.

---

## Авторизация

| Метод | URL | Тело |
|-------|-----|------|
| POST | `/auth/revit/login/` | `{ "email", "password" }` |

**Ответ:** `token.access`, `token.refresh`, `user` (fio, email, …)

**Код:** `AuthService`, `AuthDtos`, `AuthStorage` → `auth.session.json`

Refresh token в коде **не используется** для продления сессии.

---

## Поиск ремонта

| Метод | URL | Тело |
|-------|-----|------|
| POST | `/client_request/quick_search/` | `{ "remont_id": N }` или `{ "client_request_id": N }` |

**Код:** `RemontService.QuickSearchAsync` → `RemontOption[]`

403 → сообщение про право `OA__RemontFormQuickSearch`.

---

## Revit events

| Метод | URL | Назначение |
|-------|-----|------------|
| POST | `/common/revit_events/create/` | Создать событие |
| GET | `/common/revit_events/status/?remont_id={id}&type={type}` | Статус импорта |

**Код:** `RevitEventsService`

### Типы событий

| `type` | Константа | Экран |
|--------|-----------|-------|
| `DS_AREA_CHANGE` | `RevitEventTypes.DsAreaChange` | SelectedRemontSummaryWindow |
| `MEASURES` | `RevitEventTypes.Measures` | RoomMeasurementsWindow |

### Тело create

```json
{
  "remont_id": 123,
  "type": "MEASURES",
  "payload": { ... }
}
```

Payload — см. [DATA_SOURCES.md](DATA_SOURCES.md).

### Ответ create

`data.id`, `data.remont_id`, `data.event_type_code`, `data.created_at`

### Статус

`has_event`, `event_id`, `is_imported`, `created_at` — отображается в баннерах (`RevitEventStatusUi`).

---

## Требования для отправки

1. Авторизованный пользователь
2. `SelectedRemont.RemontId > 0`
3. Непустой payload (комнаты / замеры)

---

## Проверка ID материалов в каталоге

| Метод | URL | Тело |
|-------|-----|------|
| POST | `/common/catalog/validate_material_ids/` | `{ "material_ids": ["12133", "6225"] }` |

**Код:** `MaterialValidationService.ValidateMaterialIdsAsync` → экран `RoomMaterialsWindow`

**Назначение:** при открытии «Материалы по комнатам» плагин собирает уникальные ID из модели и ведомости краски, отправляет на бэкенд и подсвечивает **зелёным** строки, чей ID есть в базе.

### Ответ

```json
{
  "status": true,
  "error": null,
  "data": {
    "found_ids": ["12133", "6225"]
  }
}
```

- `found_ids` — список ID, найденных в каталоге (сравнение в плагине без учёта регистра).
- ID, не попавшие в `found_ids`, остаются без подсветки.
- Пустой `material_ids` → `found_ids: []`, `status: true`.

### Требования

1. Авторизованный пользователь (`Bearer` token).
2. Эндпоинт должен искать материалы по коду изделия в каталоге Smart Remont (таблица/справочник материалов — на усмотрение бэкенда).

### Источники ID в Revit

| Параметр | Где используется |
|----------|------------------|
| `ADSK_Код изделия` | отделка, мебель, общие изделия |
| `Код по классификатору` | Blocks / Celite и др. |
| `ERBO_ЭОМ_Наименование` | розетки, выключатели, электрика |

Приоритет отображения в таблице: ADSK → классификатор → ЭОМ.

---

## Ошибки

- 401 → «Сессия истекла…»
- Тело с `error` / `detail` парсится в `TryReadErrorMessage`

Логи HTTP: `ExportRoomsApplication._logger` (Serilog, Warning на ошибки статуса в хабе).
