# Epic: Revit Materials Sync — ближний скоуп (без MinIO)

**Цель:** по `remont_id` / `client_request_id` плагин Revit получает JSON материалов с полями
для скачивания и кэш-проверки (`revit_file_type/url/hash/asset_name`), без реального хранилища
(ссылки фиктивные, но стабильные).

**DoD эпика:**
- [ ] 4 новых поля в `material_tab`, дефолты корректны для всех существующих строк
- [ ] `public.read_revit_material_by_remont` — дедуп по материалу, только `revit_file_type <> 'none'`
- [ ] `GET /common/revit_material/read/?remont_id=` — рабочий эндпоинт, тот же auth/паттерн что `RevitEventView`
- [ ] Классификация `surface/rfa/none` заполнена для всех активных материалов (фиктивные ссылки)

**Out of scope:** MinIO, реальные `.rfa`/`.rvt` файлы, C#-плагин, изменение `client_material/tk/read/`.

```
task-01 [DB fields] ──→ task-02 [SQL function] ──→ task-03 [Backend endpoint]
                                                          │
task-04 [Fake data fill] ────────────────────────────────┘ (параллельно с task-02/03, но перед QA)
```

---

## task-01 — БД: 4 поля в `material_tab`

**Кто:** Backend
**Оценка:** ~0.5d
**Зависит от:** —

- `ALTER TABLE material_tab ADD COLUMN revit_file_type varchar(20) NOT NULL DEFAULT 'none'`
- `+ revit_file_url varchar(500)`, `revit_file_hash varchar(64)`, `revit_asset_name varchar(200)`
- `CHECK` на `revit_file_type IN ('rfa','surface','none')`

**DoD:**
- [ ] Миграция применена на dev (после явного запроса)
- [ ] Существующие строки не ломаются (дефолт `'none'`, остальные поля `NULL`)
- [ ] `sql/revit-materials-sync/01_material_tab_revit_fields.sql` + README

---

## task-02 — SQL: `public.read_revit_material_by_remont`

**Кто:** Backend
**Оценка:** ~0.5–1d
**Зависит от:** task-01

- Копия паттерна `read_client_material_by_remont` (resolve `remont_id → client_request_id` через `utils.get_client_request_id_by_remont`)
- `DISTINCT ON (material_id)`, фильтр `revit_file_type <> 'none'`
- JOIN `material_type_tab` для `material_type_code`

**DoD:**
- [ ] Функция на dev, ручной вызов через MCP для тестового `remont_id`
- [ ] Ремонт без Revit-материалов → `data: []`, не ошибка
- [ ] Несуществующий `remont_id` → `client_request_id: null`, `data: []`
- [ ] `sql/revit-materials-sync/02_read_revit_material_by_remont.sql` + README
- [ ] `functions/read_revit_material_by_remont.md`

---

## task-03 — Backend: эндпоинт `GET /common/revit_material/read/`

**Кто:** Backend
**Оценка:** ~0.5d
**Зависит от:** task-02

- `common/ex_services/revit_material_services.py` — `read_revit_material_by_remont(remont_id)`, тонкий враппер `call_an_sp`
- `common/ex_views/revit_material_views.py` — `ViewSet`, метод `read`, валидация `remont_id` (как `RevitEventView.read_status`)
- `common/ex_urls/revit_material_urls.py` + подключение в `common/urls.py`

**DoD:**
- [ ] `GET /common/revit_material/read/?remont_id=X` возвращает контракт из `SPEC.md` §4
- [ ] 400 без `remont_id` / не-integer / ≤ 0
- [ ] 401 без JWT
- [ ] Ремонт без данных → `200`, `data: []`

---

## task-04 — Данные: классификация + фиктивные ссылки в `material_tab`

**Кто:** Backend
**Оценка:** ~0.5d
**Зависит от:** task-01 (может идти параллельно с task-02/03)

- SQL-скрипт `UPDATE material_tab SET revit_file_type = ... , revit_file_url = ..., revit_file_hash = md5(...), revit_asset_name = ...` по `material_type_tab.material_type_code` (список групп — `SPEC.md` §2)
- **Не выполнять на dev/prod без явного запроса пользователя** (`no-autonomous-database-writes`)

**DoD:**
- [ ] SQL-скрипт готов и человекочитаем (сначала `SELECT` — превью, потом `UPDATE`)
- [ ] Список `material_type_code` по трём группам подтверждён с пользователем/бизнесом перед выполнением
- [ ] После выполнения — количество строк по каждой группе (`SELECT revit_file_type, COUNT(*) ...`)

---

## Сводка (backend, готово)

