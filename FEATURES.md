# Фичи проекта «Revit-плагин Smart Remont»

Проект живёт в четырёх местах, и выкатывать его нужно согласованно:

| Часть | Репозиторий | Боевая ветка | Рабочая ветка |
|---|---|---|---|
| Плагин | `revit-plugin` | `main` | `dev1.0.1` |
| Backend | `../myspace/myspace-backend` | `master` | `dev` |
| Frontend MySpace | `../myspace/myspace-frontend` | `master` | `dev` |
| SQL (функции, DDL) | `../myspace/sql/<папка>`, `myspace-backend/revit/sql/` | накатывается вручную | — |

Статусы: **prod** — в `master` / `main`, **dev** — влито в `dev`, но не в `master`, **план** — ещё не начато.
Ход работ и чеклист выкатки — в [PROGRESS.md](PROGRESS.md).

## Сделано

| # | Фича | Плагин | Backend | Frontend | SQL | Статус |
|---|---|---|---|---|---|---|
| F1 | Версии плагина: реестр релизов, проверка версии при входе | `dev1.0.1` | `feat/revit-plugin-versioning` (#159) | `feat/revit-plugin-versioning` (#320) | `sql/revit-plugin-versions/` | prod |
| F2 | Материалы Revit: загрузка `.rfa` / `surface.rvt` на странице подбора | — | `feature/revit-materials-upload-master`, `feature/revit-materials-sync` | те же + `feature/revit-preset-materials-no-model-master` | `sql/revit-materials-sync/` | prod |
| F3 | Неймспейс `/revit/plugin/`: чтение материалов, ТК, замеров, ДС площади; запись замеров и ДС площади | да | `feature/revit-plugin-namespace` (#135) | — | `sql/client-request-primary-revit-api/`, `sql/revit-plugin-direct-apply/` | prod |
| F4 | Поиск заявки только грейда 8 и только своих; шаблоны проекта по грейду; запрет частичной отправки | `dev1.0.1` | `revit-fixes-search` (#176, #177) | `revit-fixes-search` (#341) — экран шаблонов проекта | `myspace-backend/revit/sql/revit_plugin__search_client_request.sql`, `revit_project_template.sql` | **dev** |
| F5 | ДС «Изменение ТК»: атомарная запись объёмов из Revit | `dev1.0.1` (6b8310a) | `revit-ds-tk-apply-ondev` (#180) | — | использует `tk_change_set_item_cnt` | **dev** |
| F6 | Грейд 8: площади и замеры только из Revit, ручной ввод в MySpace отключён, импорт из Revit в UI убран | — | — | `feat/grade8-lock-manual-area-measures` (#347) | — | **dev** |
| F7 | Новый UI плагина (дизайн-система) | `ui-redesign-design-system` → `main` (PR #1) | — | — | — | prod (`main`) |

### F6 подробнее (`feat/grade8-lock-manual-area-measures`)

Только frontend, коммиты `f5c886f` и `c3cf1d5`, влито в `dev` 30.09.2026 (PR #347):

- `remontMeasure/measure.vue`, `remontDS/dsCreationTemplates/roomChange.vue`: для грейда 8 ручной ввод заблокирован с подсказкой «Грейд 8: ручной ввод отключён», кнопка импорта из Revit удалена;
- `remontMarkStep`: скрыта галочка встречи на объекте;
- `services/index.js`: `isGrade8Restricted()`. Пока `grade_id` не пришёл из хедера, ограничение считается включённым;
- `header.vue`: грейд обновляется во вкладках.

Backend этот запрет не проверяет: запись замеров и ДС площади через API по-прежнему принимает ручные значения.

## В работе

| # | Фича | Плагин | Backend | Frontend | SQL | Статус |
|---|---|---|---|---|---|---|
| F8 | Неактуальные материалы в ТК: строка ТК не совпадает с текущим подбором заявки (заменён, конструктив убран, вариант выключен) | `feature/tk-material-flags` (от `dev1.0.1`) | `feature/tk-material-flags` (от `master`) | `feature/tk-material-flags` (от `master`) | `sql/tk-material-flags/` | код написан, не закоммичен, не проверен на стенде |
| F9 | Наличие материалов в ТК: снят с продажи / нет в наличии / нет поставщика в городе (справочник) и «поставщик отказал» (закуп) | та же | та же | та же | та же | то же |

### F8 + F9: решения

- Неактуальность проверяется во всех заявках с подбором, без учёта стадии и утверждения проекта.
- Выключенный подбор заявки тоже сравнивается.
- Строки, добавленные через ДС (`ds_wset_id > 0`), не подсвечиваются.
- В ТК MySpace только метка и материал из подбора, без кнопки замены. Кнопка «Заменить в ДС» — отдельная задача.
- «Конструктив убран из подбора» и «Нет поставщика в городе» — серые метки, остальные жёлтые или красные.
- Наличие: голова строки и элементы набора; материалы подрядчика (`is_owned_material`) не проверяются.
- Отказ поставщика — `provider_request_item_tab.is_avail = 2`, если по тому же материалу ремонта нет подтверждения.
- Плагин: бейдж на плитке «Материалы Revit» в хабе и колонка «Подбор / наличие» в окне «Материалы Revit». Окно «ДС ТК» и превью инициализации — второй шаг.

### F8 + F9: что сделано

| Часть | Файлы |
|---|---|
| SQL | `sql/tk-material-flags/01_read_client_material_flags.sql`: `client_material_avail_status()`, `read_client_material_flags()` |
| Backend | `client_request/ex_services/client_material_services.py` (`read_client_material_flags` + summary), `GET /client_request/<id>/client_material/tk/flags/` (`OA__RemontFormTabulation`), `GET /revit/plugin/material/flags/?client_request_id=` (`require_revit_materials_show` + доступ к заявке) |
| Frontend | `textConstructor/` (вкладка «Текстовый конструктор»): метки под материалом, «В подборе: …», баннер и фильтры «Все / Неактуальные / Наличие» |
| Плагин | `DTO/ClientMaterialFlagDtos.cs`, `Services/ClientMaterialFlagsService.cs`, `Configs.RevitMaterialFlagsUrl`, бейдж в `RemontHubWindow`, колонка в `RevitMaterialsWindow` |

Разбор F8 и F9 с данными и mock'ами: [ТК: наличие и актуальность](https://claude.ai/artifact/4CqmzsxKKJayoqiA3ZoSJ6).
