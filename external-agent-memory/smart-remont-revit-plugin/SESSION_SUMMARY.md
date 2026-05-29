# Smart Remont Revit Plugin — история работ (agent session)

Документ для onboarding следующего агента. Описывает, что было сделано в сессии разработки (май 2026).

## Цель проекта

Плагин **Autodesk Revit 2025** для компании Smart Remont: авторизация через API, выбор ремонта, экспорт помещений (комнат) и связанных данных в JSON для backend Smart Remont.

## Хронология изменений

### 1. Сборка и окружение

- Проект изначально ссылался на несуществующие пути к `RevitAPI.dll` (`C:\Users\Bekzhan-SmartRemont\Desktop\revitapi\`).
- Исправлено на: `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll` и `RevitAPIUI.dll`.
- Целевой framework: **.NET 8** (`net8.0-windows`), WPF + Revit API.
- Solution: `SBS.sln` → активный проект `SBS/SBS.csproj` (имя папки `SBS`, имя сборки другое — см. ниже).

### 2. Переименование сборки (замена старого плагина)

Пользователь хотел **перезаписывать** существующий плагин в:

`C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\`

| Было | Стало |
|------|--------|
| `SBS.dll` | `SmartRemont.ExportRooms.dll` |
| `SBS.AppTools` | `SmartRemont.ExportRooms.ExportRoomsApplication` |
| namespace `SBS.*` | `SmartRemont.ExportRooms.*` |

Манифест Revit **не менялся** — `SmartRemont.ExportRooms.addin` уже указывал на `ExportRoomsApplication`.

В `SBS.csproj` добавлен опциональный post-build deploy:

```bash
dotnet build SBS.sln -c Release -p:DeployToRevit=true
```

(требует закрытый Revit — DLL блокируется процессом)

### 3. Конфигурация API (origin URL)

- URL вынесен в `SBS/app.config` → при сборке `SmartRemont.ExportRooms.dll.config`.
- Ключ: **`apiOriginUrl`** (без завершающего `/`).
- Код: `Configs.ApiOriginUrl`, `Configs.AuthLoginUrl` → `{origin}/auth/revit/login/`.
- Дефолт: `https://office-testapi.smart-remont.kz`.

### 4. Авторизация

**API:** `POST {apiOriginUrl}/auth/revit/login/`

```json
{ "email": "...", "password": "..." }
```

**Ответ:** `token.access`, `token.refresh`, `user` (fio, email, employee_id, …).

**Файлы:**

- `DTO/AuthDtos.cs` — модели запроса/ответа
- `Models/AuthSession.cs` — сессия + `DisplayName` из `user.fio`
- `Services/AuthService.cs` — HTTP login, restore, logout
- `Services/AuthStorage.cs` — `auth.session.json` рядом с DLL
- `Views/AuthLoginWindow.xaml` — модальное окно входа
- `Services/AuthGuard.cs` — `EnsureAuthenticated()` перед командами

**Состояние в runtime:** `ExportRoomsApplication.CurrentSession`

### 5. UX-поток (цепочка окон)

Пользователь не видел логин в dockable pane — он был скрыт по умолчанию. Реализован явный поток:

```
Кнопка ленты «SmartRemont → помещения»
  → AuthLoginWindow (если нет сессии)
  → HomeWindow (приветствие + выбор ремонта)
  → ExportSmartRemontRoomsWindow (экспорт JSON)
```

**HomeWindow:**

- «Добро пожаловать, {Имя}»
- ComboBox «Выберите ремонт» — **мок** в `Services/RemontService.cs`
- Выбор → `ExportRoomsApplication.SelectedRemont`

### 6. Удалён экспорт спецификаций в CSV

- Удалены: `Commands/ExportAllSchedulesCommand.cs`, кнопка ленты «расписание».
- Удалён неиспользуемый `DTO/SmartRemontScheduleExportDto.cs`.
- Класс `SmartRemontWorkItemDto` перенесён в `DTO/SmartRemontRoomsExportDto.cs`.
- **Осталось:** блок «Спецификации и маппинг» внутри окна экспорта помещений (данные идут в JSON `WorkItems`, не отдельный CSV).

### 7. Dockable pane (вторичный UI)

- `Views/ViewContainer.xaml` + `AuthView` — боковая панель «Smart Remont».
- `VisibleByDefault = false` — основной UX через модальные окна.
- Можно использовать для профиля/выхода, но не обязательна для основного сценария.

### 8. Очистка legacy (май 2026)

Удалено из репозитория:

- `SBS/SBS/` — старый .NET 4.8 проект
- `packages/`, `SBS/packages/` — legacy NuGet
- `SBS/schedules/` — тестовые CSV
- `SBS/publish/`, `SBS/SBS.sln`, `packages.config`
- Неиспользуемые DTO: стены, RevitElement, diagnostics, TbaDto и др.
- `ExportSettingsDialog` — не вызывался
- `Properties/Settings.*` — пустые user settings
- Лишние ключи в `app.config` (оставлен только `apiOriginUrl`)

## Что НЕ делали / известные ограничения

- Список ремонтов с API — только мок (`RemontService.GetMockRemonts()`).
- Refresh token не используется для продления сессии.

## Связанные файлы в репозитории

- `deploy/SmartRemont.ExportRooms.addin` — образец манифеста
- `AGENTS.md` — правила для агентов
- `README.md` — входная точка для людей и агентов
