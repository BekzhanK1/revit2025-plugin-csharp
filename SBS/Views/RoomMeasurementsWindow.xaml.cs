using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
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
    public class RoomMeasurementParamVm
    {
        public string param_code { get; set; }
        public string param_name { get; set; }
        public double? param_value { get; set; }
        public string param_value_display =>
            param_value.HasValue
                ? param_value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "—";
    }

    public class RoomMeasuresRoomVm
    {
        public string RoomName { get; set; }
        public System.Collections.Generic.List<RoomMeasurementParamVm> Parameters { get; set; }
    }

    public class RoomMeasurementSourceVm
    {
        public string LineText { get; set; }
        public Brush StatusBrush { get; set; }
    }

    public partial class RoomMeasurementsWindow : Window
    {
        readonly Document _doc;
        bool _mappingVisible;
        RoomMeasurementsSnapshot _snapshot;

        public string LastSuccessMessage { get; private set; }

        public RoomMeasurementsWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            Loaded += RoomMeasurementsWindow_Loaded;
        }

        async void RoomMeasurementsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadEventStatusAsync().ConfigureAwait(true);

            _snapshot = RoomMeasurementsService.Collect(_doc);
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
                    .GetStatusAsync(remont.RemontId.Value, RevitEventTypes.Measures)
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

        async System.Threading.Tasks.Task SendMeasuresAsync()
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

                LastSuccessMessage = $"Замеры отправлены · {roomCount} помещ. · {paramCount} знач. · событие #{result?.Id}";

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
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка отправки MEASURES");
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
                           + $"  ведомость: «{s.schedule_name_expected}» → "
                           + (s.Found ? $"«{s.schedule_name_found}»" : "не найдена") + "\n"
                           + $"  {s.Message}",
                StatusBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    s.Found ? "#1B6FC8" : "#CC6666"))
            };

        void MappingToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _mappingVisible = !_mappingVisible;
            MappingPanel.Visibility = _mappingVisible
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            MappingToggleButton.Content = _mappingVisible ? "Скрыть маппинг" : "Маппинг параметров";
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
