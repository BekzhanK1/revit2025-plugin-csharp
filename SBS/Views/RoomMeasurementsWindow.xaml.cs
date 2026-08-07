using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public class RoomMeasurementParamVm
    {
        public string param_code { get; set; }
        public string param_name { get; set; }
        public double? param_value { get; set; }
        public double? CurrentValue { get; set; }
        public string SourceHint { get; set; }

        public string param_value_display => Format(param_value);
        public string CurrentValueDisplay => Format(CurrentValue);

        public bool WillSend => param_value.HasValue &&
            (!CurrentValue.HasValue || Math.Abs(param_value.Value - CurrentValue.Value) > 0.001);

        public bool IsMatch => param_value.HasValue && CurrentValue.HasValue &&
            Math.Abs(param_value.Value - CurrentValue.Value) <= 0.001;

        public string DiffDisplay
        {
            get
            {
                if (!param_value.HasValue && !CurrentValue.HasValue) return "—";
                if (!param_value.HasValue) return "нет в Revit";
                if (!CurrentValue.HasValue) return "новое";
                if (IsMatch) return "совпадает";

                var diff = param_value.Value - CurrentValue.Value;
                return diff > 0
                    ? $"▲ +{diff.ToString("0.##", CultureInfo.InvariantCulture)}"
                    : $"▼ {diff.ToString("0.##", CultureInfo.InvariantCulture)}";
            }
        }

        static string Format(double? value) =>
            value.HasValue
                ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "—";
    }

    public class RoomMeasuresRoomVm
    {
        public string RoomName { get; set; }
        public List<RoomMeasurementParamVm> Parameters { get; set; } = new();
        public int OutgoingCount => Parameters?.Count(p => p.WillSend) ?? 0;
        public System.Windows.Visibility BadgeVisibility =>
            OutgoingCount > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public class RoomMeasurementSourceVm
    {
        public string LineText { get; set; }
        public Brush StatusBrush { get; set; }
    }

    public class MeasurePreviewItemVm
    {
        public string RoomName { get; set; }
        public string ParamName { get; set; }
        public string SourceHint { get; set; }
        public string SystemDisplay { get; set; }
        public string RevitDisplay { get; set; }
        public string ActionLabel { get; set; }
        public Brush ActionBrush { get; set; }
        public Brush ActionForeground { get; set; }
    }

    public partial class RoomMeasurementsWindow : Window
    {
        readonly Document _doc;
        bool _previewVisible;
        RoomMeasurementsSnapshot _snapshot;
        Dictionary<string, int> _roomIdsByKey = new();
        Dictionary<string, MeasureRoomInfoDto> _backendRoomsByKey = new();
        List<RoomMeasuresRoomVm> _roomVms = new();

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
            ReloadUi();
            await LoaderOverlay.HideAsync();
        }

        void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new ScheduleMappingWindow(_doc) { Owner = this };
            if (win.ShowDialog() == true)
                ReloadUi();
        }

        void ReloadUi()
        {
            _snapshot = RoomMeasurementsService.Collect(_doc);
            MergeMissingSystemRooms();

            _roomVms = _snapshot.Rooms.Select(ToRoomVm).OrderBy(r => r.RoomName).ToList();
            RoomsListBox.ItemsSource = _roomVms;
            if (_roomVms.Count > 0)
                RoomsListBox.SelectedIndex = 0;

            var hasRows = _roomVms.Count > 0;
            RoomsListBox.Visibility = hasRows ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            NoDataTextBlock.Visibility = hasRows ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

            RefreshPreview();
            UpdateSendButtonState();
            UpdateSummaryHint();
        }

        void MergeMissingSystemRooms()
        {
            if (_backendRoomsByKey == null || _backendRoomsByKey.Count == 0)
                return;

            var existing = new HashSet<string>(
                _snapshot.Rooms.Select(r => RoomNameMatcher.GetBaseName(r.RoomName)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _backendRoomsByKey)
            {
                if (!existing.Add(kvp.Key))
                    continue;

                _snapshot.Rooms.Add(new RoomMeasurementsRoomRow
                {
                    RoomName = kvp.Key,
                    Parameters = RoomMeasurementsScheduleMapping.All
                        .Where(entry => RoomMeasurementsService.ParamAppliesToRoom(entry, kvp.Key))
                        .Select(entry =>
                        {
                            // Комнаты только из системы: если ведомость есть — 0, иначе null.
                            var source = _snapshot.Sources?.FirstOrDefault(s =>
                                string.Equals(s.param_code, entry.ParamCode, StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(s.schedule_name_found)
                                && s.schedule_name_found != "—");
                            return new RoomMeasurementParamItem
                            {
                                param_code = entry.ParamCode,
                                param_name = entry.ParamName,
                                param_value = source != null ? 0d : null
                            };
                        })
                        .ToList()
                });
            }
        }

        void UpdateSummaryHint()
        {
            var outgoing = _roomVms.Sum(r => r.OutgoingCount);
            var withRevit = _roomVms.SelectMany(r => r.Parameters).Count(p => p.param_value.HasValue);
            SummaryHintText.Text = outgoing > 0
                ? $"К отправке: {outgoing} измен. · найдено в Revit: {withRevit}"
                : withRevit > 0
                    ? $"В Revit найдено {withRevit} знач., изменений нет"
                    : "В Revit пока нет значений — проверьте источники";
        }

        void UpdateSendButtonState()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var hasValues = HasAnyParamValues(_snapshot);
            SendButton.IsEnabled = (remont?.ClientRequestId ?? 0) > 0 && hasValues;

            if ((remont?.ClientRequestId ?? 0) <= 0)
                SetStatus("Отправка недоступна: не указан ID заявки.", isError: true);
            else if (!hasValues)
                SetStatus("Нет значений из Revit для отправки. Откройте «Настроить источники» или проверьте ведомости.", isError: false);
            else
                SetStatus("Проверьте превью и нажмите «Отправить» — в систему уйдут значения из колонки «Уйдёт в систему».", isError: false);
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
                _backendRoomsByKey = (rooms ?? Enumerable.Empty<MeasureRoomInfoDto>())
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.RoomName) && r.PlanirovkaRoomId > 0)
                    .GroupBy(r => RoomNameMatcher.GetBaseName(r.RoomName), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
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
            PreviewToggleButton.IsEnabled = !isBusy;
        }

        void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(
                (System.Windows.Media.Color)ColorConverter.ConvertFromString(isError ? "#C0392B" : "#666666"));
        }

        RoomMeasuresRoomVm ToRoomVm(RoomMeasurementsRoomRow r)
        {
            var baseName = RoomNameMatcher.GetBaseName(r.RoomName);
            _backendRoomsByKey.TryGetValue(baseName, out var backendRoom);
            var currentParams = backendRoom?.CurrentParameters;
            var sourcesByCode = (_snapshot?.Sources ?? new List<RoomMeasurementSourceInfo>())
                .GroupBy(s => s.param_code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            return new RoomMeasuresRoomVm
            {
                RoomName = r.RoomName,
                Parameters = (r.Parameters ?? new List<RoomMeasurementParamItem>())
                    .Select(p =>
                    {
                        var currentParam = currentParams?.FirstOrDefault(cp =>
                            string.Equals(cp.ParamCode, p.param_code, StringComparison.OrdinalIgnoreCase));
                        double? currentVal = null;
                        if (currentParam != null &&
                            double.TryParse(currentParam.ParamValue?.Replace(',', '.'),
                                NumberStyles.Any, CultureInfo.InvariantCulture, out var cv))
                            currentVal = cv;

                        sourcesByCode.TryGetValue(p.param_code ?? "", out var source);
                        var sourceHint = source == null
                            ? "источник не настроен"
                            : source.Found
                                ? $"из «{source.schedule_name_found}»"
                                : $"ведомость «{source.schedule_name_expected}» не найдена";

                        return new RoomMeasurementParamVm
                        {
                            param_code = p.param_code,
                            param_name = p.param_name,
                            param_value = p.param_value,
                            CurrentValue = currentVal,
                            SourceHint = sourceHint
                        };
                    })
                    .ToList()
            };
        }

        void RefreshPreview()
        {
            var items = new List<MeasurePreviewItemVm>();
            foreach (var room in _roomVms)
            {
                foreach (var p in room.Parameters.Where(x => x.param_value.HasValue || x.CurrentValue.HasValue))
                {
                    string action;
                    string bg;
                    string fg;
                    if (p.WillSend && !p.CurrentValue.HasValue)
                    {
                        action = "новое → система";
                        bg = "#DBEAFE";
                        fg = "#1D4ED8";
                    }
                    else if (p.WillSend)
                    {
                        action = "обновит систему";
                        bg = "#FEE2E2";
                        fg = "#B91C1C";
                    }
                    else if (p.IsMatch)
                    {
                        action = "без изменений";
                        bg = "#DCFCE7";
                        fg = "#15803D";
                    }
                    else
                    {
                        action = "не уйдёт";
                        bg = "#F1F5F9";
                        fg = "#64748B";
                    }

                    items.Add(new MeasurePreviewItemVm
                    {
                        RoomName = room.RoomName,
                        ParamName = p.param_name,
                        SourceHint = p.SourceHint,
                        SystemDisplay = p.CurrentValueDisplay,
                        RevitDisplay = p.param_value_display,
                        ActionLabel = action,
                        ActionBrush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(bg)),
                        ActionForeground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(fg))
                    });
                }
            }

            // Show outgoing first, then matches, then the rest
            PreviewItemsControl.ItemsSource = items
                .OrderBy(i => i.ActionLabel.StartsWith("обновит") ? 0
                    : i.ActionLabel.StartsWith("новое") ? 1
                    : i.ActionLabel.StartsWith("без") ? 2 : 3)
                .ThenBy(i => i.RoomName)
                .ThenBy(i => i.ParamName)
                .ToList();

            var sendCount = items.Count(i =>
                i.ActionLabel.Contains("обновит", StringComparison.Ordinal) ||
                i.ActionLabel.Contains("новое", StringComparison.Ordinal));
            PreviewSummaryText.Text = sendCount > 0
                ? $"В систему уйдёт {sendCount} значений (новые или отличающиеся). Остальные строки — для справки."
                : "Сейчас нечего менять в системе: либо Revit совпадает с системой, либо в ведомостях нет значений.";
        }

        void RoomsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // no-op — binding handles detail; kept for future hooks
        }

        void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _previewVisible = !_previewVisible;
            PreviewPanel.Visibility = _previewVisible
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            PreviewToggleButton.Content = _previewVisible ? "Скрыть превью" : "Превью отправки";
            if (_previewVisible)
                RefreshPreview();
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
