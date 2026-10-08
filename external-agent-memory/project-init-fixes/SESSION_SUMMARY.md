# Инициализация проекта: исправления и оптимизация (F10)

07.10.2026, ветка `fix/project-init` (от `feature/tk-material-flags`). Не закоммичено, не собиралось — нет Windows / dotnet.

## Что сделано

| Проблема | Решение | Файлы |
|---|---|---|
| Старая заявка «залипает», поиск скрыт | Если открытый файл не инициализирован — `SelectedRemont = null` в начале команды | `Commands/ExportSmartRemontRoomsCommand.cs` |
| SaveAs затирает существующий `.rvt`, откат удаляет его | Переименование в `имя.backup-дата.rvt` перед SaveAs, возврат при ошибке | `ProjectInitService.cs`, `ProjectInitRollbackService.cs`, превью, хаб |
| После init документы закрываются без сохранения | Изменённые документы не закрываются, ExitRevit спрашивает сам | `ProjectPostInitExitService.cs` |
| Хаб закрывается крестиком во время init | `Closing` → `e.Cancel` при `_initInProgress`; проверка `IsVisible` перед превью | `RemontHubWindow.xaml.cs` |
| RFA открываются до 3 раз, `surfaces.rvt` 3 раза | Кэш результата SR_ID (путь + время + размер), кэш списка SR_ID библиотеки | `RevitFamilyImportService.cs`, `RevitSurfaceImportService.cs` |
| Окно «висит», «Отмена» не работает | `UiYield.ToUiAsync()` между семействами и в проверках; токен в `LoadFamiliesIntoDocumentAsync` | `UiYield.cs` (новый), оркестратор, preflight |
| Фоновое скачивание не останавливается | CTS в `StartBackgroundPreDownload`, `CancelBackgroundPreDownload()` при закрытии превью; `SaveManifest` в `finally` | оркестратор, `RevitMaterialsDownloadService.cs` |
| «Продолжить без исправления» всё равно падает | `InitSkipSrIdValidation.AbortBeforeImportOnErrors = false` | `RevitMaterialsSyncOptions.cs` |
| Откат транзакции surface → «загружено» | При ошибке `Commit` успешные копии помечаются ошибкой | `RevitSurfaceImportService.cs` |
| Привязка к чужой заявке при enrich | `FindBestMatch` — только точное совпадение | `ProjectRemontBindingService.cs` |
| Лишние материалы подбора в init | SQL отдаёт только ТК заявки (строки + элементы наборов). Первый вариант с полем `in_tk` и фильтром в плагине отброшен 08.10.2026 | `../myspace/sql/revit-project-init/01_read_revit_material_by_client_request_tk_only.sql` |

## Как проверить в Revit

1. Новая заявка без файла: init проходит, прогресс обновляется, «Отмена» во время импорта откатывает и удаляет новый файл.
2. Заявка с уже существующим `.rvt`: после init рядом лежит `*.backup-*.rvt`; при отмене файл возвращается на место.
3. Выбрать заявку, закрыть хаб, открыть плагин на другом неинициализированном файле — должен быть поиск, а не старая заявка.
4. Исходный документ с несохранёнными правками: после init Revit при выходе спрашивает, сохранить ли.
5. Превью на заявке с проблемным RFA: окно отзывчиво во время проверки, «Продолжить без исправления» грузит остальные.

## Не сделано

- KNOWN_ISSUES №10 (имя файла зависит от карточки), №11 (единичные тяжёлые вызовы Revit API).
- SQL не накатан ни на одну БД (доступ к dev_prod только на чтение).
