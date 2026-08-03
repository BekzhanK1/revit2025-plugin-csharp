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
        System.Collections.Generic.Dictionary<string, int> _roomIdsByKey = new();

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
            await LoadRoomIdsAsync().ConfigureAwait(true);

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
            var canSend = (remont?.ClientRequestId ?? 0) > 0 && hasValues;
            SendButton.IsEnabled = canSend;

            if ((remont?.ClientRequestId ?? 0) <= 0)
                SetStatus("Отправка недоступна: не указан ID заявки.", isError: true);
            else if (!hasValues)
                SetStatus("Нет заполненных замеров для отправки.", isError: false);
            else
                SetStatus("Проверьте замеры и нажмите «Отправить».", isError: false);
        }

        async Task LoadRoomIdsAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont == null || remont.ClientRequestId <= 0)
                return;

            try
            {
                var rooms = await MeasuresService.ReadAsync(remont.ClientRequestId).ConfigureAwait(true);
                _roomIdsByKey = MeasuresService.BuildRoomIdsByKey(rooms);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось загрузить сопоставление комнат для замеров");
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
            if (remont == null || remont.ClientRequestId <= 0)
            {
                MessageBox.Show("Не указан ID заявки — отправка недоступна.", "Smart Remont",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true);
            SetStatus("Отправка…", isError: false);

            try
            {
                var result = await MeasuresService
                    .ApplyAsync(remont.ClientRequestId, _snapshot.Rooms, _roomIdsByKey)
                    .ConfigureAwait(true);

                var skippedCount = result.Skipped?.Count ?? 0;
                var skippedSuffix = skippedCount > 0 ? $" · пропущено {skippedCount}" : "";
                SetStatus(
                    $"Отправлено: {result.AppliedRooms} помещ., {result.AppliedParams} знач.{skippedSuffix}",
                    isError: false);

                LastSuccessMessage =
                    $"Замеры (по коду) отправлены · {result.AppliedRooms} помещ. · {result.AppliedParams} знач.{skippedSuffix}";

                var details = $"Помещений: {result.AppliedRooms}\nЗначений: {result.AppliedParams}";
                if (skippedCount > 0)
                {
                    var reasons = result.Skipped
                        .Select(s => $"— {(string.IsNullOrWhiteSpace(s.RoomName) ? "?" : s.RoomName)}: {s.Reason}");
                    details += "\n\nПропущено:\n" + string.Join("\n", reasons);
                }

                AppMessageDialog.ShowSuccess(this, "Успешно отправлено", "Замеры отправлены", details);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, isError: true);
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка отправки замеров (по коду)");
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
                (ExportRoomsApplication.SelectedRemont?.ClientRequestId ?? 0) > 0 &&
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
