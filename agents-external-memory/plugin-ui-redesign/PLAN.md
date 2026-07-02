# PLAN — Plugin UI Redesign

## Фазы

```
Phase A — Shared layout & tokens     (task-01, task-05)
Phase B — HomeWindow                 (task-02)
Phase C — RemontHubWindow            (task-03, task-04)
Phase D — QA & docs                  (task-06, task-07)
```

## Phase A — Foundation (0.5d)

1. Зафиксировать ширину окон: Home 880–920, Hub 960
2. Обновить `WindowLayoutHelper` при необходимости (центрирование широких окон)
3. Вынести повторяющиеся стили (`Card`, `PrimaryButton`, `SectionLabel`) в один ResourceDictionary **или** скопировать единый набор в оба окна (минимальный diff — дублирование OK на первом этапе)

## Phase B — HomeWindow (1d)

1. Удалить radio «По ID заявки», `ByClientRequestIdRadio`, ветку в `RemontService.QuickSearchAsync`
2. UI: search bar + `ProgressBar`/`LoadingSpinner` overlay
3. `ListBox` → `MouseDoubleClick` или `PreviewMouseLeftButtonUp` → `DialogResult=true; Close()`
4. Убрать `SelectedPanel`, `ContinueButton`
5. Обновить `RunSearchAsync`: всегда `byRemontId: true`
6. Restore selection при повторном открытии — только remont id

## Phase C — RemontHubWindow (1–1.5d)

1. Новая шапка: hero `RemontId` + `ClientRequestId`
2. Info grid — compact secondary
3. Переименовать Content + subtitle константы в `.xaml.cs`
4. Reorder StackPanel кнопок
5. `Visibility=Collapsed` + TODO-комментарии для скрытых кнопок
6. Удалить `DsTkChangeButton` из XAML и code-behind

## Phase D — QA (0.5d)

1. Build Release
2. Ручной чеклист (см. task-06)
3. Обновить `USER_FLOW_AND_SCREENS.md`

## Риски

| Риск | Митигация |
|------|-----------|
| Случайный двойной клик закрывает окно | Debounce 300 ms или single click с highlight |
| `SelectedRemont` restore использовал client_request id | Только remont id в HomeWindow |
| Скрытые кнопки — мёртвый code-behind | Оставить handlers, скрыть XAML; TODO в EPIC |

## Оценка

**Итого:** ~3 рабочих дня (1 dev + UX polish)
