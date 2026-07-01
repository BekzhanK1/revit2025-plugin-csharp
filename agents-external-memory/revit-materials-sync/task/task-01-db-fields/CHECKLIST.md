# Checklist — task-01-db-fields

- [x] Снят актуальный список колонок `material_tab` (MCP dev) перед ALTER — revit_* полей нет (51 колонка)
- [x] `agent-memory/.../task-01-db-fields/work/01_material_tab_revit_fields.sql`
- [x] `sql/revit-materials-sync/01_material_tab_revit_fields.sql` (deploy, синхронизирован с work/)
- [x] CHECK constraint на `revit_file_type`
- [x] CHECK `none` → url/hash/asset_name IS NULL (`material_tab_revit_none_fields_null_check`)
- [x] README в `sql/revit-materials-sync/`
- [x] `sql/README.md` обновлён
- [x] `agent-memory/revit-materials-sync/schemas/material_tab_revit_fields.md`
- [x] WORK_LOG обновлён
- [ ] Явное подтверждение пользователя получено ПЕРЕД выполнением ALTER на dev
