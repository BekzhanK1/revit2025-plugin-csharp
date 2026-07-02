# Checklist — task-03-hub-header

## Layout (RemontHubWindow.xaml)

- [x] Hero row: `Ремонт #21642` — FontSize 28–32, SemiBold, `#111827`
- [x] Hero row: `Заявка #2995745` — FontSize 22–26, `#1B6FC8` или рядом badge
- [x] Subtitle / name remont (если есть в `RemontOption.Name`) — 14px muted
- [x] Info block ниже: клиент, ЖК, квартира, пакет — grid 2 col, labels 11px uppercase muted
- [x] Убрать дублирование: ID не повторять мелко в info grid (или оставить только в hero)

## Code (RemontHubWindow.xaml.cs)

- [x] `BindRemontInfo` заполняет новые TextBlock'и
- [x] `BuildSubtitle` — только вторичные поля (без remont/client request id)

## Visual

- [x] Logo компании (если `BrandAssets`) — слева от hero или над ним
- [x] Достаточный padding (32–40)

## Build

- [x] `dotnet build SBS.sln -c Release`
