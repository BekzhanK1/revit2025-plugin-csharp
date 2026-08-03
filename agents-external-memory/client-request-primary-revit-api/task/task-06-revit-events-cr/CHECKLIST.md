# Checklist — task-06-revit-events-cr

**Статус:** pending · **Prio:** P2 · **Decisions:** #4 B, #5

- [ ] create принимает `client_request_id`
- [ ] status принимает `client_request_id`
- [ ] без remont → 409
- [ ] legacy `remont_id` OK
- [ ] mismatch → 400
- [ ] DDL `client_request_id` nullable + write on create
- [ ] no mass backfill
- [ ] payload types unchanged
