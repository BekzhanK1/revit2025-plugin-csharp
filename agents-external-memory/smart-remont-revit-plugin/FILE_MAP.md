# Карта файлов

## Корень репозитория

| Путь | Назначение |
|------|------------|
| `README.md` | Документация для людей |
| `AGENTS.md` | Краткие правила для AI-агентов |
| `INSTALL.md` | Установка в Revit |
| `SBS.sln` | Solution |
| `deploy/` | Пример `.addin` |
| `external-agent-memory/` | История сессий (кратко) |
| **`agents-external-memory/`** | **Системная документация (этот набор)** |

---

## `SBS/` — активный проект

| Путь | Назначение |
|------|------------|
| `SBS.csproj` | Сборка `SmartRemont.ExportRooms.dll` |
| `app.config` | `apiOriginUrl` |
| `Configs.cs` | URL API |
| `ExportRoomsApplication.cs` | IExternalApplication, лента, состояние |
| `BrandAssets.cs` | Логотип, иконка ленты |

### Commands/

| Файл | Назначение |
|------|------------|
| `BaseCommand.cs` | doc/uiApp, `EnsureAuthenticated()` |
| `ExportSmartRemontRoomsCommand.cs` | Auth → Home → RemontHub |

### Services/

| Файл | Назначение |
|------|------------|
| `AuthService.cs` | Login, restore, logout |
| `AuthStorage.cs` | `auth.session.json` |
| `AuthGuard.cs` | Модальный логин |
| `RemontService.cs` | Quick search API |
| `RoomAreaService.cs` | Площади Room для ДС |
| `RoomMeasurementsService.cs` | Парсинг ведомостей (MEASURES) |
| `RoomMeasurementsScheduleMapping.cs` | Статический маппинг ведомостей |
| `TypeParameterChangeService.cs` | Категории/семейства/типы и запись type-параметров |
| `RoomNameMatcher.cs` | Базовые имена помещений |
| `RevitEventsService.cs` | create/status revit_events |
| `RevitEventStatusFormatter.cs` | Текст статуса в UI |

### Models/

| Файл | Назначение |
|------|------------|
| `AuthSession.cs` | Токены, DisplayName |
| `RemontOption.cs` | Выбранный ремонт |
| `RoomMeasurementsModels.cs` | Snapshot, Sources |

### DTO/

| Файл | Назначение |
|------|------------|
| `AuthDtos.cs` | Login |
| `QuickSearchDtos.cs` | Поиск ремонта |
| `RevitEventDtos.cs` | DS_AREA_CHANGE, MEASURES payloads |
| `RemontRoomsJsonDto.cs` | `RemontRoomAreaDto` для ДС |
| `SmartRemontRoomsExportDto.cs` | JSON экспорт rooms + workItems |
| `ScheduleMappingConfig.cs` | `*.mapping.json` |

### Views/

| Файл | Назначение |
|------|------------|
| `AuthLoginWindow` | Вход |
| `HomeWindow` | Поиск ремонта |
| `RemontHubWindow` | Хаб действий |
| `SelectedRemontSummaryWindow` | ДС площади |
| `RoomMeasurementsWindow` | Замеры |
| `TypeParameterChangeWindow` | Изменение параметров выбранного типа |
| `ExportSmartRemontRoomsWindow` | JSON экспорт (не в потоке команды) |
| `AuthView` / `ViewContainer` | Dockable pane |
| `AppMessageDialog`, `SuccessDialog` | Диалоги |
| `RevitEventStatusUi.cs` | Бейджи статуса |
| `WindowLayoutHelper.cs` | Размер окон |

### Resources

| Файл | Назначение |
|------|------------|
| `unit.png` | Иконка ленты |

---

## Runtime рядом с DLL

- `auth.session.json`
- `logs/{Month}/`
- `SmartRemont.ExportRooms.dll.config`

---

## Установка Revit

```
C:\ProgramData\Autodesk\Revit\Addins\2025\
  SmartRemont.ExportRooms.addin
  SmartRemont\
    SmartRemont.ExportRooms.dll (+ deps)
```
