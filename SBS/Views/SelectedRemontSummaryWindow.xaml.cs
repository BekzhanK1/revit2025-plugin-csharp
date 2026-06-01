using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public class RoomAreaRowVm
    {
        public string RoomNumber { get; set; }
        public string RoomName { get; set; }
        public double AreaM2 { get; set; }
        public double WallHeightM { get; set; }
        public string AreaDisplay => AreaM2.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public partial class SelectedRemontSummaryWindow : Window
    {
        readonly Document _doc;
        List<RoomAreaRowVm> _rows = new List<RoomAreaRowVm>();

        public string LastSuccessMessage { get; private set; }

        public SelectedRemontSummaryWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            Loaded += SelectedRemontSummaryWindow_Loaded;
        }

        void SelectedRemontSummaryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BindRemontInfo(ExportRoomsApplication.SelectedRemont);

            var rooms = RoomAreaService.CollectRooms(_doc);
            _rows = rooms
                .Select(r => new RoomAreaRowVm
                {
                    RoomNumber = string.IsNullOrWhiteSpace(r.RoomNumber) ? "—" : r.RoomNumber,
                    RoomName = r.RoomName,
                    AreaM2 = r.AreaM2,
                    WallHeightM = r.WallHeightM
                })
                .ToList();

            RoomsDataGrid.ItemsSource = _rows;

            var phaseName = RoomAreaService.GetPreferredPhaseName(_doc);
            PhaseHintText.Text = $"Фаза: {phaseName}";
            var wallHeight = ResolvePayloadWallHeight(_rows);
            WallHeightHintText.Text = wallHeight > 0d
                ? $"Высота потолка: {wallHeight.ToString("0.##", CultureInfo.InvariantCulture)} м"
                : "Высота потолка: —";

            var totalArea = _rows.Sum(r => r.AreaM2);
            var count = _rows.Count;

            TotalAreaText.Text = count == 0
                ? "—"
                : $"{totalArea.ToString("0.##", CultureInfo.InvariantCulture)} м² · {count} помещ.";

            var hasRooms = count > 0;
            RoomsDataGrid.Visibility = hasRooms
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            NoRoomsTextBlock.Visibility = hasRooms
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            UpdateSendButtonState();
        }

        void BindRemontInfo(RemontOption remont)
        {
            if (remont == null)
            {
                RemontNameText.Text = "—";
                RemontIdText.Text = "—";
                ClientRequestIdText.Text = "—";
                return;
            }

            RemontNameText.Text = remont.Name ?? "—";
            RemontIdText.Text = remont.RemontId.HasValue
                ? remont.RemontId.Value.ToString()
                : "—";
            ClientRequestIdText.Text = remont.ClientRequestId.ToString();
        }

        void UpdateSendButtonState()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var canSend = remont?.RemontId != null && remont.RemontId > 0 && _rows.Count > 0;
            SendButton.IsEnabled = canSend;

            if (remont?.RemontId == null || remont.RemontId <= 0)
                SetStatus("Отправка недоступна: у выбранной заявки нет ID ремонта.", isError: true);
            else if (_rows.Count == 0)
                SetStatus("Нет помещений для отправки.", isError: false);
            else
                SetStatus("Проверьте площади и нажмите «Отправить».", isError: false);
        }

        async void SendButton_Click(object sender, RoutedEventArgs e) =>
            await SendAreasAsync();

        async System.Threading.Tasks.Task SendAreasAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont?.RemontId == null || remont.RemontId <= 0)
            {
                MessageBox.Show("У выбранного ремонта нет remont_id.", "Smart Remont",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true);
            SetStatus("Отправка…", isError: false);

            try
            {
                var payloadRooms = _rows.Select(r => new RemontRoomAreaDto
                {
                    RoomName = r.RoomName,
                    RoomAreaM2 = r.AreaM2
                }).ToList();
                var wallHeight = ResolvePayloadWallHeight(_rows);

                var result = await RevitEventsService
                    .SendDsAreaChangeAsync(remont.RemontId.Value, wallHeight, payloadRooms)
                    .ConfigureAwait(true);

                var when = string.IsNullOrWhiteSpace(result?.CreatedAt) ? "" : $" · {result.CreatedAt}";
                SetStatus($"Отправлено (событие #{result?.Id}){when}", isError: false);

                LastSuccessMessage = $"Площади отправлены · {payloadRooms.Count} помещ. · событие #{result?.Id}";

                AppMessageDialog.ShowSuccess(
                    this,
                    "Успешно отправлено",
                    "Площади отправлены",
                    $"Помещений: {payloadRooms.Count}\nВысота стен: {wallHeight.ToString("0.##", CultureInfo.InvariantCulture)} м\nСобытие: #{result?.Id}");

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, isError: true);
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка отправки DS_AREA_CHANGE");
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

        void SetBusy(bool isBusy)
        {
            SendButton.IsEnabled = !isBusy && (ExportRoomsApplication.SelectedRemont?.RemontId > 0) && _rows.Count > 0;
            SendButton.Content = isBusy ? "Отправка…" : "Отправить";
            CloseButton.IsEnabled = !isBusy;
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
