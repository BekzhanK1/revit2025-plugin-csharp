# Установка Smart Remont — Revit Plugin

Пошаговая инструкция для **Autodesk Revit 2025** (Windows, x64).

Общее описание проекта: [README.md](README.md).

---

## 1. Требования

| Компонент | Версия / путь |
|-----------|----------------|
| Autodesk Revit | **2025**, 64-bit |
| Windows | 10 / 11 (x64) |
| .NET (для сборки из исходников) | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Revit API (только для разработки) | `C:\Program Files\Autodesk\Revit 2025\` |

Права на запись в:

- `C:\ProgramData\Autodesk\Revit\Addins\2025\`
- `C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\`

---

## 2. Структура после установки

```
C:\ProgramData\Autodesk\Revit\Addins\2025\
├── SmartRemont.ExportRooms.addin          ← манифест (обязателен)
└── SmartRemont\
    ├── SmartRemont.ExportRooms.dll        ← основная сборка
    ├── SmartRemont.ExportRooms.dll.config ← URL API
    ├── SmartRemont.ExportRooms.deps.json
    ├── SmartRemont.ExportRooms.runtimeconfig.json
    ├── Newtonsoft.Json.dll
    ├── Serilog.dll
    ├── Serilog.Sinks.File.dll
    ├── logs\                              ← создаётся плагином
    │   └── …
    └── Resources\                         ← иконки (см. раздел 5)
        ├── export_32.png
        ├── export_16.png   (необязательно)
        └── logo.png
```

Файл `auth.session.json` появится рядом с DLL после первого входа.

---

## 3. Сборка из репозитория

Клонируйте репозиторий и откройте терминал в корне проекта (`revit/`).

```powershell
dotnet build SBS.sln -c Release
```

Готовые файлы:

```
SBS\bin\Release\net8.0-windows\
```

> **Важно:** перед копированием или автодеплоем **закройте Revit** — иначе DLL может быть заблокирована.

---

## 4. Установка вручную

### Шаг 1 — Закрыть Revit

Полностью завершите Revit 2025 (включая фоновые процессы, если остались).

### Шаг 2 — Скопировать DLL и зависимости

Скопируйте **все** файлы из:

`SBS\bin\Release\net8.0-windows\`

в папку:

`C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\`

Создайте папку `SmartRemont`, если её нет.

Минимально нужны:

- `SmartRemont.ExportRooms.dll`
- `SmartRemont.ExportRooms.dll.config`
- `SmartRemont.ExportRooms.deps.json`
- `SmartRemont.ExportRooms.runtimeconfig.json`
- `Newtonsoft.Json.dll`, `Serilog.dll`, `Serilog.Sinks.File.dll`

Файл `.pdb` для работы плагина не обязателен (удобен только для отладки).

### Шаг 3 — Манифест `.addin`

Скопируйте образец из репозитория:

`deploy\SmartRemont.ExportRooms.addin`

в:

`C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont.ExportRooms.addin`

Проверьте путь внутри XML — он должен указывать на DLL:

```xml
<Assembly>C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\SmartRemont.ExportRooms.dll</Assembly>
```

Если плагин лежит в другой папке — измените `<Assembly>` соответственно.

### Шаг 4 — Ресурсы (иконки)

Создайте папку:

`C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\Resources\`

Положите туда:

| Файл | Назначение |
|------|------------|
| `export_32.png` | Иконка кнопки на ленте Revit |
| `logo.png` | Логотип на экранах входа и выбора функций |

Без этих файлов плагин работает, но кнопка и логотип могут не отображаться.

### Шаг 5 — Запуск Revit

1. Запустите Revit 2025.
2. На ленте должна появиться вкладка **Smart Remont** с кнопкой **SmartRemont**.
3. При первом запуске откроется окно входа.

---

## 5. Автоматическая установка (разработчикам)

Если Revit **закрыт**, из корня репозитория:

```powershell
dotnet build SBS.sln -c Release -p:DeployToRevit=true
```

Скрипт сборки копирует DLL и конфиги в:

`C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\`

(путь задан в `SBS\SBS.csproj` → `RevitAddinDeployDir`).

Манифест `.addin` и папку `Resources\` автодеплой **не копирует** — их нужно установить один раз вручную (разделы 3–4).

---

## 6. Настройка API

URL backend задаётся в:

`C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\SmartRemont.ExportRooms.dll.config`

Пример (тестовый стенд):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="apiOriginUrl" value="https://office-testapi.smart-remont.kz" />
  </appSettings>
</configuration>
```

- Указывайте **origin** без завершающего `/`.
- После изменения — **перезапустите Revit**.

Исходный шаблон в репозитории: [SBS/app.config](SBS/app.config).

---

## 7. Обновление плагина

1. Закройте Revit.
2. Соберите новую версию (`dotnet build …`) или скопируйте обновлённые файлы в `SmartRemont\`.
3. Замените `SmartRemont.ExportRooms.dll` и зависимости (при изменении NuGet-пакетов).
4. При смене URL — обновите `SmartRemont.ExportRooms.dll.config`.
5. Запустите Revit.

Манифест `.addin` менять не нужно, если путь к DLL не изменился.

---

## 8. Удаление

1. Закройте Revit.
2. Удалите файл `C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont.ExportRooms.addin`.
3. Удалите папку `C:\ProgramData\Autodesk\Revit\Addins\2025\SmartRemont\` (при необходимости сохраните `auth.session.json` и логи).

---

## 9. Устранение неполадок

### Плагин не появляется на ленте

- Revit **2025** (не 2024 / 2026).
- Файл `.addin` лежит в `Addins\2025\`, а не только внутри `SmartRemont\`.
- В `<Assembly>` в `.addin` — **полный путь** к существующему `SmartRemont.ExportRooms.dll`.
- Все зависимости (`Newtonsoft.Json.dll` и др.) рядом с основной DLL.
- Установлен [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).

### Ошибка при сборке / деплое

- Установлен Revit 2025 по пути  
  `C:\Program Files\Autodesk\Revit 2025\`
- Или поправьте пути к `RevitAPI.dll` / `RevitAPIUI.dll` в `SBS\SBS.csproj`.

### «Не удалось скопировать» при `DeployToRevit=true`

Revit держит DLL открытой — закройте Revit и повторите сборку.

### Нет иконки / логотипа

Проверьте наличие `Resources\export_32.png` и `Resources\logo.png` рядом с DLL.

### Ошибки входа / API

- Проверьте `apiOriginUrl` в `.dll.config`.
- Убедитесь в доступе к сети и корректности учётных данных Smart Remont.

Логи плагина: `SmartRemont\logs\` (рядом с DLL).

---

## 10. Отладка (для разработчиков)

Присоединение отладчика Visual Studio / Cursor к процессу Revit:

- Исполняемый файл: `C:\Program Files\Autodesk\Revit 2025\Revit.exe`
- Символы: `SmartRemont.ExportRooms.pdb` из папки сборки

---

## Контакты

Внутренний проект Smart Remont. Вопросы по API — у backend-команды.
