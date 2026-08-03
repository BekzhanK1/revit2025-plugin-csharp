# Task 03 — Quick search: CR без remont

Скопируй блок «Промпт для агента» агенту. Backend / existing quick_search.

---

## Промпт для агента

```
Реализуй Task 03 эпика client-request-primary: quick_search должен отдавать заявку без remont.

Прочитай:
- agents-external-memory/client-request-primary/TASK.md §2
- agents-external-memory/client-request-primary/decisions/DECISIONS.md (#1, #6)
- endpoint POST /client_request/quick_search/
- плагин уже шлёт { client_request_id } или { remont_id } (SBS/Services/RemontService.cs)

Требования:
1. Поиск только по client_request_id возвращает карточку даже если remont_id IS NULL
2. Не отфильтровывать заявки без строки в remont_tab
3. data[]: client_request_id always; remont_id nullable
4. Сохранить поля UI: client_name, resident_name/prop_fio, flat_num, preset_name, статусы
5. Права: decision #6 — существующее OA__RemontFormQuickSearch (или текущее), без новых rights
6. Поиск по remont_id не ломать

Обнови CHECKLIST.md. Приложи пример request/response для CR без remont.
```

---

## DoD

- [ ] CR без remont находится по `client_request_id`
- [ ] `remont_id` в ответе `null` / отсутствует как nullable
- [ ] Поиск по `remont_id` работает как раньше
- [ ] Пример curl / JSON в docs или checklist
