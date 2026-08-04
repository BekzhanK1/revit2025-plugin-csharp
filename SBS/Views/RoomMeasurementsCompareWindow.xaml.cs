using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public class RoomMeasurementCompareParamVm
    {
        public string param_code { get; set; }
        public string param_name { get; set; }
        public double? schedule_value { get; set; }
        public double? code_value { get; set; }
        public RoomMeasurementCompareStatus Status { get; set; }

        public string schedule_value_display =>
            schedule_value.HasValue
                ? schedule_value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "—";

        public string code_value_display =>
            code_value.HasValue
                ? code_value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : "—";

        public string delta_display
        {
            get
            {
                if (!schedule_value.HasValue || !code_value.HasValue)
                    return "—";

                var delta = code_value.Value - schedule_value.Value;
                if (Math.Abs(delta) < 0.005d)
                    return "0";

                return (delta > 0d ? "+" : string.Empty)
                       + delta.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        public string status_display => Status switch
        {
            RoomMeasurementCompareStatus.Match => "Совпадает",
            RoomMeasurementCompareStatus.Mismatch => "Расхождение",
            RoomMeasurementCompareStatus.ScheduleOnly => "Только спецификация",
            RoomMeasurementCompareStatus.CodeOnly => "Только код",
            _ => "—"
        };

        public bool IsDifference =>
            Status is RoomMeasurementCompareStatus.Mismatch
                or RoomMeasurementCompareStatus.ScheduleOnly
                or RoomMeasurementCompareStatus.CodeOnly;
    }

    public class RoomMeasurementsCompareRoomVm
    {
        public string RoomName { get; set; }
        public List<RoomMeasurementCompareParamVm> Parameters { get; set; }
    }

    public partial class RoomMeasurementsCompareWindow : Window
    {
        readonly Document _doc;
        RoomMeasurementsCompareSnapshot _snapshot;
        List<RoomMeasurementsCompareRoomVm> _allRooms;

        public RoomMeasurementsCompareWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            Loaded += RoomMeasurementsCompareWindow_Loaded;
        }

        async void RoomMeasurementsCompareWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _snapshot = RoomMeasurementsCompareService.Compare(_doc);
            _allRooms = _snapshot.Rooms.Select(ToRoomVm).ToList();
            ApplyFilter();

            var total = _snapshot.MatchCount
                          + _snapshot.MismatchCount
                          + _snapshot.ScheduleOnlyCount
                          + _snapshot.CodeOnlyCount;

            StatusTextBlock.Text = total > 0
                ? $"Строк: {total}. Совпадений: {_snapshot.MatchCount}, расхождений: {_snapshot.MismatchCount}, "
                  + $"только спецификация: {_snapshot.ScheduleOnlyCount}, только код: {_snapshot.CodeOnlyCount}."
                : "Нет заполненных значений ни в спецификациях, ни в расчёте по модели.";
                
            await LoaderOverlay.HideAsync();
        }

        void ApplyFilter()
        {
            var differencesOnly = DifferencesOnlyCheckBox.IsChecked == true;
            var rooms = differencesOnly
                ? _allRooms
                    .Select(r => new RoomMeasurementsCompareRoomVm
                    {
                        RoomName = r.RoomName,
                        Parameters = r.Parameters.Where(p => p.IsDifference).ToList()
                    })
                    .Where(r => r.Parameters.Count > 0)
                    .ToList()
                : _allRooms;

            RoomsItemsControl.ItemsSource = rooms;

            var hasRows = rooms.Count > 0;
            RoomsItemsControl.Visibility = hasRows
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            NoDataTextBlock.Visibility = hasRows
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            if (differencesOnly && !hasRows && _allRooms.Count > 0)
                NoDataTextBlock.Text = "Расхождений не найдено — значения совпадают.";
            else if (!hasRows)
                NoDataTextBlock.Text = "Нет данных для сравнения.";
        }

        void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_allRooms == null)
                return;

            ApplyFilter();
        }

        static RoomMeasurementsCompareRoomVm ToRoomVm(RoomMeasurementsCompareRoomRow row) =>
            new RoomMeasurementsCompareRoomVm
            {
                RoomName = row.RoomName,
                Parameters = row.Parameters
                    .Select(p => new RoomMeasurementCompareParamVm
                    {
                        param_code = p.param_code,
                        param_name = p.param_name,
                        schedule_value = p.schedule_value,
                        code_value = p.code_value,
                        Status = p.Status
                    })
                    .ToList()
            };

        void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
