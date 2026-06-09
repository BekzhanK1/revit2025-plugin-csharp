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
            SetStatus("Поиск...", isError: false);

            try
            {
                var byRemontId = ByRemontIdRadio.IsChecked == true;
                var results = await RemontService.QuickSearchAsync(byRemontId, id)
                    .ConfigureAwait(true);

                ResultsListBox.ItemsSource = results;

                if (results.Count == 0)
                {
                    ResultsSection.Visibility = Visibility.Collapsed;
                    SetStatus("Ничего не найдено. Проверьте ID и доступ к заявке.", isError: false);
                }
                else
                {
                    ResultsSection.Visibility = Visibility.Visible;
                    SetStatus(results.Count == 1
                        ? "Найден 1 результат — нажмите для выбора"
                        : $"Найдено {results.Count} — выберите нужный",
                        isError: false);

                    if (restoreSelection != null)
                        SelectInList(restoreSelection);
                    else if (results.Count == 1)
                        ResultsListBox.SelectedIndex = 0;
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

        void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsListBox.SelectedItem is RemontOption selected)
            {
                ExportRoomsApplication.SelectedRemont = selected;
                ShowSelectedPreview(selected);
            }

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
            ResultsListBox.ItemsSource = null;
            ResultsListBox.SelectedItem = null;
            ResultsSection.Visibility = Visibility.Collapsed;
        }

        void SelectInList(RemontOption remont)
        {
            if (ResultsListBox.ItemsSource == null)
                return;

            foreach (RemontOption item in ResultsListBox.Items)
            {
                if (item.ClientRequestId == remont.ClientRequestId &&
                    item.RemontId == remont.RemontId)
                {
                    ResultsListBox.SelectedItem = item;
                    ShowSelectedPreview(item);
                    return;
                }
            }
        }

        void ShowSelectedPreview(RemontOption remont)
        {
            SelectedPanel.Visibility = Visibility.Visible;
            SelectedTextBlock.Text = $"Выбрано: {remont.Name}";
        }

        void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(isError ? "#C0392B" : "#9CA3AF"));
        }

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
            var confirmed = ExportRoomsApplication.SelectedRemont;
            ContinueButton.IsEnabled = confirmed != null;

            if (confirmed != null)
                ShowSelectedPreview(confirmed);
        }
    }
}
