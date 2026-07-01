# Task 06 — Плагин: кнопка «Синхронизировать» — скачивание файлов с локальным кэшем

Скопируй блок «Промпт для агента» агенту. **Только C# (Revit plugin, `SBS/`)**, без Python/SQL.

⚠️ **Перед реализацией — прочитай `decisions/DECISIONS.md` №11.** `revit_file_url` — подписанный
(presigned) MinIO URL с TTL ~12ч, **меняется при каждом запросе** к `/revit/material/read/`.
`revit_file_hash` пока везде `NULL`. Кэш **нельзя** ключевать по URL — см. стратегию ниже.

---

## Промпт для агента

```
Реализуй Task 06 фичи revit-materials-sync: кнопка «Синхронизировать» в RevitMaterialsWindow,
которая скачивает файлы материалов (revit_file_url) в локальный кэш на диске, с построчным
статусом и защитой от повторного скачивания уже закэшированного файла.

Прочитай ВСЕ файлы из секции «Контекст» (в порядке таблицы). Особое внимание —
decisions/DECISIONS.md №11 (presigned URL, hash пока NULL) — это определяет стратегию кэша ниже.

MVP-скоуп: ТОЛЬКО скачивание файлов на диск + кэш-манифест. Загрузка в Revit-документ
(LoadFamily) — task-07, НЕ делать в этой таске.

Требования:

1. Стратегия кэша (следствие decisions/DECISIONS.md №11):
   - Локальная папка кэша: %LOCALAPPDATA%\SmartRemont\revit-materials-cache\
     (использовать Environment.SpecialFolder.LocalApplicationData, аналогично тому, как
     AuthStorage.cs хранит auth.session.json — сверить реальный путь в Services/AuthStorage.cs
     и использовать тот же корень SmartRemont, если он там уже есть)
   - Ключ кэша — material_id (не URL, не revit_file_hash — он пока NULL везде). Имя файла на
     диске: "{material_id}_{revit_asset_name или material_id}.{ext по revit_file_type:
     rfa→rfa, surface→rvt}"
   - JSON-манифест кэша (cache_manifest.json в той же папке): material_id → { file_path,
     revit_file_hash (если был не-null на момент скачивания), downloaded_at }
   - Правило докачки (упрощённое по решению пользователя — реального хэша пока нет,
     полноценная инвалидация по хэшу будет добавлена отдельно позже, когда backend начнёт
     заполнять revit_file_hash): если материал уже есть в манифесте И файл физически
     существует на диске → пропустить (Skipped = true), НЕ перекачивать. Если материала нет
     в манифесте ИЛИ файла нет физически на диске → скачать. Никакого TTL/протухания —
     это временное упрощение, оставить TODO-комментарий в коде: "TODO: инвалидация по
     revit_file_hash, когда backend начнёт его заполнять (сейчас всегда NULL)".

2. SBS/Services/RevitMaterialsDownloadService.cs — новый сервис:
   - record/class DownloadResult { MaterialId, Success, Skipped (уже в кэше), FilePath,
     ErrorMessage }
   - Task<List<DownloadResult>> SyncAsync(IEnumerable<RevitMaterialRowDto> rows,
     IProgress<(int done, int total)> progress = null) — качает файлы по одному
     (HttpClient.GetByteArrayAsync(revit_file_url) или GetStreamAsync + File-поток для больших
     файлов), пишет во временный файл + атомарный File.Move в целевой путь (защита от
     повреждённого файла при обрыве соединения)
   - Пропускать строки с revit_file_url == null (defensive, хотя SPEC гарантирует url для
     revit_file_type <> 'none')
   - Timeout на файл — 60 секунд (файлы могут быть тяжелее JSON-ответов, отдельный HttpClient
     с большим таймаутом, не переиспользовать Timeout=30s из других сервисов)
   - Обновлять/перечитывать cache_manifest.json атомарно (написать во временный файл + Move,
     как для самих файлов материалов)

3. SBS/Views/RevitMaterialsWindow.xaml + .xaml.cs — добавить:
   - Кнопку "Синхронизировать" внизу окна (рядом с "Закрыть", слева от статус-текста)
   - Новый столбец в DataGrid "Статус" (или отдельная колонка справа): Ожидает / Скачивается /
     Готово (из кэша или скачано) / Ошибка — на VM-строке добавить свойство SyncStatusDisplay,
     обновляемое через INotifyPropertyChanged (VM сейчас — POCO с init-only свойствами,
     переделать RevitMaterialRowVm на класс с обычными сеттерами + INotifyPropertyChanged
     для этого столбца, не ломая остальные свойства)
   - При клике — задизейблить кнопку, вызвать RevitMaterialsDownloadService.SyncAsync с
     IProgress, обновляющим строки по мере скачивания, по завершении — StatusTextBlock:
     "Синхронизировано: N из M" (+ "Ошибок: K", если есть)
   - Ошибки по отдельным файлам — НЕ прерывают синхронизацию остальных (продолжить, собрать
     список ошибок, показать в конце)

4. SBS/SBS.csproj — зарегистрировать новый сервис.

5. Собрать `dotnet build SBS.sln -c Release` — без ошибок. НЕ деплоить в Revit Addins.

Обнови task-06-plugin-download-cache/CHECKLIST.md и work_log/WORK_LOG.md.
```

---

## Контекст

### Решения

| Файл | Зачем |
|------|-------|
| `decisions/DECISIONS.md` №11 | Presigned URL, hash=NULL — стратегия кэша по `material_id`, не по URL/hash |
| `decisions/DECISIONS.md` №8 | Изначальный замысел `revit_file_hash` (для будущего, когда backend начнёт его заполнять) |

### Эталон (паттерны в плагине)

| Файл | Зачем |
|------|-------|
| `SBS/Services/AuthStorage.cs` | Где и как плагин уже хранит файлы на диске (локальная папка, JSON-сериализация) |
| `SBS/Services/RevitMaterialsService.cs` | Уже реализованный сервис чтения списка (task-05) — источник `RevitMaterialRowDto` |
| `SBS/Views/RevitMaterialsWindow.xaml` / `.xaml.cs` | Текущее окно (task-05) — куда добавляется кнопка/колонка |

### Вне scope

- Загрузка скачанных `.rfa` в Revit-документ (`LoadFamily`) — **task-07**
- `surface`-материалы (текстуры/`.rvt`-библиотека) — импорт не определён, будущая таска (task-08+)
- Реальный `revit_file_hash` с backend — зависит от backend (не в этой таске)

---

## Артефакты

| Создать/изменить | Путь |
|---|---|
| Service | `SBS/Services/RevitMaterialsDownloadService.cs` |
| Изменить | `SBS/Views/RevitMaterialsWindow.xaml`, `.xaml.cs`, `SBS/SBS.csproj` |

## DoD

- [ ] Кнопка «Синхронизировать» скачивает файлы по `revit_file_url` в `%LOCALAPPDATA%\SmartRemont\revit-materials-cache\`
- [ ] Кэш ключуется по `material_id`, не по URL (presigned URL нестабилен — decisions №11)
- [ ] Повторный клик «Синхронизировать» без изменений в материалах — не перекачивает файлы (или перекачивает по TTL-политике, задокументированной в коде)
- [ ] Построчный статус в UI (Ожидает/Скачивается/Готово/Ошибка)
- [ ] Ошибка на одном файле не прерывает синхронизацию остальных
- [ ] `dotnet build SBS.sln -c Release` — без ошибок
- [ ] Ручной тест на `remont_id=21838` — оба файла скачиваются, повторный клик не перекачивает
- [ ] WORK_LOG обновлён
