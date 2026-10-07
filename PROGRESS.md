# Прогресс

Список фич и веток — в [FEATURES.md](FEATURES.md). Здесь — что выкатывать и что делается сейчас. Новые записи сверху.

## Выкатка на бой: что в `dev`, но не в `master`

Проверено 07.10.2026 по `origin/master` и `origin/dev`.

### Backend (`myspace-backend`)

В `master` нет ручек, без которых плагин `dev1.0.1` не работает:

| Ручка / файл | Ветка | PR |
|---|---|---|
| `POST /revit/plugin/client-request/search/` | `revit-fixes-search` | #176, #177 |
| `GET /revit/project-template/check/` + админ-ручки шаблонов (`revit/ex_urls/project_template_urls.py`) | `revit-fixes-search` | #176, #177 |
| Откат замеров и ДС площади при обрыве записи (813386c) | `revit-fixes-search` | #177 |
| `POST /revit/plugin/ds/tk-change/apply/` (`revit_ds_tk_apply_views.py`, `revit_ds_tk_apply_services.py`) | `revit-ds-tk-apply-ondev` | #180 |

Итого `git diff origin/master origin/dev -- revit/`: 17 файлов, +1507 / −81.

Перед выкаткой накатить SQL из `myspace-backend/revit/sql/` (ветка `dev`):
- `revit_plugin__search_client_request.sql`
- `revit_project_template.sql`

### Frontend (`myspace-frontend`)

| Что | Ветка | PR |
|---|---|---|
| Грейд 8: ручной ввод площадей и замеров отключён | `feat/grade8-lock-manual-area-measures` | #347 |
| Экран шаблонов проекта, снятие импорта из Revit | `revit-fixes-search` | #341 |

### Плагин (`revit-plugin`)

`dev1.0.1` впереди `main` на 8 коммитов: шаблон проекта по грейду, атомарный ДС ТК, предупреждение установщика и т.д. Перед релизом:
- `SBS/app.config`: `useTestApi` → `false`;
- версия `dev1.0.1` → числовая (`1.0.1`) в `AssemblyInfo.cs` и `.iss`, иначе backend сравнит её как `0.0.1` (KNOWN_ISSUES №3);
- зарегистрировать версию в админке версий плагина.

### Порядок

1. SQL на боевую БД.
2. Backend: `revit-fixes-search`, `revit-ds-tk-apply-ondev` → `master`.
3. Frontend: `revit-fixes-search`, `feat/grade8-lock-manual-area-measures` → `master`.
4. Плагин: сборка с prod-конфигом, установщик, регистрация версии.

Ветки отходили от `dev` и содержат чужие коммиты. Влить их в `master` целиком нельзя: переносить нужно только свои коммиты (cherry-pick или отдельные `*-master` ветки, как делалось для `feature/revit-materials-upload-master`).

### F8 + F9 (метки ТК) — когда будет готово

1. SQL: `psql -f client_request/sql/read_client_material_flags.sql` (backend)
2. Backend: PR #198 → `master` (в `dev` — #197)
3. Frontend: PR #358 → `master` (в `dev` — #357)
4. Плагин: `feature/tk-material-flags` → `dev1.0.1` и в релиз вместе с ним

Плагин не падает, если ручки `material/flags/` ещё нет: метки просто не показываются.

## Журнал

### 07.10.2026 — F8 + F9: PR в MySpace

- Backend и frontend закоммичены и запушены. Ветки `feature/tk-material-flags` отходят от `master`, в `master` есть коммиты, которых нет в `dev` (Sync1C, CMS). Поэтому для `dev` отдельные ветки `feature/tk-material-flags-dev` от `origin/dev` с cherry-pick.
- PR: backend #197 (`dev`), #198 (`master`); frontend #357 (`dev`), #358 (`master`).
- SQL перенесён в backend: `client_request/sql/read_client_material_flags.sql`.
- Изменения только на чтение: новые ручки и SQL-функции, существующее поведение не меняется.
- Все 4 PR смёрджены. На тесте ручка `revit/plugin/material/flags/` отвечала 400: в `dev` в `revit_plugin_read_views.py` не было импорта `require_plugin_client_request_access` (в `master` он есть). Исправление — backend #199 (`fix/tk-material-flags-dev-import` → `dev`). `master` (#198) не затронут.
- Плагин теперь пишет в лог текст ошибки ручки меток, а не только код ответа.
- Тестовые заявки проектировщика Габдуллин Д. (employee 2219): 2988877, 2988849, 2573344, 3071538, 3123528, 2988957, контроль 2174212.

### 07.10.2026 — сборка под 2025 и 2027, адрес API для разработки

Ветка плагина `feature/tk-material-flags`, не закоммичено, не собиралось (нет Windows / dotnet).

