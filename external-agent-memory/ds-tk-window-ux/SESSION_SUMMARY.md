# ДС ТК — UX окна, этап 1 (08.10.2026)

Окно: `SBS/Views/DsTkChangeWindow.xaml(.cs)`. Расчёт: `DsTkTargetService` (что уйдёт / заблокировано), `DsTkCompareService` (состав + `ApplyTarget`).

## Диагноз

На экране смешаны две модели: сверка состава (на отправку не влияет, правится в MySpace) и расчёт объёмов (решает, активна ли кнопка). Блокировки показывались сплошным текстом, одна причина (строка ведомости без помещения) повторялась по каждой позиции, карточка «к отправке» считала строки, подвал — позиции.

## Сделано (этап 1)

- Плашка предупреждений: `WarningGroupsItemsControl`, группы `WarningGroupVm` / `WarningItemVm`, `MaxHeight=200`.
- Группировка `Blocked`/`Skipped` по `Reason`; `Unassigned` скрываются, если их ведомость (`SourceCode`) уже заблокировала позиции (`BlockedBySourceCode`).
- `ResolveSendBlockReason(brief)` — короткая причина в подвале (`SendBlockText`).
- `DsTkCompareRow.NeedsAttention` для фильтра «Только проблемы».
- Меню «⋯» (`MoreButton` + ContextMenu): сканирование, JSON отправки, экспорт JSON.
- Токены `Extra*`, `Blocked*` в `AppStyles.xaml`; `SetBadgeColors` берёт ключи ресурсов.

## Доработка: вкладки

- Плашка заменена вкладками `FixTab` / `SendTab` / `TableTab` (RadioButton `SegmentTab`), панели `FixPanel` / `SendPanel` / `TablePanel`, переключение в `ApplyTab`, автовыбор в `SelectDefaultTab` (флаги `_tabChosenByUser`, `_settingTab`).
- «Уйдёт в ДС» — `SendGrid` из `_target.ToSend[].ChangedLines` (`BindSendGrid`).
- Группировка блокировок по `(Reason, ReasonDetail)`; `WarningItemVm.Lines` — строки подробностей.

## Дальше

Этапы 2–3 — в `PROGRESS.md` (раздел «F11: окно «ДС ТК», UX этап 1»).
