# AGENTS.md — инструкции для AI-агентов

Репозиторий: **Smart Remont Revit Plugin** (экспорт помещений + авторизация).

## Быстрый onboarding

1. Прочитай [README.md](README.md) — сборка, установка, структура.
2. Прочитай [external-agent-memory/smart-remont-revit-plugin/SESSION_SUMMARY.md](external-agent-memory/smart-remont-revit-plugin/SESSION_SUMMARY.md) — что уже сделано.
3. Используй [external-agent-memory/smart-remont-revit-plugin/FILE_MAP.md](external-agent-memory/smart-remont-revit-plugin/FILE_MAP.md) — где лежит код.

## Что это за проект

- **Тип:** Revit add-in (DLL), не standalone app.
- **Revit:** 2025, x64.
- **Стек:** C# / .NET 8, WPF, Revit API, Newtonsoft.Json, Serilog.
- **Сборка:** `SmartRemont.ExportRooms.dll` (папка проекта — `SBS/`).

## Активный код vs legacy

| Работай здесь | Не трогай |
|---------------|-----------|
| `SBS/*.cs`, `SBS/Views/`, `SBS/SBS.csproj` | `SBS/SBS/` (старый .NET 4.8) |
| `SBS.sln` | `SBS/SBS.sln` |
| `deploy/*.addin` | `packages/`, `SBS/packages/` (legacy NuGet) |

## Архитектура (кратко)

```
ExportRoomsApplication (IExternalApplication)
  └── Ribbon: ExportSmartRemontRoomsCommand
        └── AuthGuard.EnsureAuthenticated() → AuthLoginWindow
        └── HomeWindow (ремонт, мок)
        └── ExportSmartRemontRoomsWindow → JSON file
```

**Глобальное состояние:** `ExportRoomsApplication.CurrentSession`, `SelectedRemont`, `_path`, `_logger`.

**Конфиг:** `SBS/app.config` → ключ `apiOriginUrl`.

## Правила для агентов

### Сборка

```bash
dotnet build SBS.sln -c Release
```

Деплой в Revit Addins (только если Revit **закрыт**):

```bash
dotnet build SBS.sln -c Release -p:DeployToRevit=true
```

Путь деплоя задан в `SBS.csproj`: `RevitAddinDeployDir` → `C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\`.

### Revit API

- `RevitAPI.dll` / `RevitAPIUI.dll`: `Private=false`, путь к установленному Revit 2025.
- Не коммить Revit DLL в репозиторий.
- Команды: `[Transaction(TransactionMode.Manual)]`, UI — WPF `ShowDialog()` на UI thread.

### Стиль изменений

- Минимальный diff, следовать существующим namespace `SmartRemont.ExportRooms.*`.
- Новые файлы добавлять в `SBS.csproj` (`EnableDefaultItems=false`).
- XAML: `x:Class` и pack URI используют имя сборки `SmartRemont.ExportRooms`.
- Не переименовывать сборку обратно в `SBS.dll` без явного запроса — сломается `.addin` у пользователей.

### Git

- Не коммитить без запроса пользователя.
- Не коммитить `auth.session.json`, логи, бинарники из `bin/`.

## Ключевые точки расширения

| Задача | Куда смотреть |
|--------|----------------|
| Новый API URL | `SBS/app.config`, `Configs.cs` |
| Логин / токены | `Services/AuthService.cs`, `AuthStorage.cs` |
| Список ремонтов | `Services/RemontService.cs` (сейчас мок) |
| Логика экспорта JSON | `Views/ExportSmartRemontRoomsWindow.xaml.cs` |
| Новая кнопка ленты | `ExportRoomsApplication.cs` + новый `IExternalCommand` |
| Маппинг спецификаций | `ScheduleMappingConfig.cs`, grid в Export window |

## Документация сессий

После значимых задач добавляй заметку в:

`external-agent-memory/<task-slug>/`

Шаблон: `SESSION_SUMMARY.md`, при необходимости `FILE_MAP.md`, `NEXT_STEPS.md`.

## Контакты с backend

- Test API: `https://office-testapi.smart-remont.kz`
- Login: `POST /auth/revit/login/`
- Production URL меняется через `apiOriginUrl` в config.
