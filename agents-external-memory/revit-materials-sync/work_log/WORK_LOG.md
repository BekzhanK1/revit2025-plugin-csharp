# Work Log — revit-materials-sync

## 2026-07-01

**Сделано:**
- Разобрана презентация `smart_remont_revit_flow.pdf` (5 шагов: `client_request_id` → список материалов → проверка хэша → скачивание → подгрузка в Revit)
- Проверена структура `client_request_tab`, `material_tab` на dev через MCP; уточнено — `client_request_material_tab` не существует, реальная связка `client_material_tab` (= ТК)
- Обнаружен уже готовый паттерн под эту задачу: `sql/client-material/read_client_material_by_remont.sql` + `GET /common/client_material/tk/read/?remont_id=` (изначально сделан «для Revit-плагина»)
- Согласован минимальный набор новых полей `material_tab`: `revit_file_type`, `revit_file_url`, `revit_file_hash`, `revit_asset_name` (без `revit_category` — она уже в `.rfa`)
- Обсуждена масштабируемость: работает при параметрических `.rfa`-семействах (~15–20 типов), а не файле на каждую номенклатуру
- Спроектирован полный скоуп ближнего спринта: `SPEC.md`, `decisions/DECISIONS.md`, `task/EPIC_TASKS.md` + task-01…04 (prompt.md + CHECKLIST.md)

**Task-01 (DDL, без применения на dev):**
- MCP dev: `material_tab` — 51 колонка, `revit_*` отсутствуют
- `sql/revit-materials-sync/01_material_tab_revit_fields.sql` — 4 колонки + 2 CHECK (type enum, none→NULL)
- `sql/revit-materials-sync/README.md`, блок в `sql/README.md`
- `schemas/material_tab_revit_fields.md`, CHECKLIST task-01
- Конвенция **`task/task-0N-*/work/*.sql`**: рабочие SQL рядом с таской; deploy-копия в `sql/` (правила в `agent-memory.mdc`, `sql-artifacts.mdc`)
- `task-01-db-fields/work/01_material_tab_revit_fields.sql` — синхронизирован с `sql/revit-materials-sync/`

**Task-02 (SQL-функция, без применения на dev):**
- `read_revit_material_by_remont(cur, remont_id_)` — resolve remont → client_request_id, DISTINCT ON material_id, фильтр `revit_file_type <> 'none'`, jsonb-массив `data`
- `task-02-sql-function/work/02_read_revit_material_by_remont.sql` + deploy-копия в `sql/revit-materials-sync/`
- `functions/read_revit_material_by_remont.md`, README `sql/revit-materials-sync/` + `sql/README.md`, CHECKLIST task-02

**Next:**
- task-02 deploy — CREATE FUNCTION на dev только по явному запросу пользователя; затем read-only проверка дедупа через MCP
- Рефакторинг task-03: пакет `revit/` (не в INSTALLED_APPS), URL `/revit/material/read/` вместо `/common/revit_material/read/`
- task-04 — классификация материалов + фиктивные ссылки (список `material_type_code` по группам нужно подтвердить с бизнесом/пользователем перед UPDATE)

**Task-03 (backend endpoint):**
- `common/ex_services/revit_material_services.py` — `read_revit_material_by_remont`, парсинг json `data`
- `common/ex_views/revit_material_views.py` — `RevitMaterialView.read`, валидация `remont_id` как `RevitEventView.read_status`
- `common/ex_urls/revit_material_urls.py` + `common/urls.py` (`revit_material/`)
- `sql/revit-materials-sync/README.md` — полная секция HTTP API; `sql/README.md` обновлён
- `agent-memory/revit-materials-sync/api/backend.md`

**Next:**
- task-04 — SQL `UPDATE material_tab` по `material_type_code` (surface/rfa/none + фиктивные URL/hash); без выполнения на dev без явного запроса
- QA: curl `GET /common/revit_material/read/?remont_id=` на dev после deploy backend + task-04 для ненулевого `data`

**QA эндпоинта (2026-07-01):**
- Найден тестовый ремонт по `client_request_tab.rowversion DESC` (в `remont_tab` такой колонки нет) —
  `remont_id=21838`, `client_request_id=3042029`
- Первая попытка `GET /revit/material/read/?remont_id=21838` — HTTP 400:
  `function public.read_revit_material_by_remont(integer) does not exist` — backend вызывал SP
  напрямую с одним `integer`-аргументом вместо `call_an_sp` (refcursor-паттерн, как у task-02)
- После фикса backend (сделан пользователем вне этого чата) — повторный запрос: HTTP 200, `status: true`,
  `data`: 2 материала (`material_id=1916` `BATH`/`rfa`/ванна, `material_id=4742` `SERVICE_FROM_CONTRACTOR`/`rfa`/потолок),
  `revit_file_url` — реальные подписанные MinIO-ссылки (не placeholder), `revit_file_hash`/`revit_asset_name` — `null`
- **Вывод:** task-01…03 backend полностью рабочие end-to-end, эндпоинт готов для интеграции в плагин

