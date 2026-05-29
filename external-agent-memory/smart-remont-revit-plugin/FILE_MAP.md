# Карта файлов — где что лежит

## Корень репозитория `revit/`

| Путь | Назначение |
|------|------------|
| `README.md` | Главная документация, быстрый старт |
| `AGENTS.md` | Инструкции для AI-агентов |
| `SBS.sln` | Solution Visual Studio / `dotnet build` |
| `deploy/` | Пример `.addin` для Revit |
| `external-agent-memory/` | Память между сессиями агентов |

## Активный проект `SBS/`

> Папка называется `SBS`, сборка — **`SmartRemont.ExportRooms.dll`**.

| Путь | Назначение |
|------|------------|
| `SBS.csproj` | Единственный активный csproj |
| `app.config` | `apiOriginUrl` и binding redirects → `*.dll.config` |
| `ExportRoomsApplication.cs` | `IExternalApplication`: лента, dockable pane, глобальное состояние |
| `Configs.cs` | URL API из config |

### Commands/

| Файл | Назначение |
|------|------------|
| `BaseCommand.cs` | Базовый `IExternalCommand`, `EnsureAuthenticated()` |
| `ExportSmartRemontRoomsCommand.cs` | Точка входа: auth → home → export window |

### Services/

| Файл | Назначение |
|------|------------|
| `AuthService.cs` | Login / logout / restore session |
| `AuthStorage.cs` | `auth.session.json` на диске |
| `AuthGuard.cs` | Показ `AuthLoginWindow` при отсутствии сессии |
| `RemontService.cs` | **Мок** списка ремонтов для HomeWindow |

### Models/

| Файл | Назначение |
|------|------------|
| `AuthSession.cs` | Токены + user, `DisplayName` |
| `RemontOption.cs` | id + name выбранного ремонта |

### DTO/

| Файл | Назначение |
|------|------------|
| `AuthDtos.cs` | Login request/response |
| `SmartRemontRoomsExportDto.cs` | JSON экспорта помещений + `SmartRemontWorkItemDto` |
| `ScheduleMappingConfig.cs` | Маппинг колонок спецификаций (*.mapping.json) |

### Views/

| Файл | Назначение |
|------|------------|
| `AuthLoginWindow.xaml` | Модальный вход |
| `HomeWindow.xaml` | Приветствие + выбор ремонта |
| `ExportSmartRemontRoomsWindow.xaml` | **Основное** окно экспорта (фаза, комнаты, спецификации, JSON) |
| `AuthView.xaml` | UserControl для dockable pane |
| `ViewContainer.xaml` | Host для dockable pane |
### Resources/

| Файл | Назначение |
|------|------------|
| `unit.png` | Иконка кнопок ленты |

### Сборка

```
SBS/bin/Release/net8.0-windows/
  SmartRemont.ExportRooms.dll
  SmartRemont.ExportRooms.dll.config
  Newtonsoft.Json.dll
  Serilog.dll
  Serilog.Sinks.File.dll
  *.deps.json, *.runtimeconfig.json
```

### Установка в Revit

```
C:\ProgramData\Autodesk\Revit\Addins\2025\
  SmartRemont.ExportRooms.addin
  SmartRemont\
    SmartRemont.ExportRooms.dll
    (+ зависимости и dll.config)
```

Runtime-файлы плагина (рядом с DLL):

- `auth.session.json` — сохранённые токены
- `logs/{Month}/` — Serilog

## Удалено (май 2026)

Legacy очищен: `SBS/SBS/`, `packages/`, `SBS/packages/`, тестовые CSV в `schedules/`, неиспользуемые DTO и `ExportSettingsDialog`.
