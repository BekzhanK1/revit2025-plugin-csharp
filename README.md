# Smart Remont — Revit Plugin

Плагин для **Autodesk Revit 2025**: авторизация в Smart Remont, выбор ремонта, экспорт помещений в JSON.

## Документация

| Документ | Для кого | Содержание |
|----------|----------|------------|
| **[README.md](README.md)** (этот файл) | Все | Быстрый старт, структура репозитория |
| **[AGENTS.md](AGENTS.md)** | AI-агенты | Правила работы с кодом, архитектура, что не трогать |
| **[external-agent-memory/smart-remont-revit-plugin/](external-agent-memory/smart-remont-revit-plugin/)** | Агенты / новые разработчики | История сессии, карта файлов, next steps |

### Внутри `external-agent-memory/smart-remont-revit-plugin/`

- [SESSION_SUMMARY.md](external-agent-memory/smart-remont-revit-plugin/SESSION_SUMMARY.md) — что уже сделано
- [FILE_MAP.md](external-agent-memory/smart-remont-revit-plugin/FILE_MAP.md) — где что лежит в коде
- [NEXT_STEPS.md](external-agent-memory/smart-remont-revit-plugin/NEXT_STEPS.md) — идеи на будущее

---

## Требования

- Autodesk Revit **2025** (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Revit API: `C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll` (пути в `SBS/SBS.csproj`)

## Сборка

```bash
dotnet build SBS.sln -c Release
```

Результат:

```
SBS/bin/Release/net8.0-windows/SmartRemont.ExportRooms.dll
```

## Установка в Revit

Скопировать содержимое `SBS/bin/Release/net8.0-windows/` в:

```
C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\
```

Манифест (уже должен быть у пользователя):

```
C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont.ExportRooms.addin
```

Образец манифеста в репозитории: [deploy/SmartRemont.ExportRooms.addin](deploy/SmartRemont.ExportRooms.addin)

### Автодеплой (Revit закрыт)

```bash
dotnet build SBS.sln -c Release -p:DeployToRevit=true
```

## Настройка API

Файл `SmartRemont.ExportRooms.dll.config` (генерируется из [SBS/app.config](SBS/app.config)):

```xml
<add key="apiOriginUrl" value="https://office-testapi.smart-remont.kz" />
```

После смены URL — перезапуск Revit.

## Использование в Revit

1. Вкладка **Smart Remont** → кнопка **помещения**
2. Окно **входа** (если нет сохранённой сессии)
3. **Начальный экран** — «Добро пожаловать, …» + выбор ремонта
4. **Экспорт помещений** — фаза, фильтры, JSON

Сессия сохраняется в `auth.session.json` рядом с DLL.

## Структура репозитория

```
revit/
├── README.md                 ← вы здесь
├── AGENTS.md                 ← инструкции для AI-агентов
├── SBS.sln
├── deploy/
│   └── SmartRemont.ExportRooms.addin
├── external-agent-memory/
│   └── smart-remont-revit-plugin/
│       ├── SESSION_SUMMARY.md
│       ├── FILE_MAP.md
│       └── NEXT_STEPS.md
└── SBS/                      ← АКТИВНЫЙ проект (сборка SmartRemont.ExportRooms.dll)
    ├── SBS.csproj
    ├── app.config
    ├── ExportRoomsApplication.cs
    ├── Commands/
    ├── Services/
    ├── Models/
    ├── DTO/
    ├── Views/
    └── Resources/
```

Активный код только в `SBS/` (сборка `SmartRemont.ExportRooms.dll`). Legacy-папки удалены.

## Отладка

Visual Studio / Cursor → Debug → Program:

```
C:\Program Files\Autodesk\Revit 2025\Revit.exe
```

## Лицензия / контакты

Внутренний проект Smart Remont. Детали API — у backend-команды (`office-testapi.smart-remont.kz`).
