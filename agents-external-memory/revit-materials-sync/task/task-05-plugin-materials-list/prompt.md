# Task 05 — Плагин: экран «Материалы (Revit sync)» — список (MVP)

Скопируй блок «Промпт для агента» агенту. **Только C# (Revit plugin, `SBS/`)**, без Python/SQL.

---

## Промпт для агента

```
Реализуй Task 05 фичи revit-materials-sync: новый экран плагина со списком материалов
ремонта, полученных из GET /revit/material/read/?remont_id={remont_id}.

Прочитай ВСЕ файлы из секции «Контекст» (в порядке таблицы).

MVP-скоуп: только чтение и отображение списка. Скачивание файлов, LoadFamily/ImportMaterial,
кэш по hash — НЕ в этой таске (будущая фаза, см. PLAN.md §5, Фаза 3).

Требования:

1. SBS/DTO/RevitMaterialDtos.cs — новый файл:
   - RevitMaterialReadResponse: Status (bool), Error (string), RemontId (int?),
     ClientRequestId (int?), Data (List<RevitMaterialRowDto>) — атрибуты [JsonProperty(...)]
     как в DTO/ClientMaterialDtos.cs
   - RevitMaterialRowDto: MaterialId (int?), MaterialName (string), MaterialTypeId (int?),
     MaterialTypeCode (string), RevitFileType (string), RevitFileUrl (string),
     RevitFileHash (string), RevitAssetName (string)

2. SBS/Configs.cs — добавить:
   public static string RevitMaterialReadUrl(int remontId) =>
       $"{ApiOriginUrl}/revit/material/read/?remont_id={remontId}";

3. SBS/Services/RevitMaterialsService.cs — новый статический сервис, паттерн как
   Services/RevitEventsService.cs (HttpClient static readonly, Bearer из
   ExportRoomsApplication.CurrentSession.AccessToken, TryReadErrorMessage на не-200):
   - Task<RevitMaterialReadResponse> ReadAsync(int remontId)
   - Бросать InvalidOperationException с понятным текстом на: нет авторизации, remontId <= 0,
     401 ("Сессия истекла..."), другие не-200, status:false в теле ответа
   - НЕ бросать исключение на пустой data: [] — это валидный ответ (ремонт без Revit-материалов)

4. SBS/Views/RevitMaterialsWindow.xaml + .xaml.cs — новое окно, паттерн/стиль как
   Views/RoomMaterialsWindow.xaml (Card/DataGrid стили, WindowLayoutHelper.UseFullWorkAreaHeight):
   - При загрузке (Loaded) дёргает RevitMaterialsService.ReadAsync(remontId) для
     ExportRoomsApplication.SelectedRemont.RemontId
   - DataGrid со столбцами: material_id, material_name, material_type_code, revit_file_type,
     revit_asset_name (— если null), revit_file_url (— если null, не обрезать вручную, WPF сам
     перенесёт/обрежет)
   - Состояния: загрузка (текст "Загрузка..."), пусто (понятное сообщение "Нет материалов для
     синхронизации в этом ремонте"), ошибка (текст ошибки + кнопка "Повторить")
   - Кнопка "Закрыть" — DialogResult = true (как в RemontHubWindow, чтобы не откатывать
     транзакции сессии Revit)

5. SBS/Views/RemontHubWindow.xaml + .xaml.cs — добавить новую кнопку в StackPanel ФУНКЦИИ:
   x:Name="RevitMaterialsButton", Content="Материалы (Revit)", Tag="default",
   Click="RevitMaterialsButton_Click"; в SetupFeatureButtons() — ConfigureFeatureButton с
   иконкой (выбери любой неиспользуемый Segoe MDL2 глиф, например "\uE7B8") и subtitle
   "Список материалов для синхронизации из SmartRemont"; в обработчике клика — открыть
   new RevitMaterialsWindow(remontId).ShowDialog() с Owner = this (без завязки на _doc,
   т.к. таска не трогает геометрию модели)

6. SBS/SBS.csproj (EnableDefaultItems=false) — добавить новые файлы:
   <Compile Include="DTO\RevitMaterialDtos.cs" />
   <Compile Include="Services\RevitMaterialsService.cs" />
   <Compile Include="Views\RevitMaterialsWindow.xaml.cs">
     <DependentUpon>RevitMaterialsWindow.xaml</DependentUpon>
   </Compile>
   + <Page Include="Views\RevitMaterialsWindow.xaml"> ... (сверить точный формат Page-записи
   для существующих окон в .csproj и повторить один в один)

7. Собрать `dotnet build SBS.sln -c Release` — без ошибок. НЕ деплоить в Revit Addins
   (-p:DeployToRevit=true) без явного запроса пользователя.

Обнови task-05-plugin-materials-list/CHECKLIST.md и work_log/WORK_LOG.md.
```

