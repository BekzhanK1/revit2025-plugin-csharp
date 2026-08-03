# Decisions — client-request-primary

## Принято

### #1 — Primary key = `client_request_id`

**Решение:** источник истины для привязки RVT и Revit API — `client_request_id`.  
`remont_id` — optional / nullable.

**Почему:** ТК (`client_material_tab`) живёт на CR; заявка появляется раньше ремонта (~680k CR без remont на dev).

---

### #2 — Backward compatible query/body

**Решение:** старые вызовы с `?remont_id=` / `"remont_id"` остаются на 1–2 релиза. Новый primary — `client_request_id`.

---

### #3 — URL материалов: только query-param

**Решение:** `GET /revit/material/read/?client_request_id=` на существующем path.  
Отдельный `read_by_cr/` **не** делать.

**Почему:** меньше surface area, проще плагину и docs.

---

### #4 — Events без remont: 409 на MVP (вариант B)

**Решение (P2-MVP):** API принимает `client_request_id`, внутри резолвит `remont_id`.  
Если remont нет → `409` / `{status:false, error:"Для заявки ещё нет ремонта"}`.  
Плагин дизейблит «Замеры» / «ДС» пока `remont_id == null`.

**Later (не в этом эпике):** вариант A — писать events по CR с nullable remont.

---

### #5 — `revit_event_log.client_request_id`: колонка сейчас, backfill later

**Решение:** в P2 добавить nullable колонку `client_request_id` + писать её при create.  
Массовый backfill старых строк — **later** (не блокирует MVP).

---

### #6 — Rights: текущий JWT Revit

**Решение:** не вводить отдельные `UserRightItemTab` на CR в этом эпике.  
Достаточно `IsAuthenticated` + текущих прав Revit-логина (как у `/revit/material/read/`).  
Если quick_search потребует право — оставить существующее `OA__RemontFormQuickSearch`.

---

## Отклонено / отложено

| Тема | Решение |
|------|---------|
| Отдельный path `read_by_cr/` | отклонено (#3) |
| Events write без remont | отложено (#4 later) |
| Backfill event_log | later (#5) |
| Новые rights на CR | отложено (#6) |
