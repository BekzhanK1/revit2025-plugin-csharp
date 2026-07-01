# Checklist — task-03-backend-endpoint

- [x] `revit/ex_services/revit_material_services.py`
- [x] `revit/ex_views/revit_material_views.py` (валидация как `RevitEventView.read_status`)
- [x] `revit/ex_urls/material_urls.py` + `revit/urls.py` + `office_api/urls.py` (`revit/`, не в INSTALLED_APPS)
- [x] Убрано из `common/` (`revit_material_*`)
- [ ] 400 без `remont_id` / не-integer / ≤ 0
- [ ] 401 без JWT
- [ ] Ремонт не найден → 200, `data: []`
- [x] README (SQL-папка + `sql/README.md`) — секция HTTP API
- [x] `api/backend.md`
- [x] WORK_LOG обновлён
