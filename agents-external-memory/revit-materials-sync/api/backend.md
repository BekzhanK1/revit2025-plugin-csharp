# Backend API — revit-materials-sync

## Endpoint

`GET /revit/material/read/?remont_id={remont_id}`

- Auth: JWT (`IsAuthenticated`), без `UserRightItemTab`
- Контракт: `SPEC.md` §4

## Модуль

Пакет `revit/` — **не** в `INSTALLED_APPS`, только подключение в `office_api/urls.py` (как `it_support/`, `notifications/`).

## Сервис

`revit/ex_services/revit_material_services.py` → `read_revit_material_by_remont(remont_id)`

- SP: `public.read_revit_material_by_remont`
- Парсит `data` из json-строки, если нужно

## Файлы

| Файл | Назначение |
|------|------------|
| `revit/ex_services/revit_material_services.py` | Вызов SP |
| `revit/ex_views/revit_material_views.py` | `RevitMaterialView.read` |
| `revit/ex_urls/material_urls.py` | `read/` |
| `revit/urls.py` | `material/` include |
| `office_api/urls.py` | `revit/` include |

**Revit events** (замеры, payload) по-прежнему в `common/revit_events/` — отдельный legacy-модуль.
