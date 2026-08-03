# Task 01 — SQL: `read_revit_material_by_client_request`

Скопируй блок «Промпт для агента» агенту. **Только SQL**, без Python/C#.

---

## Промпт для агента

```
Реализуй Task 01 эпика client-request-primary: SQL-функцию материалов Revit по client_request_id.

Прочитай:
- agents-external-memory/client-request-primary/TASK.md §0, §1
- agents-external-memory/client-request-primary/decisions/DECISIONS.md (#1, #2)
- agents-external-memory/revit-materials-sync/functions/read_revit_material_by_remont.md
- эталон SP: public.read_revit_material_by_remont

Требования:
1. Создай public.read_revit_material_by_client_request(cur refcursor, client_request_id_ integer)
2. client_request_id_ IS NULL / ≤ 0 → RAISE EXCEPTION с понятным текстом
3. НЕ ходить через remont для выборки материалов — сразу client_material_tab WHERE client_request_id = …
4. remont_id в ответе: (SELECT remont_id FROM remont_tab WHERE client_request_id = client_request_id_ LIMIT 1) или NULL
5. Дедуп DISTINCT ON (material_id), фильтр revit_file_type <> 'none', JOIN material_type_tab — как у read_revit_material_by_remont
6. Поля data[] — тот же контракт (material_id, material_name, material_type_*, revit_*)
7. surfaces_file_url / surfaces_file_hash — если уже есть в старой SP, сохранить ту же логику на уровне CR
8. Старую read_revit_material_by_remont можно оставить: либо thin wrapper (resolve CR → вызвать новую), либо без изменений (parallel)
9. Положи SQL в sql/… + короткий md в agents-external-memory/client-request-primary/ или рядом с functions/

Не применяй DDL/CREATE на prod. На dev — только после явного подтверждения пользователя.
Обнови CHECKLIST.md.
```

---

## Контекст

| Файл | Зачем |
|------|-------|
| `client-request-primary/TASK.md` §1 | Контракт |
| `revit-materials-sync/functions/read_revit_material_by_remont.md` | Эталон логики |
| `sql/revit-materials-sync/02_read_revit_material_by_remont.sql` | Копия паттерна |

## DoD

- [ ] SP создана (файл в репо)
- [ ] CR с материалами → тот же `data[]`, что через remont этой заявки
- [ ] CR без remont → `remont_id: null`, `data` из ТК (не exception)
- [ ] CR без материалов / несуществующий → `data: []` (или принятый паттерн старой SP)
- [ ] CHECKLIST обновлён