**Task-05 (план, плагин — список материалов, MVP):**
- Написана `task/task-05-plugin-materials-list/prompt.md` + `CHECKLIST.md`
- Скоуп: новая кнопка в `RemontHubWindow` → окно `RevitMaterialsWindow` со списком материалов
  из `/revit/material/read/`; без скачивания файлов/`LoadFamily` (это будущая task-06+, PLAN.md Фаза 3)
- `EPIC_TASKS.md` дополнен разделом «Фаза 2 — Плагин»
- Статус: **ждёт согласования с пользователем перед реализацией**

**Task-05 (реализация):**
- Реализовано через subagent (composer-2.5-fast): `DTO/RevitMaterialDtos.cs`,
  `Services/RevitMaterialsService.cs`, `Views/RevitMaterialsWindow.xaml`/`.xaml.cs`,
  кнопка «Материалы (Revit)» в `RemontHubWindow`, регистрация в `SBS.csproj`
- `dotnet build SBS.sln -c Release` — 0 ошибок
- UI-фикс после ручной проверки: убрана колонка `revit_file_url` из `DataGrid`
  (занимала слишком много места) — ширины остальных колонок перераспределены
- Не сделано (сознательно, вне скоупа task-05): скачивание файлов, `LoadFamily`/`ImportMaterial`,
  деплой в Revit Addins

**Task-06/07 (план, плагин — скачивание + импорт `.rfa`):**
- Написаны `task/task-06-plugin-download-cache/` и `task/task-07-plugin-load-family/`
  (`prompt.md` + `CHECKLIST.md`)
- **Важная находка QA**, зафиксирована в `decisions/DECISIONS.md` №11: `revit_file_url` на
  реальных данных — presigned MinIO URL с TTL ~12ч (`X-Amz-Expires=43199`), меняется при каждом
  запросе к API; `revit_file_hash` пока везде `NULL`. Наивный кэш «по URL/hash» не сработает —
  дизайн task-06 использует `material_id` как ключ кэша вместо URL/hash
- `decisions/DECISIONS.md` №12: импорт (task-07) — сначала только `revit_file_type = 'rfa'` через
  `LoadFamily()`, без расстановки экземпляров; `surface`-материалы — будущая таска
- `EPIC_TASKS.md` дополнен разделом «Фаза 3 — кнопка Синхронизировать»
- Статус: **обе таски ждут согласования с пользователем перед реализацией**

**Task-05 (плагин, реализация):**
- DTO: `SBS/DTO/RevitMaterialDtos.cs` — `RevitMaterialReadResponse`, `RevitMaterialRowDto` с `[JsonProperty]`
- URL: `SBS/Configs.cs` — `RevitMaterialReadUrl(remontId)` → `GET /revit/material/read/?remont_id=`
- Сервис: `SBS/Services/RevitMaterialsService.cs` — `ReadAsync(remontId)`, Bearer auth, 401 → «Сессия истекла…», пустой `data: []` не ошибка
- Окно: `SBS/Views/RevitMaterialsWindow.xaml` + `.xaml.cs` — DataGrid (material_id, material_name, material_type_code, revit_file_type, revit_asset_name, revit_file_url), состояния загрузка/пусто/ошибка + «Повторить», «Закрыть» с `DialogResult = true`
- Hub: `SBS/Views/RemontHubWindow.xaml` + `.xaml.cs` — кнопка «Материалы (Revit)» (`RevitMaterialsButton`, иконка `\uE7B8`)
- Проект: `SBS/SBS.csproj` — зарегистрированы DTO, сервис, окно (`Compile` + `Page`)
- Сборка: `dotnet build SBS.sln -c Release` — **успешно**, 0 ошибок, 0 предупреждений; деплой в Revit Addins **не выполнялся**
- Ручной тест в Revit (`remont_id=21838`) — не выполнен (требует интерактивного Revit)

**Task-06/07 (плагин — скачивание + импорт `.rfa`, реализация):**
- `SBS/Services/RevitMaterialsDownloadService.cs` — `SyncAsync`, `DownloadResult`; кэш
  `%LOCALAPPDATA%\SmartRemont\revit-materials-cache\`, манифест `cache_manifest.json` по
  `material_id`; пропуск при наличии файла в манифесте и на диске (без TTL); атомарная запись
  файлов и манифеста; HttpClient 60s; TODO по `revit_file_hash`
- `SBS/Services/RevitFamilyImportService.cs` — `LoadFamiliesIntoDocument`, одна транзакция
  «Smart Remont: импорт материалов Revit»; только `rfa` через `LoadFamily`; `surface` —
  «не поддерживается»; per-file try/catch
- `SBS/Views/RevitMaterialsWindow.xaml` + `.xaml.cs` — кнопка «Синхронизировать», столбец
  «Статус» (`INotifyPropertyChanged`), один клик: скачивание + импорт; итог
  «Загружено в проект: N · Скачано без импорта: K · Ошибок: M»
- `SBS/Views/RemontHubWindow.xaml.cs` — передача `_doc` в `RevitMaterialsWindow`
- `SBS/SBS.csproj` — зарегистрированы оба сервиса
- Сборка: `dotnet build SBS.sln -c Release` — **успешно**, 0 ошибок, 0 предупреждений;
  деплой в Revit Addins **не выполнялся**
- Ручной тест в Revit (`remont_id=21838`) — не выполнен (требует интерактивного Revit)
