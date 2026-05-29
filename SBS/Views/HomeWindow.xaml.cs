using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public partial class HomeWindow : Window
    {
        public HomeWindow()
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            Loaded += HomeWindow_Loaded;
        }

        void HomeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var session = ExportRoomsApplication.CurrentSession;
            var name = session?.DisplayName ?? "пользователь";
            WelcomeTextBlock.Text = $"Добро пожаловать, {name}";

            var confirmed = ExportRoomsApplication.SelectedRemont;
            if (confirmed != null)
            {
                if (confirmed.RemontId.HasValue)
                {
                    ByRemontIdRadio.IsChecked = true;
                    IdTextBox.Text = confirmed.RemontId.Value.ToString();
                }
                else
                {
                    ByClientRequestIdRadio.IsChecked = true;
                    IdTextBox.Text = confirmed.ClientRequestId.ToString();
                }

                _ = RunSearchAsync(restoreSelection: confirmed);
            }

            UpdateUiState();
        }

        async void SearchButton_Click(object sender, RoutedEventArgs e) =>
            await RunSearchAsync();

        async void IdTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await RunSearchAsync();
        }

        async System.Threading.Tasks.Task RunSearchAsync(RemontOption restoreSelection = null)
        {
            if (!TryParseSearchId(out var id))
            {
                SetStatus("Введите корректный числовой ID", isError: true);
                ClearResults();
                return;
            }

            SetSearchBusy(true);
            HideError();

            try
            {
                var byRemontId = ByRemontIdRadio.IsChecked == true;
                var results = await RemontService.QuickSearchAsync(byRemontId, id)
                    .ConfigureAwait(true);

                RemontComboBox.ItemsSource = results;
                RemontComboBox.IsEnabled = results.Count > 0;

                if (results.Count == 0)
                {
                    RemontComboBox.SelectedItem = null;
                    SetStatus("Ничего не найдено. Проверьте ID и доступ к заявке.", isError: false);
                    if (ExportRoomsApplication.SelectedRemont == null)
                        SelectedPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SetStatus(results.Count == 1
                        ? "Найден 1 результат — выберите в списке"
                        : $"Найдено: {results.Count} — выберите в списке",
                        isError: false);

                    if (restoreSelection != null)
                        SelectInCombo(restoreSelection);
                    else if (results.Count == 1)
                        RemontComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                ClearResults();
                SetStatus(ex.Message, isError: true);
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка quick_search");
            }
            finally
            {
                SetSearchBusy(false);
                UpdateUiState();
            }
        }

        bool TryParseSearchId(out int id)
        {
            id = 0;
            var text = (IdTextBox.Text ?? string.Empty).Trim();
            return int.TryParse(text, out id) && id > 0;
        }

        void RemontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RemontComboBox.SelectedItem is RemontOption selected)
                ShowSelectedPreview(selected, confirmed: false);
            else if (ExportRoomsApplication.SelectedRemont == null)
                SelectedPanel.Visibility = Visibility.Collapsed;

            UpdateUiState();
        }

        void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (RemontComboBox.SelectedItem is not RemontOption selected)
                return;

            ExportRoomsApplication.SelectedRemont = selected;
            ShowSelectedPreview(selected, confirmed: true);
            UpdateUiState();
        }

        void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (ExportRoomsApplication.SelectedRemont == null)
                return;

            DialogResult = true;
            Close();
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            AuthService.Logout();
            DialogResult = false;
            Close();
        }

        void ClearResults()
        {
            RemontComboBox.ItemsSource = null;
            RemontComboBox.SelectedItem = null;
            RemontComboBox.IsEnabled = false;
        }

        void SelectInCombo(RemontOption remont)
        {
            if (RemontComboBox.ItemsSource == null)
                return;

            foreach (RemontOption item in RemontComboBox.Items)
            {
                if (item.ClientRequestId == remont.ClientRequestId &&
                    item.RemontId == remont.RemontId)
                {
                    RemontComboBox.SelectedItem = item;
                    ShowSelectedPreview(item, confirmed: true);
                    return;
                }
            }
        }

        void ShowSelectedPreview(RemontOption remont, bool confirmed)
        {
            SelectedPanel.Visibility = Visibility.Visible;
            SelectedTextBlock.Text = confirmed
                ? $"Выбрано: {remont.Name}"
                : $"Выбрано: {remont.Name} (нажмите «Выбрать» для подтверждения)";
        }

        void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(isError ? "#C0392B" : "#999999"));
        }

        void HideError() =>
            StatusTextBlock.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#999999"));

        void SetSearchBusy(bool isBusy)
        {
            SearchButton.IsEnabled = !isBusy;
            IdTextBox.IsEnabled = !isBusy;
            ByRemontIdRadio.IsEnabled = !isBusy;
            ByClientRequestIdRadio.IsEnabled = !isBusy;
            SearchButton.Content = isBusy ? "Поиск..." : "Найти";
        }

        void UpdateUiState()
        {
            var hasResults = RemontComboBox.ItemsSource != null &&
                RemontComboBox.Items.Cast<object>().Any();
            var hasComboSelection = RemontComboBox.SelectedItem is RemontOption;
            var confirmed = ExportRoomsApplication.SelectedRemont;

            SelectButton.IsEnabled = hasResults && hasComboSelection;
            ContinueButton.IsEnabled = confirmed != null;

            if (confirmed != null && hasComboSelection &&
                RemontComboBox.SelectedItem is RemontOption selected &&
                selected.ClientRequestId == confirmed.ClientRequestId &&
                selected.RemontId == confirmed.RemontId)
            {
                ShowSelectedPreview(confirmed, confirmed: true);
            }
        }
    }
}
