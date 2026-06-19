# Type Parameter Change — session summary

## Что добавлено

- В хаб ремонта добавлена карточка **«Изменение параметров типов»**.
- Новое окно `TypeParameterChangeWindow` позволяет выбрать:
  - категорию;
  - семейство;
  - тип;
  - параметры выбранного типа.
- Изменения сохраняются через Revit `Transaction` в параметрах выбранного `ElementType`.

## Ключевые файлы

- `SBS/Models/TypeParameterModels.cs` — VM/options/result для UI и сохранения.
- `SBS/Services/TypeParameterChangeService.cs` — чтение model-категорий, семейств, типов, параметров и запись значений.
- `SBS/Views/TypeParameterChangeWindow.xaml` — UI выбора и таблица параметров.
- `SBS/Views/TypeParameterChangeWindow.xaml.cs` — каскадная загрузка ComboBox, сохранение и статус.
- `SBS/Views/RemontHubWindow.xaml(.cs)` — новая карточка в хабе.
- `SBS/SBS.csproj` — новые `Compile` и `Page`, так как `EnableDefaultItems=false`.

## Поведение

- Отображаются model-категории, у которых есть `ElementType`.
- Параметры с `IsReadOnly` или `StorageType.None` показываются, но не редактируются.
- Поддержана запись `String`, `Integer`, `Double` через `SetValueString` с fallback на invariant double, и `ElementId`.
- Кнопка **Сохранить** активна только если есть изменённые значения.

## Проверка

```bash
dotnet build SBS.sln -c Release
```

Результат: сборка успешна, предупреждений и ошибок нет.
