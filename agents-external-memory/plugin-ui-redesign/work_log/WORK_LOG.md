# WORK_LOG — Plugin UI Redesign

| Дата | Task | Заметки |
|------|------|---------|
| 2026-07-01 | Epic created | SPEC, PLAN, EPIC_TASKS, checklists — по запросу продукта |
| 2026-07-02 | task-01-shared-ui | `WindowLayoutHelper`: WorkArea centering (Manual startup, deferred pass, `HomeDefaultWidth`/`HubDefaultWidth` 900/960). `AppStyles.xaml`: Card, PrimaryButton, SectionLabel + brush keys. SPEC §1.8 width target. Home/Hub XAML unchanged — merge AppStyles в task-02. |
| 2026-07-02 | task-02-home-search | HomeWindow: только remont_id, merged `AppStyles.xaml`, Width 900, placeholder + inline ProgressBar loader, клик по карточке → hub (без «Продолжить»/SelectedPanel), Enter для поиска. |
| 2026-07-02 | task-03-hub-header | RemontHubWindow: merged `AppStyles.xaml`, hero `Ремонт #` + `Заявка #` (30/24px), subtitle `RemontOption.Name`, info grid 2×2 (клиент/ЖК/квартира/пакет, uppercase labels), ID убраны из info block, padding Card 36/32 + margin 24. Меню без изменений (task-04). |
| 2026-07-02 | task-04-hub-menu | RemontHubWindow: переименованы 4 пункта меню, порядок RevitMaterials → DsAreaChange → Measures → RoomMaterials; скрыты MeasuresFromCode/MeasuresCompare/TypeParameters (`Visibility=Collapsed` + TODO); удалён stub `DsTkChangeButton`; обновлены subtitle constants. Header без изменений. |
| 2026-07-02 | task-05-wider-frame | HomeWindow: Width=`HomeDefaultWidth` (900), MinWidth=760, card margin 28 (task-02). RemontHubWindow: Width=`HubDefaultWidth` (960), MinWidth=880, card margin 24→28. `WindowLayoutHelper` без изменений — центрирование по WorkArea для 900/960 px. |
| 2026-07-02 | task-06-manual-qa | Static QA pass: Release build OK (0/0). SPEC code review — all Home/Hub requirements met; no leftover stub/radio/button refs in `SBS/`. Edge cases confirmed in code. Revit smoke + DeployToRevit pending manual run. |
| 2026-07-02 | task-07-docs | Epic docs closed: `USER_FLOW_AND_SCREENS.md` — Home (remont-only search, click-to-hub, loader, 900 px) + Hub (hero header, 4 visible menu items, 3 hidden with TODO, 960 px). `README.md` DoD checkboxes (6/7 code-complete). Epic status → код завершён; manual Revit smoke still pending. |
