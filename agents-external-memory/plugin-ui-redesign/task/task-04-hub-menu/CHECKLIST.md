# Checklist — task-04-hub-menu

## Rename (Content + subtitle constants)

- [x] `RevitMaterialsButton` → **Синхронизация материалов из Revit**
- [x] `DsAreaChangeButton` → **ДС на изменение квадратуры**
- [x] `MeasuresButton` → **Замеры комнат (из спецификаций)**
- [x] `RoomMaterialsButton` → **ДС на изменение ТК**

## Reorder StackPanel

1. [x] RevitMaterialsButton
2. [x] DsAreaChangeButton
3. [x] MeasuresButton
4. [x] RoomMaterialsButton

## Hide (Visibility=Collapsed + TODO)

- [x] `MeasuresFromCodeButton` — `// TODO: plugin-ui-redesign — Замеры по коду`
- [x] `MeasuresCompareButton` — `// TODO: plugin-ui-redesign — Сравнение замеров`
- [x] `TypeParametersButton` — `// TODO: plugin-ui-redesign — Изменение параметров типов`

## Remove

- [x] `DsTkChangeButton` — из XAML и code-behind (`SetupFeatureButtons`, click handler)

## Subtitles (.xaml.cs constants)

- [x] RevitMaterialsSubtitle — «Загрузка RFA и surface-типов из Smart Remont»
- [x] MeasuresSubtitle — «Отправка замеров из ведомостей Revit»
- [x] RoomMaterialsSubtitle — «ДС на изменение технологической карты» (уточнить с продуктом)

## Regression

- [x] Badge «Отправлено» на ДС и Замерах — работает
- [x] RevitMaterials открывается с remont_id

## Build

- [x] `dotnet build SBS.sln -c Release`
