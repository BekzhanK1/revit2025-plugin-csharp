# Task 05 — `ds/room-change/read` by CR

Скопируй блок «Промпт для агента» агенту.

---

## Промпт для агента

```
Реализуй Task 05 эпика client-request-primary: GET /common/ds/room-change/read/ по client_request_id.

Прочитай:
- agents-external-memory/client-request-primary/TASK.md §0, §4
- agents-external-memory/client-request-primary/decisions/DECISIONS.md (#2)
- текущий ds room-change read (query remont_id)
- паттерн валидации task-02

Требования:
1. Принимать client_request_id и/или remont_id
2. Priority CR; оба → validate match; ни одного → 400
3. Response уже содержит оба id — сохранить; remont_id nullable если нет remont
4. Если для бизнес-логики DS нужен remont и его нет → пустой список rooms или понятная ошибка (не 500); задокументировать выбор
5. Legacy ?remont_id= не ломать

Обнови CHECKLIST.md.
```

---

## DoD

- [ ] `?client_request_id=` работает
- [ ] Legacy `?remont_id=` OK
- [ ] mismatch → 400
- [ ] Поведение без remont задокументировано
