# UI design system v2 — SESSION SUMMARY

**Задача:** переделать UI/UX всех окон плагина (современно и удобно), функционал не трогать.

## Что сделано

- `SBS/Views/AppStyles.xaml` — новая дизайн-система:
  - токены цветов (бренд — красный из логотипа; акцент меняется тремя `Color` вверху файла),
    семантические кисти Info/Success/Warning/Error;
  - неявные стили для Button, TextBox, PasswordBox, ComboBox(+Item), CheckBox, ScrollBar,
    ProgressBar (в т.ч. indeterminate), ToolTip, ListBoxItem, Expander, TabControl/TabItem,
    DataGrid (скруглённый шаблон, заголовки с grippers — ресайз колонок снова работает);
  - layout-стили: `PageHeader`, `PageFooter`, `HeaderIconTile`, `ModernCard`, `ContentCard`,
    `InsetPanel`, `Chip*`, `Callout*`, `StatBadge`, кнопки `Ghost`/`DangerGhost`/`Compact*`;
  - старые ключи (`Card`, `PrimaryButton`, `ModernDataGrid`, …) сохранены.
- Все окна переведены на единую раскладку: шапка (иконка + заголовок + контекст) → контент
  в карточках → нижняя панель (статус слева, действия справа). Имена элементов и обработчики
  событий не менялись.
- `AppMessageDialog` + `AppMessageBox` — диалог в стиле плагина; все `MessageBox.Show` в Views
  заменены на `AppMessageBox.Show` (те же параметры и `MessageBoxResult`).
- `Views/ScrollBehavior.cs` — колесо мыши во вложенных DataGrid прокручивает внешний список.
- Окно входа больше не растягивается на всю высоту экрана.
- Убраны fade-in анимации строк (Opacity=0 на старте) и тени на контейнерах с таблицами.

## Проверка

- Сборка на Linux: копия `SBS` + reference-пакеты `Nice3point.Revit.Api.RevitAPI(UI) 2025.*`,
  `dotnet build -p:EnableWindowsTargeting=true` — OK. Ссылки StaticResource проверены скриптом.
- Визуально в Revit не проверялось — нужен прогон на Windows.

## Найденные баги (не исправлены, ждут решения)

См. отчёт в чате сессии: пустой синий баннер в хабе после обновления статусов, ошибки
в «успешном» баннере, NRE в Re-sync при `SelectedRemont == null`, мёртвый бейдж
`SentBadge`, «залипающий» tooltip ошибки ДС, вход через панель без проверки версии.