| Таска | Кто | ~дни | Зависит | Статус |
|-------|-----|------|---------|--------|
| task-01 | Backend | 0.5 | — | ✅ deployed |
| task-02 | Backend | 0.5–1 | task-01 | ✅ deployed |
| task-03 | Backend | 0.5 | task-02 | ✅ deployed |
| task-04 | Backend | 0.5 | task-01 | ✅ данные подтверждены (2 материала на `remont_id=21838`) |

**Backend итого: ~2–2.5 дня. Эндпоинт `GET /revit/material/read/?remont_id=` проверен вручную 2026-07-01, HTTP 200.**

---

# Фаза 2 — Плагин (Revit add-in), MVP: только список

**Цель:** проектировщик видит в плагине список Revit-релевантных материалов ремонта
(без скачивания/импорта — это следующая фаза).

```
task-05 [Плагин: список материалов] ──→ task-06+ [скачивание .rfa/.rvt, LoadFamily/ImportMaterial] (будущее, не сейчас)
```

## task-05 — Плагин: экран «Материалы (Revit sync)» — список (MVP)

**Кто:** C# / Revit plugin
**Оценка:** ~0.5–1d
**Зависит от:** task-03 (эндпоинт уже готов и проверен)

- Новая кнопка-фича в `RemontHubWindow` → новое окно `RevitMaterialsWindow`
- Дёргает `GET /revit/material/read/?remont_id=`, показывает `DataGrid`
  (`material_id`, `material_name`, `material_type_code`, `revit_file_type`, `revit_asset_name`, `revit_file_url`)
- Обрабатывает пустой список и ошибки (401/400/сеть) без падения плагина
- **Не входит:** скачивание файлов, `LoadFamily`/`ImportMaterial`, кэш по hash (PLAN.md, Фаза 3 — будущие таски)

**DoD:** см. `task/task-05-plugin-materials-list/prompt.md` и `CHECKLIST.md`.

---

## Сводка (Фаза 2)

| Таска | Кто | ~дни | Зависит | Статус |
|-------|-----|------|---------|--------|
| task-05 | Plugin (C#) | 0.5–1 | task-03 | ✅ реализовано, собрано (`dotnet build` — 0 ошибок) |

---

# Фаза 3 — Плагин: кнопка «Синхронизировать» (скачивание + загрузка `.rfa`)

**Цель:** из окна «Материалы (Revit)» одним кликом скачать файлы материалов и загрузить
`.rfa`-семейства в текущий проект Revit (без расстановки экземпляров — это отдельно, дальше).

⚠️ **Важная находка QA (2026-07-01):** `revit_file_url` — presigned MinIO URL с TTL ~12ч,
меняется при каждом запросе к API; `revit_file_hash` пока везде `NULL`. Это меняет наивный
дизайн кэша «по хэшу URL» — см. `decisions/DECISIONS.md` №11–12.

```
task-06 [Скачивание + локальный кэш] ──→ task-07 [LoadFamily в проект (только rfa)]
                                                          │
                                     surface-материалы ───┘ (не входит, будущая task-08+)
```

## task-06 — Плагин: кнопка «Синхронизировать» — скачивание файлов с локальным кэшем

**Кто:** C# / Revit plugin
**Оценка:** ~1d
**Зависит от:** task-05

- Кнопка «Синхронизировать» в `RevitMaterialsWindow`, скачивает файлы по `revit_file_url`
  в `%LOCALAPPDATA%\SmartRemont\revit-materials-cache\`
- Кэш ключуется по `material_id` (не по URL/hash — presigned URL нестабилен, hash пока `NULL`)
- Построчный статус в UI (Ожидает/Скачивается/Готово/Ошибка), ошибка одного файла не блокирует остальные

**DoD:** см. `task/task-06-plugin-download-cache/prompt.md` и `CHECKLIST.md`.

## task-07 — Плагин: загрузка скачанных `.rfa` семейств в проект

**Кто:** C# / Revit plugin
**Оценка:** ~1d
**Зависит от:** task-06

- Только `revit_file_type == "rfa"`, `Document.LoadFamily()` в одной транзакции на батч
- `surface`-материалы — явный статус "не поддерживается", без падений (импорт материалов — future)
- Ошибка `LoadFamily` на одном файле не прерывает остальные
- Итоговая статистика в UI: загружено / скачано без импорта / ошибок

**DoD:** см. `task/task-07-plugin-load-family/prompt.md` и `CHECKLIST.md`.

---

## Сводка (Фаза 3)

| Таска | Кто | ~дни | Зависит | Статус |
|-------|-----|------|---------|--------|
| task-06 | Plugin (C#) | ~1 | task-05 | 📝 задача написана, ждёт согласования |
| task-07 | Plugin (C#) | ~1 | task-06 | 📝 задача написана, ждёт согласования |

**Не входит (будущее, PLAN.md Фаза 4):** импорт `surface`-материалов (`.rvt`-библиотека/текстуры),
параметрические семейства с настройкой под конкретный материал, авторасстановка экземпляров в модели.
