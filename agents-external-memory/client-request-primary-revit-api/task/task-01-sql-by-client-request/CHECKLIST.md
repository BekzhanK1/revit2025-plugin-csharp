# Checklist — task-01-sql-by-client-request

**Статус:** pending

- [ ] SQL-файл в репо
- [ ] Сигнатура `read_revit_material_by_client_request(cur, client_request_id_)`
- [ ] Материалы без hop через remont
- [ ] `remont_id` nullable в результате
- [ ] Дедуп + фильтр `revit_file_type <> 'none'`
- [ ] Проверка на dev (MCP/psql) после явного approve
- [ ] Docs / functions md
- [ ] WORK_LOG (если ведётся в эпике)