---

## Контекст

### Спецификация


| Файл                                                         | Зачем                                   |
| ------------------------------------------------------------ | --------------------------------------- |
| `agents-external-memory/revit-materials-sync/SPEC.md` §4     | Контракт ответа `/revit/material/read/` |
| `agents-external-memory/revit-materials-sync/api/backend.md` | Реальный (задеплоенный) эндпоинт, поля  |


### Эталон (паттерны в плагине)


| Файл                                              | Зачем                                                                          |
| ------------------------------------------------- | ------------------------------------------------------------------------------ |
| `SBS/Services/RevitEventsService.cs`              | Паттерн HTTP-сервиса: `HttpClient`, `Bearer`, обработка ошибок/401             |
| `SBS/Services/ClientMaterialTkService.cs`         | Паттерн снапшота с пустым состоянием (`HasData`, `EmptyMessage`)               |
| `SBS/DTO/ClientMaterialDtos.cs`                   | Стиль DTO с `[JsonProperty]`                                                   |
| `SBS/Views/RoomMaterialsWindow.xaml` / `.xaml.cs` | Стиль окна: `Card`, `DataGrid`, состояния загрузки/ошибки                      |
| `SBS/Views/RemontHubWindow.xaml` / `.xaml.cs`     | Как добавить новую кнопку-фичу (`ConfigureFeatureButton`, `FeatureCardButton`) |
| `SBS/Configs.cs`                                  | Как оформлен URL-билдер (`RevitEventStatusUrl`, `ClientMaterialTkReadUrl`)     |


### Реальные тестовые данные (проверено вручную через curl + MCP на dev, 2026-07-01)

- `remont_id=21838` (`client_request_id=3042029`) — 2 материала с `revit_file_type: "rfa"`
(`BATH`, `SERVICE_FROM_CONTRACTOR`), `revit_file_url` — реальные MinIO-ссылки,
`revit_file_hash`/`revit_asset_name` — пока `null`

### Правила


| Файл        | Зачем                                                                                  |
| ----------- | -------------------------------------------------------------------------------------- |
| `AGENTS.md` | Минимальный diff, новые файлы — в `.csproj`, XAML pack URI — `SmartRemont.ExportRooms` |


### Вне scope

- Скачивание `.rfa`/`.rvt` файлов, `LoadFamily`/`ImportMaterial` в проект (PLAN.md, Фаза 3) — **task-06+**
- Проверка хэша / локальный кэш (PLAN.md, Фаза 3)
- Изменение backend/SQL (уже готово, task-01…03)
- Деплой в Revit Addins без явного запроса

---

## Артефакты


| Создать/изменить | Путь                                                                             |
| ---------------- | -------------------------------------------------------------------------------- |
| DTO              | `SBS/DTO/RevitMaterialDtos.cs`                                                   |
| Service          | `SBS/Services/RevitMaterialsService.cs`                                          |
| Window           | `SBS/Views/RevitMaterialsWindow.xaml`, `.xaml.cs`                                |
| Изменить         | `SBS/Configs.cs`, `SBS/Views/RemontHubWindow.xaml`, `.xaml.cs`, `SBS/SBS.csproj` |


## DoD

- [ ] Новая кнопка «Материалы (Revit)» в `RemontHubWindow` открывает `RevitMaterialsWindow`
- [ ] Окно показывает таблицу материалов из `GET /revit/material/read/?remont_id=`
- [ ] Пустой список (`data: []`) — понятное сообщение, не пустой экран и не ошибка
- [ ] Ошибка сети/401/400 — текст ошибки + кнопка "Повторить", без падения плагина
- [ ] `dotnet build SBS.sln -c Release` — без ошибок
- [ ] Ручной тест на `remont_id=21838` — 2 строки материалов отображаются корректно (кириллица не битая)
- [ ] WORK_LOG обновлён