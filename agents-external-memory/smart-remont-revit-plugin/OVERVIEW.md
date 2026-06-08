# Smart Remont Revit Plugin — обзор

## Назначение

Add-in **Autodesk Revit 2025** (x64) для Smart Remont:

- авторизация в office API;
- поиск и выбор ремонта (заявки);
- **ДС по изменению площади** — отправка площадей помещений из модели;
- **замеры комнат** — отправка параметров из ведомостей Revit;
- (в коде есть, но **не в основном потоке**) полный экспорт помещений + WorkItems в JSON-файл.

## Технологии

| Параметр | Значение |
|----------|----------|
| Язык | C# |
| Framework | .NET 8 (`net8.0-windows`), WPF |
| Revit API | 2025, `Private=false` на RevitAPI.dll |
| Сборка | `SmartRemont.ExportRooms.dll` |
| Папка проекта | `SBS/` |
| Solution | `SBS.sln` |
| Namespace | `SmartRemont.ExportRooms.*` |

## Точка входа Revit

- `ExportRoomsApplication` — `IExternalApplication`: лента, логи Serilog, dockable pane, глобальное состояние.
- Кнопка ленты → `ExportSmartRemontRoomsCommand`.

## Глобальное состояние (runtime)

| Поле | Где | Назначение |
|------|-----|------------|
| `CurrentSession` | `ExportRoomsApplication` | JWT access token, user |
| `SelectedRemont` | `ExportRoomsApplication` | Выбранный ремонт после HomeWindow |
| `_logger` | Serilog | `logs/{Month}/` рядом с DLL |
| `_path` | Каталог DLL | `auth.session.json`, логи |

## Конфигурация API

Файл `SBS/app.config` → при сборке `SmartRemont.ExportRooms.dll.config`.

- Ключ: **`apiOriginUrl`** (без `/` в конце)
- Код: `Configs.ApiOriginUrl`
- Дефолт: `https://office-testapi.smart-remont.kz`

## Сборка и деплой

```bash
dotnet build SBS.sln -c Release
dotnet build SBS.sln -c Release -p:DeployToRevit=true   # Revit должен быть закрыт
```

Целевая папка деплоя (в csproj):  
`C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\`

## Архитектура (высокий уровень)

```
ExportRoomsApplication
├── Ribbon → ExportSmartRemontRoomsCommand
├── DockablePane (скрыт по умолчанию) → ViewContainer → AuthView
└── Глобально: CurrentSession, SelectedRemont

ExportSmartRemontRoomsCommand
  → AuthGuard / AuthLoginWindow
  → HomeWindow (поиск ремонта)
  → RemontHubWindow (хаб действий)
       ├── SelectedRemontSummaryWindow  → DS_AREA_CHANGE
       ├── RoomMeasurementsWindow       → MEASURES
       └── ДС по ТК — заглушка

[не в потоке команды]
  ExportSmartRemontRoomsWindow → JSON файл (rooms + workItems)
```

## Ключевые сервисы

| Сервис | Роль |
|--------|------|
| `AuthService` / `AuthStorage` | Login, restore, logout |
| `RemontService` | Quick search ремонтов |
| `RoomAreaService` | Площади Room для ДС |
| `RoomMeasurementsService` | Парсинг ведомостей для замеров |
| `RevitEventsService` | POST/GET revit_events |

Подробнее по экранам: [USER_FLOW_AND_SCREENS.md](USER_FLOW_AND_SCREENS.md).
