# Task 07 — Плагин: загрузка скачанных `.rfa` семейств в проект Revit

Скопируй блок «Промпт для агента» агенту. **Только C# (Revit plugin, `SBS/`)**, без Python/SQL.

**Зависит от task-06** (файлы уже должны быть скачаны в локальный кэш).

---

## Промпт для агента

```
Реализуй Task 07 фичи revit-materials-sync: после скачивания (task-06) — загрузка `.rfa`
семейств из локального кэша в текущий документ Revit через Document.LoadFamily().

Прочитай ВСЕ файлы из секции «Контекст» (в порядке таблицы), особенно decisions/DECISIONS.md
№12 (скоуп только `rfa`, без авторасстановки экземпляров).

Скоуп ТОЛЬКО revit_file_type == "rfa". Для revit_file_type == "surface" — показывать в UI как
"Импорт материалов пока не поддержан" (не пытаться грузить, не падать), это будущая таска.

Требования:

1. RevitMaterialsWindow должен получить доступ к активному Document. Сейчас конструктор
   RevitMaterialsWindow(int remontId) не принимает Document — добавить перегрузку/параметр
   RevitMaterialsWindow(int remontId, Document doc), обновить вызов в
   RemontHubWindow.xaml.cs (RevitMaterialsButton_Click), т.к. RemontHubWindow уже хранит _doc.

2. SBS/Services/RevitFamilyImportService.cs — новый сервис:
   - Result LoadFamiliesIntoDocument(Document doc, IEnumerable<(int materialId, string
     filePath, string revitFileType)> items) — только Manual Transaction внутри ОДНОЙ
     транзакции на весь батч ("Smart Remont: импорт материалов Revit"), TransactionMode.Manual
     на уровне вызывающей команды уже установлен (сверить BaseCommand/ExportSmartRemontRoomsCommand)
   - Для каждого item с revitFileType == "rfa": doc.LoadFamily(filePath, out Family family);
     ловить исключения Autodesk.Revit.Exceptions.* по каждому файлу отдельно (try/catch внутри
     цикла, не прерывать весь батч на одном плохом файле)
   - Для revitFileType != "rfa" — пропустить с пометкой "не поддерживается", не пытаться грузить
   - Вернуть список результатов (materialId, success, familyName или null, errorMessage)
     для отображения в UI (переиспользовать/расширить DownloadResult из task-06 или новый тип
     ImportResult — на усмотрение агента, минимизировать дублирование полей)

3. SBS/Views/RevitMaterialsWindow.xaml + .xaml.cs — расширить кнопку "Синхронизировать" (или
   добавить вторую кнопку "Загрузить в проект", если это чище по UX — на усмотрение агента,
   но по умолчанию предпочтительно ОДНА кнопка "Синхронизировать", которая делает
   скачивание (task-06) + сразу загрузку rfa в документ, т.к. пользователь просил один клик):
   - После скачивания — вызвать RevitFamilyImportService.LoadFamiliesIntoDocument для всех
     успешно скачанных rfa-файлов
   - Обновить столбец статуса: Готово (загружено в проект) / Готово (скачано, surface — импорт
     не поддержан) / Ошибка загрузки (с текстом из ErrorMessage)
   - Итоговый StatusTextBlock: "Загружено в проект: N · Скачано без импорта: K · Ошибок: M"

4. SBS/SBS.csproj — зарегистрировать новый сервис.

5. Собрать `dotnet build SBS.sln -c Release` — без ошибок. НЕ деплоить в Revit Addins.

Обнови task-07-plugin-load-family/CHECKLIST.md и work_log/WORK_LOG.md.
```

---

## Контекст

### Решения

| Файл | Зачем |
|------|-------|
| `decisions/DECISIONS.md` №12 | Скоуп только `rfa`, без авторасстановки экземпляров семейства в модели |

### Эталон (паттерны в плагине)

| Файл | Зачем |
|------|-------|
| `SBS/Commands/ExportSmartRemontRoomsCommand.cs`, `SBS/Commands/BaseCommand.cs` | `[Transaction(TransactionMode.Manual)]`, где открывается `Document doc` |
| `SBS/Views/RemontHubWindow.xaml.cs` | Как `_doc` передаётся в дочерние окна (`new RoomMaterialsWindow(_doc)` и т.п.) |
| `SBS/Services/RevitMaterialsDownloadService.cs` (task-06) | Откуда брать скачанные файлы (пути из `DownloadResult`) |

### Вне scope

- Расстановка экземпляров семейства в модели (`FamilyInstance.Create` и т.п.) — не входит, только `LoadFamily`
- `surface`-материалы / `.rvt`-библиотека материалов — будущая таска (task-08+)
- Параметрические семейства с настройкой параметров под конкретный материал (PLAN.md, Фаза 4)

---

## Артефакты

| Создать/изменить | Путь |
|---|---|
| Service | `SBS/Services/RevitFamilyImportService.cs` |
| Изменить | `SBS/Views/RevitMaterialsWindow.xaml`, `.xaml.cs`, `SBS/Views/RemontHubWindow.xaml.cs`, `SBS/SBS.csproj` |

## DoD

- [ ] `RevitMaterialsWindow` получает `Document` от `RemontHubWindow`
- [ ] Кнопка «Синхронизировать» скачивает (task-06) и грузит `rfa`-файлы в проект одним кликом
- [ ] Ошибка `LoadFamily` на одном файле не прерывает импорт остальных
- [ ] `surface`-материалы явно помечены как неподдерживаемые, без падений
- [ ] Итоговая статистика в UI (загружено/скачано без импорта/ошибок)
- [ ] `dotnet build SBS.sln -c Release` — без ошибок
- [ ] Ручной тест в открытом проекте Revit на `remont_id=21838` — семейства появляются в Project Browser
- [ ] WORK_LOG обновлён
