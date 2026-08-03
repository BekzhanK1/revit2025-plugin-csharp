# Task 04 — `client_material/tk/read` by CR

Скопируй блок «Промпт для агента» агенту.

---

## Промпт для агента

```
Реализуй Task 04 эпика client-request-primary: GET /common/client_material/tk/read/ по client_request_id.

Прочитай:
- agents-external-memory/client-request-primary/TASK.md §0, §5
- agents-external-memory/client-request-primary/decisions/DECISIONS.md (#2)
- текущий view/service tk/read (принимает remont_id)
- паттерн валидации из task-02 (material/read) — тот же контракт query params

Требования:
1. Принимать client_request_id и/или remont_id
2. Priority CR; оба → validate match; ни одного → 400
3. Данные ТК и так на client_request_id — убрать лишний hop через remont где возможно
4. Response: оба id (remont_id nullable) + прежний контракт ТК
5. Legacy ?remont_id= не ломать
6. Не ломать office UI, который ещё шлёт remont_id

Обнови CHECKLIST.md.
```

---

## DoD

- [ ] `?client_request_id=` работает
- [ ] `?remont_id=` legacy OK
- [ ] mismatch → 400
- [ ] CR без remont → данные ТК или пустой принятый ответ (не 500)
