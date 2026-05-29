# Рекомендуемые следующие шаги

Для следующего агента или разработчика.

## Высокий приоритет

1. **API списка ремонтов** — заменить мок в `RemontService` на реальный endpoint; передавать `SelectedRemont.Id` в экспорт/API.
2. **Проброс ремонта в JSON** — добавить в `SmartRemontRoomsExportDto` поля `remontId` / `remontName` из `ExportRoomsApplication.SelectedRemont`.
3. **Refresh token** — обновление `access` при 401, если backend поддерживает.

## Средний приоритет

4. Убрать или доработать dockable pane (`AuthView`) — сейчас дублирует логику модального входа.
5. Почистить неиспользуемые DTO (`WallInfoDto`, `TbaDto`, …) если не нужны для экспорта.
6. Удалить legacy `SBS/SBS/` и корневой `packages/` из репо или вынести в archive-ветку.

## Низкий приоритет

7. UI: показывать выбранный ремонт в заголовке `ExportSmartRemontRoomsWindow`.
8. Автотесты для `AuthService` (mock HTTP) — опционально.

## Проверка после изменений

```bash
dotnet build SBS.sln -c Release
```

Деплой (Revit закрыт):

```bash
dotnet build SBS.sln -c Release -p:DeployToRevit=true
```

Ручная проверка в Revit: вкладка **Smart Remont** → **помещения** → login → home → export.
