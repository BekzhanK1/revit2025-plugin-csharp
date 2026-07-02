# Epic: Plugin UI Redesign

**DoD эпика:** см. [README.md](README.md)

```
task-01 [Shared UI tokens] ──→ task-02 [HomeWindow]
                          └──→ task-05 [Window width]
task-03 [Hub header] ──→ task-04 [Hub menu]
task-06 [Manual QA]
task-07 [Docs update]
```

---

## task-01 — Shared UI tokens & window sizing

**Файлы:** `WindowLayoutHelper.cs`, опционально `Views/AppStyles.xaml`

**DoD:**
- [ ] Единые константы ширины: Home ~900, Hub ~960
- [ ] `WindowLayoutHelper` корректно центрирует широкие окна
- [ ] (Опционально) ResourceDictionary подключён в Home + Hub

**Checklist:** [task/task-01-shared-ui/CHECKLIST.md](task/task-01-shared-ui/CHECKLIST.md)

---

## task-02 — HomeWindow: поиск только по ремонту + click-through

**Файлы:** `HomeWindow.xaml`, `HomeWindow.xaml.cs`

**DoD:**
- [ ] Убраны radio и поиск по заявке
- [ ] Loader (не текст на кнопке)
- [ ] Клик по результату → hub (без «Продолжить»)
- [ ] «Отмена» / «Выйти» работают
- [ ] Enter → поиск

**Checklist:** [task/task-02-home-search/CHECKLIST.md](task/task-02-home-search/CHECKLIST.md)

---

## task-03 — RemontHubWindow: hero header

**Файлы:** `RemontHubWindow.xaml`, `RemontHubWindow.xaml.cs`

**DoD:**
- [ ] Крупно: `Ремонт #…` и `Заявка #…`
- [ ] Вторичный блок: клиент, ЖК, квартира, пакет
- [ ] Logo + визуальная иерархия

**Checklist:** [task/task-03-hub-header/CHECKLIST.md](task/task-03-hub-header/CHECKLIST.md)

---

## task-04 — RemontHubWindow: меню rename / reorder / hide

**Файлы:** `RemontHubWindow.xaml`, `RemontHubWindow.xaml.cs`

**DoD:**
- [ ] Переименования по SPEC
- [ ] Порядок: Sync materials → ДС квадратура → Замеры spec → ДС ТК
- [ ] Скрыты: Замеры по коду, Сравнение, Параметры типов
- [ ] Удалён stub `DsTkChangeButton`
- [ ] `// TODO: plugin-ui-redesign` у скрытых элементов

**Checklist:** [task/task-04-hub-menu/CHECKLIST.md](task/task-04-hub-menu/CHECKLIST.md)

---

## task-05 — Wider frame (глобально)

**Файлы:** `HomeWindow.xaml`, `RemontHubWindow.xaml`, `WindowLayoutHelper.cs`

**DoD:**
- [ ] Home: Width ≥ 880, MinWidth ≥ 760
- [ ] Hub: Width ≥ 960, MinWidth ≥ 880
- [ ] На 1920×1080 окно не обрезается (`MaxHeight` = WorkArea)

**Checklist:** [task/task-05-wider-frame/CHECKLIST.md](task/task-05-wider-frame/CHECKLIST.md)

---

## task-06 — Manual QA

**DoD:**
- [ ] Прогон чеклиста на remont `21642`
- [ ] Скриншоты до/после (опционально)
- [ ] Нет регрессии auth / logout

**Checklist:** [task/task-06-manual-qa/CHECKLIST.md](task/task-06-manual-qa/CHECKLIST.md)

---

## task-07 — Documentation

**Файлы:** `agents-external-memory/smart-remont-revit-plugin/USER_FLOW_AND_SCREENS.md`, WORK_LOG

**DoD:**
- [ ] USER_FLOW обновлён под новый UX
- [ ] WORK_LOG запись

**Checklist:** [task/task-07-docs/CHECKLIST.md](task/task-07-docs/CHECKLIST.md)
