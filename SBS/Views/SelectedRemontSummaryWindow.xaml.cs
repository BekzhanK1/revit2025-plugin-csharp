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
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public class RoomAreaRowVm
    {
        public string RoomName { get; set; }
        public double AreaM2 { get; set; }
        public double WallHeightM { get; set; }
        public double? SystemAreaM2 { get; set; }
        public DsAreaCompareStatus AreaCompareStatus { get; set; }
        public bool IsPayloadHeight { get; set; }

        public string AreaDisplay =>
            AreaM2 > 0d
                ? AreaM2.ToString("0.##", CultureInfo.InvariantCulture)
                : "—";

        public string SystemAreaDisplay =>
            SystemAreaM2.HasValue
                ? SystemAreaM2.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "—";

        public string HeightDisplay => WallHeightM > 0d
            ? WallHeightM.ToString("0.##", CultureInfo.InvariantCulture)
            : "—";

        public string AreaDeltaDisplay
        {
            get
            {
                if (!SystemAreaM2.HasValue || AreaM2 <= 0d)
                    return "—";

                var delta = AreaM2 - SystemAreaM2.Value;
                if (Math.Abs(delta) < 0.005d)
                    return "0";

                return (delta > 0d ? "+" : string.Empty)
                       + delta.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        public bool IsAreaDifference =>
            AreaCompareStatus is DsAreaCompareStatus.Mismatch
                or DsAreaCompareStatus.SystemOnly
                or DsAreaCompareStatus.RevitOnly;
    }

    public partial class SelectedRemontSummaryWindow : Window
    {
        readonly Document _doc;
        List<RoomAreaRowVm> _rows = new List<RoomAreaRowVm>();
        List<RoomAreaRowVm> _allRows = new List<RoomAreaRowVm>();
        double? _systemWallHeightM;
        DsAreaCompareStatus? _wallHeightCompareStatus;
        Dictionary<string, int> _roomIdsByKey = new();
        bool _isDsAccepted;

        public string LastSuccessMessage { get; private set; }

        public SelectedRemontSummaryWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            Loaded += SelectedRemontSummaryWindow_Loaded;
        }

        async void SelectedRemontSummaryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BindRemontInfo(ExportRoomsApplication.SelectedRemont);
            await LoadRoomIdsAsync().ConfigureAwait(true);

            var rooms = RoomAreaService.CollectRooms(_doc);
            _allRows = rooms
                .Select(r => new RoomAreaRowVm
                {
                    RoomName = r.RoomName,
                    AreaM2 = r.AreaM2,
                    WallHeightM = r.WallHeightM
                })
                .ToList();

            var phaseName = RoomAreaService.GetPreferredPhaseName(_doc);
            PhaseHintText.Text = $"Фаза: {phaseName}";

            ApplyRowsView();
            ApplyPayloadWallHeightUi(ResolvePayloadWallHeight(_allRows), _allRows, null);
            UpdateTotals();

            var hasRooms = _allRows.Count > 0;
            RoomsDataGrid.Visibility = hasRooms
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            NoRoomsTextBlock.Visibility = hasRooms
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            UpdateSendButtonState();
            await LoadSystemComparisonAsync().ConfigureAwait(true);
            await LoaderOverlay.HideAsync();
        }

        async Task LoadSystemComparisonAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont == null || remont.ClientRequestId <= 0)
            {
                SystemDsInfoText.Text = "Сравнение с системой недоступно: нет ID заявки.";
                ApplyPayloadWallHeightUi(ResolvePayloadWallHeight(_allRows), _allRows, null);
                return;
            }

            SetStatus("Загрузка данных из системы…", isError: false);

            try
            {
                var system = await DsRoomChangeService
                    .ReadAsync(remont.ClientRequestId)
                    .ConfigureAwait(true);

                ApplySystemComparison(system);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось загрузить ДС room-change из системы");
                SystemDsInfoText.Text = $"Не удалось загрузить данные системы: {ex.Message}";
                ApplyPayloadWallHeightUi(ResolvePayloadWallHeight(_allRows), _allRows, null);
                SetStatus("Revit-данные загружены. Сравнение с системой недоступно.", isError: true);
            }
        }

        void ApplySystemComparison(DsRoomChangeSnapshot system)
        {
            if (system == null || !system.HasData)
            {
                SystemDsInfoText.Text = system?.EmptyMessage
                    ?? "В системе пока нет ДС по изменению площадей для этого ремонта.";
                _isDsAccepted = false;
                ApplyPayloadWallHeightUi(ResolvePayloadWallHeight(_allRows), _allRows, null);
                UpdateSendButtonState();
                return;
            }

            _isDsAccepted = system.Header?.IsAccept == 1;

            var dsParts = new List<string>();
            if (system.DsId.HasValue)
                dsParts.Add($"ДС #{system.DsId.Value}");
            if (!string.IsNullOrWhiteSpace(system.DsTypeName))
                dsParts.Add(system.DsTypeName.Trim());
            if (!string.IsNullOrWhiteSpace(system.DsDate))
                dsParts.Add(system.DsDate.Trim());
            SystemDsInfoText.Text = dsParts.Count > 0
                ? $"Данные системы: {string.Join(" · ", dsParts)}"
                : "Данные системы загружены.";

            // Financial summary panel
            ApplyFinancialSummary(system.Sum);

            _systemWallHeightM = system.WallHeightM;
            var systemByKey = DsAreaCompareService.BuildSystemAreaByKey(system.Rooms);
            var revitKeys = new HashSet<string>(
                _allRows.Select(r => DsAreaCompareService.GetRoomCompareKey(r.RoomName)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in _allRows)
            {
                var key = DsAreaCompareService.GetRoomCompareKey(row.RoomName);
                row.SystemAreaM2 = systemByKey.TryGetValue(key, out var systemArea) ? systemArea : null;
                row.AreaCompareStatus = DsAreaCompareService.CompareValues(
                    row.SystemAreaM2,
                    row.AreaM2 > 0d ? row.AreaM2 : (double?)null);
            }

            foreach (var systemRoom in system.Rooms ?? Enumerable.Empty<DsRoomChangeRoomDto>())
            {
                if (systemRoom == null || string.IsNullOrWhiteSpace(systemRoom.RoomName))
                    continue;

                var key = DsAreaCompareService.GetRoomCompareKey(systemRoom.RoomName.Trim());
                if (revitKeys.Contains(key))
                    continue;

                if (!systemRoom.RoomArea.HasValue || systemRoom.RoomArea.Value <= 0d)
                    continue;

                _allRows.Add(new RoomAreaRowVm
                {
                    RoomName = systemRoom.RoomName.Trim(),
                    SystemAreaM2 = Math.Round(systemRoom.RoomArea.Value, 2),
                    AreaCompareStatus = DsAreaCompareStatus.SystemOnly
                });
            }

            var payloadHeight = ResolvePayloadWallHeight(_allRows);
            _wallHeightCompareStatus = DsAreaCompareService.CompareWallHeights(_systemWallHeightM, payloadHeight);
            ApplyPayloadWallHeightUi(payloadHeight, _allRows, _wallHeightCompareStatus);

            ApplyRowsView();
            UpdateTotals();
            UpdateCompareStatusSummary();
            UpdateSendButtonState();
        }

        void ApplyRowsView()
        {
            _rows = DifferencesOnlyCheckBox.IsChecked == true
                ? _allRows.Where(r => r.IsAreaDifference).ToList()
                : _allRows.ToList();

            RoomsDataGrid.ItemsSource = _rows;

            var hasRows = _rows.Count > 0;
            RoomsDataGrid.Visibility = hasRows
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            NoRoomsTextBlock.Visibility = hasRows
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            if (DifferencesOnlyCheckBox.IsChecked == true && !hasRows && _allRows.Count > 0)
                NoRoomsTextBlock.Text = "Расхождений по площадям не найдено.";
            else if (!hasRows)
                NoRoomsTextBlock.Text = "В модели нет размещённых помещений для выбранной фазы.";
        }

        void UpdateTotals()
        {
            var revitTotal = _allRows.Where(r => r.AreaM2 > 0d).Sum(r => r.AreaM2);
            var systemTotal = _allRows.Where(r => r.SystemAreaM2.HasValue).Sum(r => r.SystemAreaM2.Value);
            var count = _allRows.Count(r => r.AreaM2 > 0d || r.SystemAreaM2.HasValue);

            if (count == 0)
            {
                TotalAreaText.Text = "—";
                return;
            }

            if (systemTotal > 0d)
            {
                TotalAreaText.Text =
                    $"Revit {revitTotal.ToString("0.##", CultureInfo.InvariantCulture)} м² · "
                    + $"система {systemTotal.ToString("0.##", CultureInfo.InvariantCulture)} м² · {count} помещ.";
                return;
            }

            TotalAreaText.Text =
                $"{revitTotal.ToString("0.##", CultureInfo.InvariantCulture)} м² · {count} помещ.";
        }

        void UpdateCompareStatusSummary()
        {
            if (_allRows.Count == 0)
                return;

            var compared = _allRows.Where(r => r.AreaCompareStatus != DsAreaCompareStatus.BothEmpty).ToList();
            if (compared.Count == 0)
            {
                SetStatus("Revit-данные загружены.", isError: false);
                return;
            }

            var match = compared.Count(r => r.AreaCompareStatus == DsAreaCompareStatus.Match);
            var mismatch = compared.Count(r => r.AreaCompareStatus == DsAreaCompareStatus.Mismatch);
            var systemOnly = compared.Count(r => r.AreaCompareStatus == DsAreaCompareStatus.SystemOnly);
            var revitOnly = compared.Count(r => r.AreaCompareStatus == DsAreaCompareStatus.RevitOnly);

            if (_isDsAccepted)
            {
                SetStatus("ДС утверждена — редактирование невозможно.", isError: true);
            }
            else if (mismatch > 0 || systemOnly > 0 || revitOnly > 0)
            {
                SetStatus("Проверьте расхождения и нажмите «Отправить».", isError: false);
            }
            else
            {
                SetStatus("Все площади совпадают с системой.", isError: false);
            }

            // Update statistics text below the table
            if (StatsCountText != null)
                StatsCountText.Text =
                    $"Помещений: {compared.Count}  |  Совпадает: {match}  |  Изменено: {mismatch}  |  Только система: {systemOnly}  |  Только Revit: {revitOnly}";
        }

        void DifferencesOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_allRows == null || _allRows.Count == 0)
                return;

            ApplyRowsView();
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
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось загрузить сопоставление комнат для ДС");
            }
        }

        void BindRemontInfo(RemontOption remont)
        {
            if (remont == null)
            {
                RemontIdHeroText.Text = "Ремонт не выбран";
                ClientRequestIdHeroText.Text = "ID заявки: —";
                return;
            }

            RemontIdHeroText.Text = remont.RemontId.HasValue
                ? $"Ремонт #{remont.RemontId.Value}"
                : "Ремонт не создан";
            ClientRequestIdHeroText.Text = $"Заявка #{remont.ClientRequestId}";
        }

        void UpdateSendButtonState()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var revitRows = _allRows.Where(r => r.AreaM2 > 0d).ToList();
            
            if (_isDsAccepted)
            {
                SendButton.IsEnabled = false;
                SetStatus("ДС уже утверждена. Изменения невозможны.", isError: true);
                return;
            }

            var canSend = remont?.RemontId != null && remont.RemontId > 0 && revitRows.Count > 0;
            SendButton.IsEnabled = canSend;

            if (remont?.RemontId == null || remont.RemontId <= 0)
                SetStatus("Отправка недоступна: у выбранной заявки нет ID ремонта.", isError: true);
            else if (revitRows.Count == 0)
                SetStatus("Нет помещений для отправки.", isError: false);
            else if (string.IsNullOrWhiteSpace(StatusTextBlock.Text)
                     || StatusTextBlock.Text.StartsWith("Загрузка")
                     || StatusTextBlock.Text.StartsWith("Revit-данные"))
                SetStatus("Проверьте сравнение и нажмите «Отправить».", isError: false);
        }

        async void SendButton_Click(object sender, RoutedEventArgs e) =>
            await SendAreasAsync();

        async Task SendAreasAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont?.RemontId == null || remont.RemontId <= 0)
            {
                MessageBox.Show("У выбранной заявки ещё нет ремонта — отправка недоступна.", "Smart Remont",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var revitRows = _allRows.Where(r => r.AreaM2 > 0d).ToList();
            if (revitRows.Count == 0)
            {
                MessageBox.Show("Нет помещений Revit для отправки.", "Smart Remont",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var payloadRooms = new List<DsRoomChangeApplyRoomDto>();
            var unresolvedRoomNames = new List<string>();
            foreach (var row in revitRows)
            {
                var key = RoomNameMatcher.GetBaseName(row.RoomName);
                if (_roomIdsByKey.TryGetValue(key, out var roomId))
                {
                    payloadRooms.Add(new DsRoomChangeApplyRoomDto { RoomId = roomId, NewArea = row.AreaM2 });
                }
                else
                {
                    unresolvedRoomNames.Add(row.RoomName);
                }
            }

            if (payloadRooms.Count == 0)
            {
                MessageBox.Show(
                    "Не удалось сопоставить помещения Revit с системой (нет room_id). Отправка недоступна.",
                    "Smart Remont", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true);
            SetStatus("Отправка…", isError: false);

            try
            {
                var wallHeight = ResolvePayloadWallHeight(revitRows);

                var result = await DsRoomChangeService
                    .ApplyAsync(remont.ClientRequestId, wallHeight, payloadRooms)
                    .ConfigureAwait(true);

                var skippedCount = (result.Skipped?.Count ?? 0) + unresolvedRoomNames.Count;
                var skippedSuffix = skippedCount > 0 ? $" · пропущено {skippedCount}" : "";
                SetStatus($"Отправлено: {result.AppliedRooms} помещ.{skippedSuffix} · ДС #{result.DsId}", isError: false);

                LastSuccessMessage = $"Площади отправлены · {result.AppliedRooms} помещ. · ДС #{result.DsId}";

                var details =
                    $"Помещений: {result.AppliedRooms}\n"
                    + $"Высота потолка: {wallHeight.ToString("0.##", CultureInfo.InvariantCulture)} м\n"
                    + $"ДС: #{result.DsId} ({(result.Created ? "создана" : "обновлена")})";

                var reasons = (result.Skipped ?? new List<ApplySkippedRoomDto>())
                    .Select(s => $"— {(string.IsNullOrWhiteSpace(s.RoomName) ? "?" : s.RoomName)}: {s.Reason}")
                    .Concat(unresolvedRoomNames.Select(n => $"— {n}: не сопоставлено с системой (нет room_id)"))
                    .ToList();
                if (reasons.Count > 0)
                    details += "\n\nПропущено:\n" + string.Join("\n", reasons);

                AppMessageDialog.ShowSuccess(this, "Успешно отправлено", "Площади отправлены", details);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, isError: true);
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка отправки ДС room-change");
                MessageBox.Show(ex.Message, "Ошибка отправки", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                UpdateSendButtonState();
            }
        }

        static double ResolvePayloadWallHeight(IEnumerable<RoomAreaRowVm> rows)
        {
            var heights = rows
                .Select(r => r.WallHeightM)
                .Where(h => h > 0d)
                .ToList();

            if (heights.Count == 0)
                return 0d;

            return heights
                .GroupBy(h => h)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .Select(g => g.Key)
                .First();
        }

        void ApplyPayloadWallHeightUi(
            double wallHeight,
            IEnumerable<RoomAreaRowVm> rows,
            DsAreaCompareStatus? wallHeightStatus)
        {
            var rowList = rows?.ToList() ?? new List<RoomAreaRowVm>();
            var hasHeight = wallHeight > 0d;

            PayloadWallHeightPanel.Visibility = hasHeight || _systemWallHeightM.HasValue
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            PayloadWallHeightText.Text = hasHeight
                ? wallHeight.ToString("0.##", CultureInfo.InvariantCulture)
                : "—";

            foreach (var row in rowList)
                row.IsPayloadHeight = hasHeight && Math.Abs(row.WallHeightM - wallHeight) < 0.005d;

            ApplyWallHeightCompareText(wallHeightStatus);
        }

        void ApplyWallHeightCompareText(DsAreaCompareStatus? status)
        {
            if (!status.HasValue || !_systemWallHeightM.HasValue || ResolvePayloadWallHeight(_allRows) <= 0d)
            {
                WallHeightCompareText.Text = string.Empty;
                WallHeightCompareText.Foreground = new SolidColorBrush(
                    (System.Windows.Media.Color)ColorConverter.ConvertFromString("#475569"));
                return;
            }

            switch (status.Value)
            {
                case DsAreaCompareStatus.Match:
                    WallHeightCompareText.Text = "· совпадает";
                    WallHeightCompareText.Foreground = new SolidColorBrush(
                        (System.Windows.Media.Color)ColorConverter.ConvertFromString("#15803D"));
                    break;
                case DsAreaCompareStatus.Mismatch:
                    WallHeightCompareText.Text = "· расхождение";
                    WallHeightCompareText.Foreground = new SolidColorBrush(
                        (System.Windows.Media.Color)ColorConverter.ConvertFromString("#B91C1C"));
                    break;
                case DsAreaCompareStatus.SystemOnly:
                    WallHeightCompareText.Text = "· только в системе";
                    WallHeightCompareText.Foreground = new SolidColorBrush(
                        (System.Windows.Media.Color)ColorConverter.ConvertFromString("#B45309"));
                    break;
                case DsAreaCompareStatus.RevitOnly:
                    WallHeightCompareText.Text = "· только в Revit";
                    WallHeightCompareText.Foreground = new SolidColorBrush(
                        (System.Windows.Media.Color)ColorConverter.ConvertFromString("#1D4ED8"));
                    break;
                default:
                    WallHeightCompareText.Text = string.Empty;
                    break;
            }
        }

        void ApplyFinancialSummary(DsSumDto sum)
        {
            if (FinancialSummaryPanel == null)
                return;

            if (sum == null || (!sum.DsSum.HasValue && !sum.MaterialDiff.HasValue))
            {
                FinancialSummaryPanel.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            FinancialSummaryPanel.Visibility = System.Windows.Visibility.Visible;

            MaterialDiffText.Text = FormatFinancial(sum.MaterialDiff);
            MaterialDiffText.Foreground = GetFinancialBrush(sum.MaterialDiff);

            WorkDiffText.Text = FormatFinancial(sum.WorkDiff);
            WorkDiffText.Foreground = GetFinancialBrush(sum.WorkDiff);

            ServiceDiffText.Text = FormatFinancial(sum.ServiceDiff);
            ServiceDiffText.Foreground = GetFinancialBrush(sum.ServiceDiff);

            DsSumText.Text = FormatFinancial(sum.DsSum);
            DsSumText.Foreground = GetFinancialBrush(sum.DsSum);
        }

        static string FormatFinancial(double? value)
        {
            if (!value.HasValue)
                return "—";

            var formatted = value.Value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
            if (value.Value > 0d) return $"+{formatted} ₸";
            if (value.Value < 0d) return $"{formatted} ₸";
            return "0 ₸";
        }

        static SolidColorBrush GetFinancialBrush(double? value)
        {
            if (!value.HasValue || Math.Abs(value.Value) < 0.01d)
                return new SolidColorBrush(
                    (System.Windows.Media.Color)ColorConverter.ConvertFromString("#64748B"));
            if (value.Value > 0d)
                return new SolidColorBrush(
                    (System.Windows.Media.Color)ColorConverter.ConvertFromString("#15803D"));
            return new SolidColorBrush(
                (System.Windows.Media.Color)ColorConverter.ConvertFromString("#DC2626"));
        }

        void SetBusy(bool isBusy)
        {
            var revitRows = _allRows.Where(r => r.AreaM2 > 0d).ToList();
            SendButton.IsEnabled = !isBusy &&
                !_isDsAccepted &&
                ExportRoomsApplication.SelectedRemont?.RemontId > 0 &&
                revitRows.Count > 0;
            SendButton.Content = isBusy ? "Отправка…" : "Отправить";
            CloseButton.IsEnabled = !isBusy;
            DifferencesOnlyCheckBox.IsEnabled = !isBusy;
        }

        void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(
                (System.Windows.Media.Color)ColorConverter.ConvertFromString(isError ? "#C0392B" : "#666666"));
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
