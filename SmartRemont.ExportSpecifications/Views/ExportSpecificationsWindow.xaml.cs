using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using Newtonsoft.Json;
using SmartRemont.ExportSpecifications.Services;

namespace SmartRemont.ExportSpecifications.Views
{
    public partial class ExportSpecificationsWindow : Window, INotifyPropertyChanged
    {
        readonly Document _doc;

        public ObservableCollection<ScheduleRowVm> ScheduleRows { get; } = new();

        string _countText;
        public string CountText
        {
            get => _countText;
            set
            {
                _countText = value;
                OnPropertyChanged();
            }
        }

        public ExportSpecificationsWindow(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            InitializeComponent();
            DataContext = this;
            LoadSchedules();
        }

        void LoadSchedules()
        {
            ScheduleRows.Clear();
            var schedules = ScheduleExportService.ListExportableSchedules(_doc);
            foreach (var s in schedules)
            {
                ScheduleRows.Add(new ScheduleRowVm
                {
                    ScheduleName = s.Name,
                    IsSelected = false
                });
            }

            CountText = $"Найдено: {ScheduleRows.Count}";
        }

        void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in ScheduleRows)
                row.IsSelected = true;
        }

        void BtnSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in ScheduleRows)
                row.IsSelected = false;
        }

        void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var selected = ScheduleRows
                .Where(r => r.IsSelected)
                .Select(r => r.ScheduleName)
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Выберите хотя бы одну спецификацию.",
                    "Экспорт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var defaultName = $"SmartRemont_Schedules_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var dlg = new SaveFileDialog
            {
                Title = "Сохранить JSON",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = defaultName,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog(this) != true)
                return;

            try
            {
                var payload = ScheduleExportService.BuildExport(_doc, selected);
                var json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                });
                File.WriteAllText(dlg.FileName, json);

                MessageBox.Show(
                    this,
                    $"Экспортировано спецификаций: {payload.Schedules.Count}\n{dlg.FileName}",
                    "Готово",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Ошибка экспорта:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
