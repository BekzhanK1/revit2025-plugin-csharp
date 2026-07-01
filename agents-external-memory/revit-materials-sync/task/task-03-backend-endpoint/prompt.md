# Task 03 — Backend: `GET /revit/material/read/`

Скопируй блок «Промпт для агента» агенту. **Только Python (Django/DRF)**, без SQL/Vue.

---

## Промпт для агента

```
Реализуй Task 03 фичи revit-materials-sync: backend-эндпоинт для Revit-плагина.

Прочитай ВСЕ файлы из секции «Контекст» (в порядке таблицы).

Требования:
1. revit/ex_services/revit_material_services.py:
   def read_revit_material_by_remont(remont_id):
       res = call_an_sp("public.read_revit_material_by_remont", [remont_id])
       if len(res) > 0:
           return res[0]
       return {"remont_id": remont_id, "client_request_id": None, "data": []}
2. revit/ex_views/revit_material_views.py:
   class RevitMaterialView(ViewSet) — permission_classes = (IsAuthenticated,)
   метод read(self, request): валидация query-параметра remont_id (обязателен, integer, > 0) —
   скопировать стиль валидации из RevitEventView.read_status (common/ex_views/revit_event_views.py)
3. revit/ex_urls/material_urls.py:
   path('read/', RevitMaterialView.as_view({"get": "read"}))
4. revit/urls.py — path('material/', include('revit.ex_urls.material_urls'))
5. office_api/urls.py — path('revit/', include('revit.urls'))  (**не** добавлять в INSTALLED_APPS)
6. sql/revit-materials-sync/README.md — секция HTTP API (аналогично sql/client-material/README.md):
   запрос, ответ, ошибки, пример curl
6. sql/README.md — обновить блок фичи (API secion)

Обнови task-03-backend-endpoint/CHECKLIST.md и work_log/WORK_LOG.md.
Не запускай миграции/psql — SQL-функция read_revit_material_by_remont уже должна быть на dev
(зависимость от task-02); если её нет — сообщи и не продолжай молча.
```

---

## Контекст

### Спецификация

| Файл | Зачем |
|------|-------|
| `agent-memory/revit-materials-sync/SPEC.md` §4 | Контракт ответа, ошибки |

### Эталон

| Файл | Зачем |
|------|-------|
| `common/ex_views/revit_event_views.py` | Паттерн `ViewSet` + валидация query-параметров |
| `common/ex_services/revit_event_services.py` | Паттерн тонкого сервиса над `call_an_sp` |
| `common/ex_urls/revit_event_urls.py`, `common/urls.py` | Подключение роутов |

### Правила

| Файл | Зачем |
|------|-------|
| `.cursor/rules/backend.mdc` | Thin views, `call_an_sp`, `custom_response`, `exception_handler` |

### Вне scope

- Изменение SQL-функции (уже готова из task-02)
- Frontend/плагин C#

---

## Артефакты

| Создать | Путь |
|---------|------|
| Сервис | `revit/ex_services/revit_material_services.py` |
| View | `revit/ex_views/revit_material_views.py` |
| URLs | `revit/ex_urls/material_urls.py`, `revit/urls.py` |

## DoD

- [ ] `GET /revit/material/read/?remont_id=X` — 200, контракт из SPEC.md §4
- [ ] 400 без `remont_id` / не-integer / ≤ 0
- [ ] 401 без JWT
- [ ] Несуществующий `remont_id` → 200, `data: []`, `client_request_id: null`
- [ ] README (SQL-папка + `sql/README.md`) обновлены с HTTP-секцией
- [ ] WORK_LOG обновлён
