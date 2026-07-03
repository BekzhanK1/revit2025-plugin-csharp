# Checklist — task-06-hub-init-ui

## Hub

- [x] `RemontHubWindow.xaml` — кнопка «Инициализировать проект» (первая в меню)
- [x] Subtitle: «Копия RVT, remont_id в модели, все материалы»
- [x] Click → confirm dialog с путём файла preview
- [x] Progress UI (StatusTextBlock через IProgress)
- [x] Success → путь + «Откройте файл …» если active doc не переключился

## States

- [x] Doc not initialized → кнопка enabled (при выбранном remont_id)
- [x] Doc initialized, remont match → badge «Инициализирован #21642»
- [x] Doc initialized, remont mismatch → warning, disable init

## Code

- [x] `RemontHubWindow.xaml.cs` — вызов `ProjectInitService`
- [x] `dotnet build SBS.sln -c Release`