- `build/RevitApi.props` подключён в оба проекта. API Revit 2025 берётся из установленного Revit, иначе из NuGet `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI` `2025.*` (только для компиляции, `ExcludeAssets=runtime`). Деплой `-p:DeployToRevit=true` идёт в `Addins\2025` и `Addins\2027`, годы задаются через `-p:RevitDeployYears=…`.
- `.addin` с относительным путём (`SmartRemont\SmartRemont.ExportRooms.dll`): один манифест подходит для любого года, установщик продолжает работать.
- Для сервера плагин всегда Revit 2025 (`PluginVersionCheckService.RevitYear`).
- API, удалённого в Revit 2026+ (`ElementId.IntegerValue`, `new ElementId(int)`), в коде нет. Остальные отличия 2027 проверяются только запуском.
- Адрес API: секретная панель в окне входа (5 кликов по чипу «Версия» за 3 с): поле, пресеты localhost:8000 / тест / прод, сброс. Порядок приоритета: панель → `apiOriginUrl` в `app.config` → `useTestApi`. При смене адреса сессия сбрасывается, чтобы токен не ушёл на другой сервер. Пока адрес задан, под шапкой окна видна жёлтая строка «API: … (задан вручную)».
- Сборка с адресом: `-p:ApiOriginUrl=http://localhost:8000` записывает его в `SmartRemont.ExportRooms.dll.config`. В `app.config` появился ключ `apiOriginUrl` с пустым значением, пустое игнорируется.
- Чтобы попасть в окно входа, когда сессия уже есть: «Выйти» в окне поиска заявки.

### 07.10.2026 — F8 + F9: код во всех частях

- Решения зафиксированы в [FEATURES.md](FEATURES.md#f8--f9-решения). По ходу правка: неактуальность проверяется во всех заявках, без учёта утверждения проекта.
- Ветки `feature/tk-material-flags`: backend и frontend от `master`, плагин от `dev1.0.1`. Ничего не закоммичено.
- Во frontend в рабочем дереве осталась твоя незакоммиченная правка `src/router/index.js` из `optimization/lazy-routes-build`, её не трогал.
- SQL проверен как обычный SELECT на `dev_prod` (3071538: 8 replaced, 2 work_set_removed; отказ по элементу набора — 2541583 / 15508). В БД функции не создавались.
- Backend: `py_compile` прошёл, на стенде не запускался. Frontend: синтаксис JS проверен `node --check`, сборка и линтер не запускались (нет `node_modules`). Плагин: не собирался (нет Windows / Revit).
- ТК в MySpace — это вкладка «Текстовый конструктор» (`textConstructor/`), а не `remontMaterials` (это смета).
- В `read_client_material` уже есть надпись «Есть неактивный материал» (`utils.get_client_material_not_active`, включает нулевую цену). Новые метки её не заменяют.

### 07.10.2026 — разбор F8 и F9, список веток к выкатке

- Изучены плагин, backend и frontend. Найдены и проверены проблемы плагина ([KNOWN_ISSUES.md](KNOWN_ISSUES.md)).
- F9 (наличие). Признак лежит не на материале, а на связке с поставщиком: `material_provider_tab.is_avail`. В легаси OFFICE это экран «Материалы поставщиков». Снять наличие у последнего поставщика нельзя, пока материал в активном подборе (`material_provider_is_avail_change_check`). Поэтому настоящее «нет в наличии» в ТК — 16 строк на 2439 заявок grade 8. Массово встречается «нет поставщика в городе»: city 6 — 51% строк ТК, city 2 — 1194 строки. Материалы подрядчика (`is_owned_material`) исключаются.
- F8 (неактуальность). Строка ТК сопоставляется с подбором по (room_id, work_set_id, material_id, material_set_id). Неактуальные строки есть у 2267 из 2439 заявок, нужен фильтр по стадии заявки. Истории подбора нет.
- Найдена ветка грейда 8: `feat/grade8-lock-manual-area-measures`, только frontend.
- Заведены FEATURES.md и PROGRESS.md.

## Следующие шаги

- [x] Ответы на вопросы по F8 и F9
- [x] F8/F9: SQL-функция статусов строк ТК
- [x] F8/F9: backend — ручка для ТК MySpace и для плагина
- [x] F8/F9: frontend ТК — метки, фильтры, баннер
- [x] F8/F9: плагин — бейдж в хабе, колонка в «Материалы Revit»
- [x] F8/F9: PR в backend и frontend (#197/#198, #357/#358)
- [ ] F8/F9: накатить SQL на тестовую БД и проверить ручки
- [ ] F8/F9: собрать frontend и посмотреть вкладку «Текстовый конструктор»
- [ ] F8/F9: собрать плагин в Windows и проверить хаб и «Материалы Revit»
- [ ] F8/F9 шаг 2: предупреждение в «ДС ТК» и в превью инициализации
- [ ] Кнопка «Заменить в ДС» в ТК MySpace
- [ ] Выкатка `dev` → `master` по списку выше
