# Checklist — task-02-sql-function

- [x] `agent-memory/.../task-02-sql-function/work/02_read_revit_material_by_remont.sql`
- [x] `sql/revit-materials-sync/02_read_revit_material_by_remont.sql` (deploy, синхронизирован с work/)
- [x] `DISTINCT ON (material_id)` + фильтр `revit_file_type <> 'none'`
- [x] JOIN `material_type_tab` для `material_type_code`
- [x] Ремонт не найден → `client_request_id: null`, `data: []`
- [ ] Дедуп по материалу проверен на тест-кейсе (2+ комнаты, один материал) — после deploy на dev
- [x] README в `sql/revit-materials-sync/`
- [x] `sql/README.md` обновлён
- [x] `functions/read_revit_material_by_remont.md`
- [x] WORK_LOG обновлён
- [ ] Явное подтверждение пользователя получено ПЕРЕД выполнением CREATE FUNCTION на dev
