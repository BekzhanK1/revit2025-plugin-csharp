# Plugin UI Redesign — Smart Remont Revit

**Цель:** упростить и улучшить UX двух главных экранов плагина — «Добро пожаловать» (`HomeWindow`) и «Hub ремонта» (`RemontHubWindow`).

**Статус:** ✅ код завершён (task-01…05); ручной smoke в Revit — pending (task-06)

## Документы

| Файл | Содержание |
|------|------------|
| [SPEC.md](SPEC.md) | Требования от продукта (as-is → to-be) |
| [PLAN.md](PLAN.md) | Фазы, порядок работ, риски |
| [EPIC_TASKS.md](EPIC_TASKS.md) | Список task-01…07 с DoD |
| [decisions/DECISIONS.md](decisions/DECISIONS.md) | UX-решения |

## Затронутые файлы (ожидаемо)

- `SBS/Views/HomeWindow.xaml` + `.xaml.cs`
- `SBS/Views/RemontHubWindow.xaml` + `.xaml.cs`
- `SBS/Views/WindowLayoutHelper.cs` (ширина / центрирование)
- Опционально: общий `AppStyles.xaml` или `BrandAssets.cs`
- **Не трогаем** (только скрываем из hub): `RoomMeasurementsFromCodeWindow`, `RoomMeasurementsCompareWindow`, `TypeParameterChangeWindow`

## DoD эпика

- [x] Поиск только по ID ремонта; переход в hub кликом по карточке (без «Продолжить»)
- [x] Loader при поиске; понятные состояния empty / error / results
- [x] Hub: крупный заголовок «Ремонт» + «Заявка»; вторичная инфо ниже
- [x] Меню переименовано, упорядочено; 3 пункта скрыты с `TODO` в коде
- [x] Окна шире (~900–960 px), единый визуальный стиль
- [x] `dotnet build SBS.sln -c Release` — OK
- [ ] Ручной прогон flow: логин → поиск → hub → 2–3 функции
