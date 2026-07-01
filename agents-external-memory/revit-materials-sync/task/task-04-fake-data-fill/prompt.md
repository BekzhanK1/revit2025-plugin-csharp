# Task 04 — Данные: классификация + фиктивные ссылки в `material_tab`

Скопируй блок «Промпт для агента» агенту. **Только SQL**, только `SELECT`/подготовка —
**никаких UPDATE на реальной БД без явного подтверждения пользователя в диалоге**.

---

## Промпт для агента

```
Реализуй Task 04 фичи revit-materials-sync: подготовь (но не выполняй) скрипт классификации
материалов и заполнения фиктивных revit_* полей.

Прочитай ВСЕ файлы из секции «Контекст» (в порядке таблицы).

Требования:
1. Сначала SELECT-превью: сгруппировать material_type_tab.material_type_code по трём корзинам
   (surface / rfa / none) — таблица как в SPEC.md §2. Вывести и показать пользователю ДО
   написания UPDATE, чтобы подтвердить список кодов (могут быть материалы вне списка — уточнить,
   что с ними: по умолчанию 'none').
2. SQL писать в **два места** (одинаковое содержимое):
   - `agent-memory/revit-materials-sync/task/task-04-fake-data-fill/work/03_fill_fake_revit_fields.sql`
   - `sql/revit-materials-sync/03_fill_fake_revit_fields.sql`
   - CTE/CASE по material_type_tab.material_type_code → revit_file_type
   - revit_file_url = 'https://placeholder.smartremont.kz/revit/' || revit_file_type || '/' ||
     material_type_code || '/' || material_id || '.' || (CASE revit_file_type WHEN 'rfa' THEN 'rfa'
     WHEN 'surface' THEN 'rvt' END)  -- только для NOT 'none'
   - revit_file_hash = md5(revit_file_url)
   - revit_asset_name = material_type_code || '_' || material_id
   - UPDATE только материалов с is_active = 1 (или актуальный флаг активности — сверить со
     столбцами material_tab)
   - Закомментированный DRY RUN (SELECT предпросмотра counts по revit_file_type) в начале файла,
     как в sql/buh/bih__send_lot_receipt_to_1c.sql
3. sql/revit-materials-sync/README.md — дополнить порядком применения (после 01 и 02)
4. НЕ выполнять UPDATE на dev/prod. Дождаться явного запроса пользователя в диалоге.

Обнови task-04-fake-data-fill/CHECKLIST.md и work_log/WORK_LOG.md.
```

---

## Контекст

### Спецификация

| Файл | Зачем |
|------|-------|
| `agent-memory/revit-materials-sync/SPEC.md` §2 | Таблица классификации по группам |
| `agent-memory/revit-materials-sync/decisions/DECISIONS.md` | №7 (код, не id), №8 (фиктивные, стабильные), №9 (только по запросу) |

### Эталон

| Файл | Зачем |
|------|-------|
| `sql/buh/bih__send_lot_receipt_to_1c.sql` | Паттерн закомментированного DRY RUN в файле |
| MCP `user-smart-remont-dev`: полный список `material_type_tab` | Проверить актуальный список кодов перед классификацией (в SPEC есть примеры, не исчерпывающий список) |

### Правила

| Файл | Зачем |
|------|-------|
| `.cursor/rules/no-autonomous-database-writes.mdc` | UPDATE — только после явного запроса пользователя |

### Вне scope

- Backend/frontend
- Реальные файлы (MinIO) — это фиктивные плейсхолдеры

---

## Артефакты

| Создать | Путь |
|---------|------|
| SQL (work) | `agent-memory/revit-materials-sync/task/task-04-fake-data-fill/work/03_fill_fake_revit_fields.sql` |
| SQL (deploy) | `sql/revit-materials-sync/03_fill_fake_revit_fields.sql` |

## DoD

- [ ] Полный список `material_type_code` по 3 группам сверен с реальными данными на dev (MCP SELECT)
- [ ] Скрипт с DRY RUN превью счётчиков по группам
- [ ] UPDATE идемпотентен (повторный запуск не меняет результат)
- [ ] README обновлён
- [ ] Список групп подтверждён пользователем ПЕРЕД выполнением UPDATE
- [ ] WORK_LOG обновлён
