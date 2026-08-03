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
        System.Collections.Generic.Dictionary<string, int> _roomIdsByKey = new();

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
            await LoadRoomIdsAsync().ConfigureAwait(true);

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

        async System.Threading.Tasks.Task SendMeasuresAsync()
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
                    $"Замеры отправлены · {result.AppliedRooms} помещ. · {result.AppliedParams} знач.{skippedSuffix}";

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
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка отправки замеров");
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
