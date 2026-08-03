using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public partial class RoomMeasurementsFromCodeWindow : Window
    {
        readonly Document _doc;
        bool _mappingVisible;
        RoomMeasurementsSnapshot _snapshot;

        public string LastSuccessMessage { get; private set; }

        public RoomMeasurementsFromCodeWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            Loaded += RoomMeasurementsFromCodeWindow_Loaded;
        }

        async void RoomMeasurementsFromCodeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEventStatusAsync().ConfigureAwait(true);

            _snapshot = RoomMeasurementsFromCodeService.Collect(_doc);
            var rooms = _snapshot.Rooms.Select(ToRoomVm).ToList();

            RoomsItemsControl.ItemsSource = rooms;
            SourcesItemsControl.ItemsSource = _snapshot.Sources.Select(ToSourceVm).ToList();

            var hasRows = rooms.Count > 0;
            RoomsItemsControl.Visibility = hasRows
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            NoDataTextBlock.Visibility = hasRows
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            var foundCount = _snapshot.Sources.Count(s => s.Found);
            UpdateSendButtonState();

            if (string.IsNullOrWhiteSpace(StatusTextBlock.Text) || StatusTextBlock.Text.StartsWith("Помещений"))
            {
                StatusTextBlock.Text = hasRows
                    ? $"Помещений: {rooms.Count}. Параметров с данными: {foundCount} из {_snapshot.Sources.Count}."
                    : $"Параметров с данными: {foundCount} из {_snapshot.Sources.Count}.";
            }
        }

        void UpdateSendButtonState()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var hasValues = HasAnyParamValues(_snapshot);
            var canSend = remont?.RemontId != null && remont.RemontId > 0 && hasValues;
            SendButton.IsEnabled = canSend;

            if (remont?.RemontId == null || remont.RemontId <= 0)
                SetStatus("Отправка недоступна: у выбранной заявки нет ID ремонта.", isError: true);
            else if (!hasValues)
                SetStatus("Нет заполненных замеров для отправки.", isError: false);
            else
                SetStatus("Проверьте замеры и нажмите «Отправить».", isError: false);
        }

        async Task LoadEventStatusAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont?.RemontId == null || remont.RemontId <= 0)
                return;

            try
            {
                var status = await RevitEventsService
                    .GetStatusAsync(remont.RemontId.Value, DTO.RevitEventTypes.Measures)
                    .ConfigureAwait(true);
                RevitEventStatusUi.ApplyBanner(EventStatusBanner, EventStatusBannerText, status);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось загрузить статус MEASURES");
            }
        }

        static bool HasAnyParamValues(RoomMeasurementsSnapshot snapshot) =>
            snapshot?.Rooms?.Any(r =>
                r.Parameters != null &&
                r.Parameters.Any(p => p.param_value.HasValue)) == true;

        async void SendButton_Click(object sender, RoutedEventArgs e) =>
            await SendMeasuresAsync();

        async Task SendMeasuresAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont?.RemontId == null || remont.RemontId <= 0)
            {
                MessageBox.Show("У выбранной заявки ещё нет ремонта — отправка недоступна.", "Smart Remont",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true);
            SetStatus("Отправка…", isError: false);

            try
            {
                var result = await RevitEventsService
                    .SendMeasuresAsync(remont.RemontId.Value, _snapshot.Rooms)
                    .ConfigureAwait(true);

                var roomCount = _snapshot.Rooms.Count(r =>
                    r.Parameters?.Any(p => p.param_value.HasValue) == true);
                var paramCount = _snapshot.Rooms
                    .SelectMany(r => r.Parameters ?? Enumerable.Empty<RoomMeasurementParamItem>())
                    .Count(p => p.param_value.HasValue);

                var when = string.IsNullOrWhiteSpace(result?.CreatedAt) ? "" : $" · {result.CreatedAt}";
                SetStatus($"Отправлено (событие #{result?.Id}){when}", isError: false);

                LastSuccessMessage = $"Замеры (по коду) отправлены · {roomCount} помещ. · {paramCount} знач. · событие #{result?.Id}";

                await LoadEventStatusAsync().ConfigureAwait(true);

                AppMessageDialog.ShowSuccess(
                    this,
                    "Успешно отправлено",
                    "Замеры отправлены",
                    $"Помещений: {roomCount}\nЗначений: {paramCount}\nСобытие: #{result?.Id}");

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, isError: true);
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка отправки MEASURES (по коду)");
                MessageBox.Show(ex.Message, "Ошибка отправки", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                UpdateSendButtonState();
            }
        }

        void SetBusy(bool isBusy)
        {
            var hasValues = HasAnyParamValues(_snapshot);
            SendButton.IsEnabled = !isBusy &&
                ExportRoomsApplication.SelectedRemont?.RemontId > 0 &&
                hasValues;
            SendButton.Content = isBusy ? "Отправка…" : "Отправить";
            CloseButton.IsEnabled = !isBusy;
            MappingToggleButton.IsEnabled = !isBusy;
        }

        void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(
                (System.Windows.Media.Color)ColorConverter.ConvertFromString(isError ? "#C0392B" : "#666666"));
        }

        static RoomMeasuresRoomVm ToRoomVm(RoomMeasurementsRoomRow r) =>
            new RoomMeasuresRoomVm
            {
                RoomName = r.RoomName,
                Parameters = r.Parameters
                    .Select(p => new RoomMeasurementParamVm
                    {
                        param_code = p.param_code,
                        param_name = p.param_name,
                        param_value = p.param_value
                    })
                    .ToList()
            };

        static RoomMeasurementSourceVm ToSourceVm(RoomMeasurementSourceInfo s) =>
            new RoomMeasurementSourceVm
            {
                LineText = $"{s.param_code} · {s.param_name}\n"
                           + $"  источник: {s.schedule_name_expected} → {s.schedule_name_found}\n"
                           + $"  {s.Message}",
                StatusBrush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(
                    s.Found ? "#1B6FC8" : "#CC6666"))
            };

        void MappingToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _mappingVisible = !_mappingVisible;
            MappingPanel.Visibility = _mappingVisible
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            MappingToggleButton.Content = _mappingVisible ? "Скрыть детали" : "Детали расчёта";
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
