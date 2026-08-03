using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Document = Autodesk.Revit.DB.Document;

namespace SmartRemont.ExportRooms.Views
{
    public partial class HomeWindow : Window
    {
        readonly Document _doc;

        public HomeWindow(Document doc = null)
        {
            _doc = doc;
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            Loaded += HomeWindow_Loaded;
        }

        void HomeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var session = ExportRoomsApplication.CurrentSession;
            var name = session?.DisplayName ?? "пользователь";
            WelcomeTextBlock.Text = $"Добро пожаловать, {name}";

            EnsureBoundFromDocument();

            if (TryShowBoundRemontBanner())
            {
                ApplyBoundRemontLayout(isBound: true);
                UpdatePlaceholderVisibility();
                _ = EnrichBoundRemontAsync();
                return;
            }

            ApplyBoundRemontLayout(isBound: false);
            UpdatePlaceholderVisibility();
        }

        // TODO: оставить разработку для одинаковых квартир на потом (поиск другого remont_id при привязанном проекте).
        void ApplyBoundRemontLayout(bool isBound)
        {
            SearchSection.Visibility = isBound ? Visibility.Collapsed : Visibility.Visible;

            if (isBound)
            {
                ClearResults();
                SetStatus(string.Empty, isError: false);
            }
        }

        void EnsureBoundFromDocument()
        {
            if (_doc == null)
                return;

            if (ExportRoomsApplication.SelectedRemont?.ClientRequestId > 0)
                return;

            ProjectRemontBindingService.TryBindFromDocument(_doc);
        }

        bool TryShowBoundRemontBanner()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var clientRequestId = remont?.ClientRequestId ?? 0;
            var docInitialized = _doc != null && ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc);

            if (clientRequestId <= 0)
            {
                if (!docInitialized)
                    return false;

                EnsureBoundFromDocument();
                remont = ExportRoomsApplication.SelectedRemont;
                clientRequestId = remont?.ClientRequestId ?? 0;
                if (clientRequestId <= 0)
                    return false;
            }

            BoundRemontBannerText.Text = BuildBoundBannerText(remont);
            BoundRemontBanner.Visibility = Visibility.Visible;
            return true;
        }

        async System.Threading.Tasks.Task EnrichBoundRemontAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont == null || remont.ClientRequestId <= 0)
                return;

            await ProjectRemontBindingService.TryEnrichFromQuickSearchAsync(remont)
                .ConfigureAwait(true);

            BoundRemontBannerText.Text = BuildBoundBannerText(remont);
        }

        static string BuildBoundBannerText(RemontOption remont)
        {
            if (remont == null || remont.ClientRequestId <= 0)
                return "Проект привязан к заявке";

            var idPart = remont.RemontId is int boundRemontId && boundRemontId > 0
                ? $"заявке #{remont.ClientRequestId} · ремонту #{boundRemontId}"
                : $"заявке #{remont.ClientRequestId}";
            var placeholder = $"Ремонт #{remont.RemontId}";

            if (!string.IsNullOrWhiteSpace(remont.Name)
                && !string.Equals(remont.Name.Trim(), placeholder, StringComparison.Ordinal))
                return $"Проект привязан к {idPart} · {remont.Name.Trim()}";

            return $"Проект привязан к {idPart}";
        }

        void ContinueToHubButton_Click(object sender, RoutedEventArgs e)
        {
            if (ExportRoomsApplication.SelectedRemont?.ClientRequestId is not int clientRequestId || clientRequestId <= 0)
            {
                SetStatus("Не удалось определить привязанную заявку", isError: true);
                return;
            }

            DialogResult = true;
            Close();
        }

        async void SearchButton_Click(object sender, RoutedEventArgs e) =>
            await RunSearchAsync();

        async void IdTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await RunSearchAsync();
        }

        void IdTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
            UpdatePlaceholderVisibility();

        void IdTextBox_GotFocus(object sender, RoutedEventArgs e) =>
            UpdatePlaceholderVisibility();

        void IdTextBox_LostFocus(object sender, RoutedEventArgs e) =>
            UpdatePlaceholderVisibility();

        async System.Threading.Tasks.Task RunSearchAsync()
        {
            if (!TryParseSearchId(out var id))
            {
                SetStatus("Введите корректный числовой ID заявки", isError: true);
                ClearResults();
                return;
            }

            SetSearchBusy(true);
            SetStatus("Поиск…", isError: false);

            try
            {
                var results = await RemontService.QuickSearchAsync(byRemontId: false, id)
                    .ConfigureAwait(true);

                ResultsListBox.ItemsSource = results;

                if (results.Count == 0)
                {
                    ResultsSection.Visibility = Visibility.Collapsed;
                    SetStatus("Ничего не найдено", isError: false);
                }
                else
                {
                    ResultsSection.Visibility = Visibility.Visible;
                    SetStatus(results.Count == 1
                        ? "Найдено 1 — выберите карточку"
                        : $"Найдено {results.Count} — выберите карточку",
                        isError: false);
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
            if (ResultsListBox.SelectedItem is not RemontOption selected)
                return;

            ExportRoomsApplication.SelectedRemont = selected;
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

        void UpdatePlaceholderVisibility()
        {
            IdPlaceholderTextBlock.Visibility =
                string.IsNullOrWhiteSpace(IdTextBox.Text) && !IdTextBox.IsFocused
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
            SearchProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
