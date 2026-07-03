# DECISIONS — Project Init

## #1 — Источник истины: Extensible Storage

**Решение:** `remont_id` хранить в Extensible Storage на `ProjectInformation`, не в Project Info parameter.

**Почему:** Project Info редактируется пользователем; Storage скрыт от стандартного UI.

**Follow-up:** опционально дублировать в SR_REMONT_ID для visibility (backlog B-01).

---

## #2 — Имя файла = remont_id + ЖК

**Решение:** `{remont_id}_{SanitizedResidentName}.rvt`

**Fallback:** `{remont_id}_Remont.rvt` если ResidentName пуст.

**Папка v1:** `%USERPROFILE%\Documents\SmartRemont\Projects\`

---

## #3 — SaveAs, не Save in-place

**Решение:** init всегда создаёт **новый** файл; исходный шаблон не перезаписываем.

**Почему:** «полная инициация» = отдельный проект на ремонт.

---

## #4 — Конфликт remont_id

**Решение:** если Storage уже есть и remont_id ≠ выбранному → **блок** init + диалог.

**Перезапись файла на диске:** только с явным подтверждением пользователя.

---

## #5 — Worksharing v1

**Решение:** v1 поддерживает только обычные file-based RVT; при central — предупреждение «не поддерживается» или manual QA.

**Backlog:** B-04.

---

## #6 — Schema GUID

**Решение:** один GUID на весь lifecycle add-in; при смене schema — новый GUID + migration note (не автomigrate v1).

**GUID (task-01):** `171500a5-1d6b-4f5d-8253-e53b5a8275c3` — `ProjectRemontSchema.SchemaGuid` в `SBS/ProjectRemont/ProjectRemontSchema.cs`.

---

## #7 — Связь с «Синхронизировать материалы»

**Решение:** init вызывает тот же pipeline sync; вынести общий метод `RevitMaterialsSyncOrchestrator.SyncAll(doc, remontId)` чтобы не дублировать.

**Init ≠ Sync:** init добавляет SaveAs + stamp + Save.
