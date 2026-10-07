# CLAUDE.md

Smart Remont Revit Plugin — add-in для Revit 2025 (C# / .NET 8, WPF, Revit API). Проектировщик находит заявку MySpace по `client_request_id`, инициализирует .rvt из шаблона, синхронизирует материалы и отправляет замеры / площади / объёмы ТК прямо в `myspace-backend`.

Полная документация (архитектура, схемы, каждый процесс, все endpoints): **[Smart Remont Revit Plugin — архитектура и процессы](https://claude.ai/code/artifact/0011947b-520b-4a37-83f3-57902f3f312f)**

Правила работы с репо — в [AGENTS.md](AGENTS.md); они действуют и для Claude.

Проект общий для плагина, `myspace-backend`, `myspace-frontend` и SQL. Фичи и их ветки во всех репо — [FEATURES.md](FEATURES.md), ход работ и чеклист выкатки на бой — [PROGRESS.md](PROGRESS.md). Обновляйте оба файла после каждой значимой задачи.

## Команды

```bash
dotnet build SBS.sln -c Release                          # сборка
dotnet build SBS.sln -c Release -p:DeployToRevit=true    # + копия в Addins\2025 и Addins\2027 (Revit закрыт)
dotnet build SBS.sln -c Release -p:DeployToRevit=true -p:ApiOriginUrl=http://localhost:8000   # + адрес API в .dll.config
powershell -ExecutionPolicy Bypass -File deploy\pack-installer.ps1   # установщик Inno Setup 6 → deploy/out/
```

Тестов нет. Плагин собирается против API Revit 2025 и для сервера всегда представляется как 2025 ([build/RevitApi.props](build/RevitApi.props)). Если Revit 2025 установлен, берутся его DLL; если нет, используются NuGet `Nice3point.Revit.Api.*` 2025. Готовая DLL раскладывается в годы из `RevitDeployYears` (по умолчанию `2025;2027`). Пути в `.addin` относительные. На Linux/WSL проект не собирается — проверяйте изменения чтением кода.

Адрес API без пересборки: в окне входа 5 кликов по чипу «Версия» открывают панель адреса API. Значение хранится в `%APPDATA%\SmartRemont\RevitPlugin\settings.json` и важнее `app.config`; сброс — там же.

## Структура

- `SBS/` → `SmartRemont.ExportRooms.dll` (основной add-in). Не переименовывать сборку — сломается `.addin` у пользователей.
- `SmartRemont.ExportSpecifications/` → отдельный add-in, выгрузка ведомостей в JSON, к API не ходит.
- `deploy/` — `.addin`, `.iss`, `pack-installer.ps1`. `deploy/payload/`, `deploy/out/` не коммитить.
- `agents-external-memory/` — системные описания (маппинги ведомостей, эпики). `external-agent-memory/` — заметки сессий.

Внутри `SBS/`: `Commands/` (одна кнопка), `Views/` (WPF, `AppStyles.xaml` — дизайн-система), `Services/` (вся логика), `DTO/` (контракты API, snake_case через `JsonProperty`), `Models/`, `ProjectRemont/` (ExtensibleStorage), `Configs.cs` (все URL).

`EnableDefaultItems=false`: каждый новый `.cs` / `.xaml` добавлять в `SBS.csproj` вручную.

## Поток

```
ExportRoomsApplication (лента, Serilog, восстановление сессии)
  └─ ExportSmartRemontRoomsCommand
       ├─ ProjectRemontBindingService.TryBindFromDocument  (ExtensibleStorage → SelectedRemont)
       ├─ AuthGuard → AuthLoginWindow (сначала проверка версии, потом логин)
       ├─ HomeWindow    — поиск заявки по ID
       └─ RemontHubWindow — init проекта / материалы / ДС площади / замеры / ДС ТК / параметры типов
```

Глобальное состояние — статические поля `ExportRoomsApplication`: `CurrentSession`, `SelectedRemont`, `CurrentUiApplication`, `_path`, `_logger`. Окна модальные (`ShowDialog()` на UI-потоке Revit).

Связь материала MySpace и элемента Revit — параметр `SR_ID` = `material_id`. Комнаты Revit ↔ комнаты планировки сопоставляются по базовому имени (`RoomNameMatcher.GetBaseName`).

## Backend

Код: `../myspace/myspace-backend` (Django + DRF + PostgreSQL SP). **Источник правды — ветка `dev`**: в других ветках может не быть `revit/plugin/client-request/search/`, `ds/tk-change/apply/`, `revit/project-template/`. Читать без checkout: `git -C ../myspace/myspace-backend show dev:revit/ex_urls/plugin_urls.py`.

| Плагин (`Configs.cs`) | Backend |
| --- | --- |
| `GET /revit/plugin/version/check/` | `revit/ex_views/revit_plugin_version_views.py` (AllowAny) |
| `POST /auth/revit/login/` | `authentication/views.py` `RevitSignInView` |
| `POST /revit/plugin/client-request/search/` | `revit/ex_views/revit_plugin_read_views.py` |
| `GET /revit/plugin/{material,tk,measures,ds/room-change}/read/` | `revit/ex_views/revit_plugin_read_views.py` |
| `POST /revit/plugin/{measures,ds/room-change}/apply/` | `revit/ex_views/revit_apply_views.py` → `revit/ex_services/revit_apply_services.py` |
| `POST /revit/plugin/ds/tk-change/apply/` | `revit/ex_views/revit_ds_tk_apply_views.py` → `revit_ds_tk_apply_services.py` |
| `GET /revit/project-template/check/` | `revit/ex_views/revit_project_template_views.py` |
| `/client_request/{id}/ds/{read,add,<ds_id>,<ds_id>/tk_material}/` | `client_request/ex_views/ds_views.py` |
| `GET /client_request/common/ds_types/read/`, `GET /common/work_sets/read/` | справочники |
| `POST /common/catalog/validate_material_ids/` | `common/ex_views/catalog_views.py` |

Формат ответа: успех `200 {"status": true, "error": null, ...}`; любая ошибка, включая отсутствие гранта, — `400 {"status": false, "error": "..."}` (не 403). Apply-ручки работают «всё или ничего» в `transaction.atomic()`.

Окружение: `SBS/app.config` → `useTestApi` (`true` = `office-testapi.smart-remont.kz`, `false` = `myspace-api.smartremont.kz`), опционально `apiOriginUrl`, `s3OriginUrl`. Читается один раз за сессию Revit.

## Известные ловушки

Полный список с файлами и вариантами исправления — [KNOWN_ISSUES.md](KNOWN_ISSUES.md).

- Refresh-токен сломан: плагин шлёт `/auth/token/refresh/`, backend слушает `/auth/api/token/refresh/`.
- Через `AuthApiClient` (retry на 401) ходят только `RevitMaterialsService` и `ProjectTemplateService`; остальные сервисы сами ставят Bearer в свой `HttpClient`. Новые вызовы делайте через `AuthApiClient.SendAsync`.
- `AssemblyInformationalVersion` = `dev1.0.1`; backend сравнивает версии как числа через точку (`dev1` → 0).
- В git закоммичены `SBS/bin/`, `SBS/obj/`, `SBS/.vs/` (legacy). Не добавляйте туда новые артефакты.
- `agents-external-memory/client-request-primary-revit-api/PLUGIN_API.md` частично устарел — сверяйтесь с `Configs.cs` и веткой `dev` бэкенда.
- Логи: `%LOCALAPPDATA%\SmartRemont\logs\yyyy-MM-dd\HH-mm.log`. Сессия: `auth.session.json` рядом с DLL (не коммитить).

## Git

Не коммитить без запроса. Не коммитить `auth.session.json`, `auth.credentials.json`, логи, бинарники. После значимых задач — заметка в `external-agent-memory/<task-slug>/SESSION_SUMMARY.md`.
