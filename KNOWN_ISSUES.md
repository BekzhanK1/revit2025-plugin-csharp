# Известные проблемы

Найдено при разборе кода плагина и `myspace-backend` (ветка `dev`), 2026-10-06. Проверено чтением кода, в Revit не воспроизводилось.

ДС «Изменение ТК» и наборы (отдельный разбор): [docs/ds-tk-change-sets-audit.md](docs/ds-tk-change-sets-audit.md).

Архитектура и процессы: [Smart Remont Revit Plugin — архитектура и процессы](https://claude.ai/code/artifact/0011947b-520b-4a37-83f3-57902f3f312f).

| # | Проблема | Серьёзность |
|---|----------|-------------|
| 1 | Refresh токена идёт не на тот URL | Высокая |
| 2 | Большинство сервисов не обновляют токен при 401 | Высокая |
| 3 | Нечисловая версия плагина `dev1.0.1` | Высокая перед релизом |
| 4 | Пароль хранится в Base64 рядом с DLL и не в `.gitignore` | Средняя |
| 5 | `AuthGuard` не проверяет срок жизни токена | Низкая |
| 6 | В репо по умолчанию включён test API | Средняя перед релизом |
| 7 | В git лежат `bin/`, `obj/`, `.vs/`, включая Revit DLL | Низкая |
| 8 | Высота потолка не обновляется у существующей ДС площади | Низкая (известное MVP-ограничение) |
| 9 | `PLUGIN_API.md` устарел | Низкая |
| 10 | Имя файла проекта зависит от загрузки карточки заявки | Низкая |
| 11 | Тяжёлые вызовы Revit API при init остаются синхронными | Низкая |

---

## 1. Refresh токена идёт не на тот URL

- **Где:** [SBS/Configs.cs:121](SBS/Configs.cs#L121) — `AuthRefreshUrl => $"{ApiOriginUrl}/auth/token/refresh/"`.
- **Backend:** `authentication/urls.py` регистрирует маршрут `api/token/refresh/` под префиксом `auth/`, то есть `/auth/api/token/refresh/` (`CustomTokenRefreshView`).
- **Последствие:** refresh получает 404. Когда access-токен истекает, пользователь видит «Сессия истекла. Выйдите и войдите снова.».
- **Исправление:** заменить путь на `/auth/api/token/refresh/`. Ответ backend — `{access, refresh}`; парсер в `AuthService.TryRefreshTokenCoreAsync` его уже понимает.

## 2. Большинство сервисов не обновляют токен при 401

- **Где:** через `AuthApiClient.SendAsync` (refresh и повтор при 401) ходят только `RevitMaterialsService` и `ProjectTemplateService`. Остальные создают свой `HttpClient` и сами ставят Bearer:
  - [SBS/Services/RemontService.cs:37](SBS/Services/RemontService.cs#L37)
  - [SBS/Services/MeasuresService.cs:39](SBS/Services/MeasuresService.cs#L39), [:73](SBS/Services/MeasuresService.cs#L73), [:136](SBS/Services/MeasuresService.cs#L136)
  - [SBS/Services/DsRoomChangeService.cs:47](SBS/Services/DsRoomChangeService.cs#L47), [:81](SBS/Services/DsRoomChangeService.cs#L81), [:136](SBS/Services/DsRoomChangeService.cs#L136)
  - [SBS/Services/DsTkChangeService.cs:239](SBS/Services/DsTkChangeService.cs#L239) и ещё 5 мест в этом файле
  - [SBS/Services/ClientMaterialTkService.cs:39](SBS/Services/ClientMaterialTkService.cs#L39), [:313](SBS/Services/ClientMaterialTkService.cs#L313)
  - [SBS/Services/MaterialValidationService.cs:63](SBS/Services/MaterialValidationService.cs#L63)
- **Последствие:** даже после исправления п.1 поиск заявки, замеры, ДС площади и ДС ТК при истёкшем токене сразу требуют перелогина.
- **Исправление:** перевести эти вызовы на `AuthApiClient.SendAsync(() => new HttpRequestMessage(...))`. Фабрика нужна, чтобы запрос пересобирался при повторе.

## 3. Нечисловая версия плагина `dev1.0.1`

- **Где:** [SBS/Properties/AssemblyInfo.cs:36](SBS/Properties/AssemblyInfo.cs#L36) и [deploy/SmartRemont.ExportRooms.iss:2](deploy/SmartRemont.ExportRooms.iss#L2).
- **Backend:** `revit/ex_services/revit_plugin_version_services.py`, `compare_plugin_versions`: части версии через точку переводятся в `int`, нечисловая часть превращается в `0`. Поэтому `dev1.0.1` сравнивается как `0.0.1`.
- **Последствие:** если в админке задать минимальную поддерживаемую версию вида `1.0.0`, проверка вернёт `is_supported=false` и вход заблокируется у всех.
- **Исправление:** для релизов использовать чисто числовую версию (`1.0.1`). Держать её в одном месте и подставлять в `.iss` из `pack-installer.ps1`.

## 4. Пароль хранится в Base64 рядом с DLL и не в `.gitignore`

- **Где:** [SBS/Services/CredentialManager.cs:16](SBS/Services/CredentialManager.cs#L16), [:28](SBS/Services/CredentialManager.cs#L28). Файл `auth.credentials.json` пишется в папку DLL: `C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\`.
- **Последствие:** Base64 не шифрует. Файл в `ProgramData` читается любым пользователем компьютера. `auth.credentials.json` нет в `.gitignore`: при отладке из папки сборки его можно случайно закоммитить.
- **Исправление:** шифровать через DPAPI (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`) и хранить в `%APPDATA%\SmartRemont\RevitPlugin\`. Добавить `auth.credentials.json` в `.gitignore`. То же стоит сделать для `auth.session.json`: refresh-токен в нём тоже лежит открыто.

## 5. `AuthGuard` не проверяет срок жизни токена

- **Где:** [SBS/Services/AuthGuard.cs:13-15](SBS/Services/AuthGuard.cs#L13-L15): если файл сессии есть, окно входа пропускается.
- **Последствие:** при протухшей сессии пользователь проходит в HomeWindow и падает на первом запросе, а не видит окно входа сразу.
- **Исправление:** проверять `exp` из payload access-токена (код уже есть в `AuthService.EnsureNotExpired`). Если токен истёк, сначала пробовать refresh, а при неудаче показывать `AuthLoginWindow`.

## 6. В репо по умолчанию включён test API

- **Где:** [SBS/app.config:9](SBS/app.config#L9) — `useTestApi = true`.
- **Последствие:** установщик, собранный без правки конфига, направит проектировщиков на `office-testapi` и покажет им панель входа по чужому JWT.
- **Исправление:** держать `false` в репо или переключать окружение в `pack-installer.ps1` параметром.

## 7. В git лежат `bin/`, `obj/`, `.vs/`, включая Revit DLL

- **Где:** `git ls-files SBS/bin SBS/obj SBS/.vs` — 228 файлов: `RevitAPI.dll`, `RevitAPIUI.dll`, legacy `SBS.dll`, артефакты старого проекта «BI BIM Tools». Они попали в git до появления `.gitignore`, поэтому всё ещё отслеживаются.
- **Последствие:** нарушено правило AGENTS.md «не коммитить Revit DLL», репозиторий раздут, а устаревшие артефакты путают.
- **Исправление:** `git rm -r --cached SBS/bin SBS/obj SBS/.vs` отдельным коммитом.

## 8. Высота потолка не обновляется у существующей ДС площади

- **Где (backend):** `revit/ex_services/revit_apply_services.py`, `apply_ds_room_change_from_revit`: при существующей ДС шапка не трогается, `wall_height` игнорируется.
- **Последствие:** плагин показывает «Высота потолка: X м» в окне успеха, но в MySpace высота остаётся старой. Ответ backend содержит `wall_height_changed: false`, а плагин это поле не показывает.
- **Исправление:** как минимум показывать пользователю `wall_height_changed`. Полностью — доработка на backend.

## 9. `PLUGIN_API.md` устарел

- **Где:** [agents-external-memory/client-request-primary-revit-api/PLUGIN_API.md:87](agents-external-memory/client-request-primary-revit-api/PLUGIN_API.md#L87): поиск описан как `POST /client_request/quick_search/`, логин — как `{login, password}`.
- **Факт:** плагин использует `POST /revit/plugin/client-request/search/` (только назначенные на сотрудника заявки с `grade_id = 8`) и шлёт `{email, password}`.
- **Исправление:** обновить документ или пометить его как исторический со ссылкой на `Configs.cs`.

## 10. Имя файла проекта зависит от загрузки карточки заявки

- **Где:** [SBS/Views/RemontHubWindow.xaml.cs](SBS/Views/RemontHubWindow.xaml.cs), `InitProjectButton_Click` → `ProjectFileNamingService.BuildFullPath(client_request_id, remont_id, ResidentName, FlatNum)`.
- **Последствие:** если `quick_search` не ответил и `ResidentName` / `FlatNum` пустые, путь получается другим, и у одной заявки может появиться второй `.rvt` в соседней папке.
- **Исправление:** искать существующий файл по префиксу `{client_request_id}_{remont_id}` в `Documents\SmartRemont\Projects` или не давать init без загруженной карточки.

## 11. Тяжёлые вызовы Revit API при init остаются синхронными

- **Где:** `OpenDocumentFile`, `LoadFamily`, `NewProjectDocument`, импорт `surfaces.rvt` в одной транзакции.
- **Состояние:** с F10 (`fix/project-init`) между семействами окно перерисовывается и отмена срабатывает, повторные открытия RFA и `surfaces.rvt` убраны кэшем. Но один вызов (например, открытие большого `surfaces.rvt` или создание проекта из шаблона) по-прежнему блокирует Revit на время своей работы — это ограничение Revit API.
