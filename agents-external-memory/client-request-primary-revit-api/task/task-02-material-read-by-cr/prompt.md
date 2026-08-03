# Task 02 — Backend: `GET /revit/material/read/?client_request_id=`

Скопируй блок «Промпт для агента» агенту. **Python (Django/DRF)**, без нового URL path.

---

## Промпт для агента

```
Реализуй Task 02 эпика client-request-primary: расширить GET /revit/material/read/ под client_request_id.

Прочитай:
- agents-external-memory/client-request-primary/TASK.md §0, §1
- agents-external-memory/client-request-primary/decisions/DECISIONS.md (#2, #3, #6)
- agents-external-memory/revit-materials-sync/api/backend.md
- текущие revit/ex_views/revit_material_views.py, revit/ex_services/revit_material_services.py

Decision #3: НЕ создавать отдельный path read_by_cr/ — только query-param.

Требования:
1. Query: client_request_id и/или remont_id (integer > 0)
2. Если задан client_request_id → вызвать read_revit_material_by_client_request (task-01)
3. Если только remont_id → legacy (старая SP / resolve CR → новая)
4. Если оба → проверить что remont.client_request_id == client_request_id, иначе 400 mismatch
5. Если ни одного → 400 «Не указан client_request_id»
6. Response: status, client_request_id, remont_id (nullable), surfaces_*, data[]
7. Auth: IsAuthenticated (decision #6 — без новых rights)
8. Обновить sql/… README / HTTP-секцию с примерами обоих query-параметров

Зависимость: task-01 SP должна быть на окружении. Если нет — сообщи и не продолжай молча.
Обнови CHECKLIST.md.
```

---

## DoD

- [ ] `?client_request_id=X` → 200, контракт TASK.md
- [ ] `?remont_id=Y` → legacy работает
- [ ] оба + match → 200
- [ ] оба + mismatch → 400
- [ ] без параметров → 400
- [ ] CR без remont → `remont_id: null`, данные ТК
- [ ] 401 без JWT
- [ ] README обновлён
