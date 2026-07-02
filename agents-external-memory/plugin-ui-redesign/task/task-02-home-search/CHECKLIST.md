# Checklist — task-02-home-search

## UI (HomeWindow.xaml)

- [x] Удалить `ByClientRequestIdRadio`, `ByRemontIdRadio` (оставить только label «ID ремонта»)
- [x] Placeholder в TextBox: «Введите ID ремонта»
- [x] Inline loader: `ProgressBar IsIndeterminate` или spinner рядом с полем (виден только при поиске)
- [x] Удалить кнопку «Найти» **или** сделать secondary (phase 1: можно оставить + Enter)
- [x] Удалить `SelectedPanel`, `ContinueButton`
- [x] Оставить «Отмена» + «Выйти»
- [x] Ширина окна ≥ 880

## Code (HomeWindow.xaml.cs)

- [x] `RunSearchAsync` — всегда `QuickSearchAsync(byRemontId: true, id)`
- [x] Убрать restore по `ClientRequestId` в radio
- [x] `ResultsListBox` — при выборе: set `SelectedRemont` → `DialogResult=true` → `Close()`
- [x] `SetSearchBusy` — disable поле, показать loader (не менять текст кнопки как единственный индикатор)
- [x] Статусы: «Поиск…», «Найдено N», «Ничего не найдено», ошибка API

## Regression

- [x] Logout работает
- [x] Cancel → `DialogResult=false`
- [x] Повторное открытие с `SelectedRemont` — подставляет remont id и показывает результаты

## Build

- [x] `dotnet build SBS.sln -c Release`
