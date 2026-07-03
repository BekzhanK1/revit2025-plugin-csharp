# SPEC — Project Init (инициализация RVT)

## Проблема

Сейчас `remont_id` живёт только в памяти плагина (`ExportRoomsApplication.SelectedRemont`). Пользователь каждый раз ищет ремонт вручную. Материалы синхронизируются в **текущий** открытый RVT без «паспорта» ремонта.

## Целевой flow

```
Пользователь открыл шаблон / пустой RVT
  → Smart Remont → выбор ремонта (Home)
  → Hub → «Инициализировать проект»
       1. SaveAs → {remont_id}_{ЖК}.rvt
       2. Stamp remont_id в Extensible Storage (+ опционально Project Info)
       3. Full sync материалов (RFA + surfaces)
       4. Save
       5. (Опционально) напомнить переключиться на новый файл, если SaveAs не активировал его

Пользователь открыл уже инициализированный {21642_ЖК.rvt}
  → Плагин читает Storage → SelectedRemont заполнен автоматически
  → Home можно пропустить или показать «Ремонт привязан к проекту»
  → Hub сразу с правильным remont_id
```

---

## 1. Метаданные ремонта в модели

### Extensible Storage (источник истины)

| Поле | Тип | Описание |
|------|-----|----------|
| `remont_id` | int | ID ремонта Smart Remont |
| `client_request_id` | int | ID заявки |
| `initialized_at` | string (ISO8601 UTC) | Когда инициализирован |
| `plugin_version` | string | Версия add-in при init |

- Schema: фиксированный GUID в коде (`ProjectRemontSchema.cs`)
- Entity на элементе `ProjectInformation`
- **Не видно** в стандартном UI Revit → пользователь не меняет случайно

### Project Information (опционально, phase 2)

- Shared parameter `SR_REMONT_ID` (Text/Integer) — для BIM-координаторов
- **Не** считать источником истины (редактируемо)
- При расхождении с Storage — warning в лог + диалог

### «Нельзя вбить вручную»

| Уровень | Реализация |
|---------|------------|
| UI Revit | Storage — скрыт, надёжно |
| Плагин | При отправке API сверять Storage ↔ `SelectedRemont` |
| Файл | Имя `{remont_id}_…rvt` — дополнительная подсказка |

---

## 2. SaveAs + именование файла

### Шаблон имени

```
{remont_id}_{resident_name_sanitized}.rvt
```

Пример: `21642_ЖК_Алатау.rvt`

- `resident_name` из `RemontOption.ResidentName`; fallback `RemontOption.Name` или `Remont_{id}`
- Sanitize: `Path.GetInvalidFileNameChars`, max length ~80 символов
- Папка по умолчанию: `%USERPROFILE%\Documents\SmartRemont\Projects\` (настраиваемо позже)

### Поведение при существующем файле

1. Файл существует + Storage с **тем же** remont_id → «Открыть существующий?» / «Пересинхронизировать материалы»
2. Файл существует + **другой** remont_id → ошибка, не перезаписывать молча
3. Нет файла → SaveAs + init

### Ограничения Revit

- Документ должен быть сохраняемым (не read-only)
- Worksharing: SaveAs создаёт **новую** модель; central path — out of scope v1
- Несохранённый doc: предложить Save/SaveAs перед init или SaveAs из текущего состояния

---

## 3. Full sync при инициализации

Переиспользовать существующий код:

- `RevitMaterialsService.ReadAsync(remontId)`
- `RevitMaterialsDownloadService` (RFA + surfaces)
- `RevitFamilyImportService` + `RevitSurfaceImportService`
- `RevitMaterialPresenceService` — проверка после sync

**Отличие от кнопки «Синхронизировать»:** init = обязательно все материалы из ответа API, затем Save.

---

## 4. UI

### Hub — новая карточка (или расширение «Синхронизация материалов»)

**«Инициализировать проект»**  
Subtitle: «Копия RVT, remont_id в модели, загрузка всех материалов»

Состояния:
- Проект **не** инициализирован → кнопка активна
- Проект инициализирован, remont совпадает → «Проект инициализирован» / «Пересинхронизировать»
- Storage remont ≠ выбранный remont → warning, блок init

### Home

- Если активный doc уже имеет Storage → auto-fill `SelectedRemont`, опционально skip search → hub
- Badge: «Ремонт привязан: #21642»

### ExportSmartRemontRoomsCommand

- При старте: `ProjectRemontMetadataService.TryRead(doc)` → если есть, set `SelectedRemont` из API/cache по id

---

## 5. Out of scope (v1)

- Backend endpoint «project initialized»
- MinIO upload RVT
- Worksharing central migration
- Блокировка Project Info параметра на уровне Revit (нет API)
- Автоматическое закрытие исходного файла после SaveAs

---

## 6. Acceptance criteria

1. После init файл на диске `{remont_id}_{ЖК}.rvt` содержит Storage с remont_id
2. Повторное открытие файла — hub знает remont без Home-поиска
3. Материалы из API remont 21642 — в проекте (presence check)
4. Init на файле с другим remont_id в Storage — отказ с понятным сообщением
