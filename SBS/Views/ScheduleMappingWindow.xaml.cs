using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Services;

namespace SmartRemont.ExportRooms.Views
{
    public partial class ScheduleMappingWindow : Window
    {
        private readonly ScheduleMappingWindowViewModel _viewModel;

        public ScheduleMappingWindow(Document doc = null)
        {
            InitializeComponent();
            _viewModel = new ScheduleMappingWindowViewModel(doc);
            DataContext = _viewModel;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.SaveToConfig();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                    "Сбросить все настройки к заводским по умолчанию?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _viewModel.ResetToDefault();
            }
        }
    }

    public class ScheduleMappingWindowViewModel : INotifyPropertyChanged
    {
        private readonly Document _doc;

        public ObservableCollection<EntryViewModel> Entries { get; } = new();
        public ObservableCollection<string> AvailableSchedules { get; } = new();
        public ObservableCollection<string> AvailableRooms { get; } = new();
        public ObservableCollection<string> AvailableRoomsWithEmpty { get; } = new();
        public ObservableCollection<string> KnownParamCodes { get; } = new();
        public ObservableCollection<string> KnownParamNames { get; } = new();

        private ObservableCollection<string> _availableColumns = new();
        public ObservableCollection<string> AvailableColumns
        {
            get => _availableColumns;
            set { _availableColumns = value; OnPropertyChanged(); }
        }

        private string _selectedAvailableSchedule;
        public string SelectedAvailableSchedule
        {
            get => _selectedAvailableSchedule;
            set
            {
                _selectedAvailableSchedule = value;
                OnPropertyChanged();
                UpdateAvailableColumns();
            }
        }

        private string _selectedAvailableColumn;
        public string SelectedAvailableColumn
        {
            get => _selectedAvailableColumn;
            set { _selectedAvailableColumn = value; OnPropertyChanged(); }
        }

        private string _selectedAvailableRoom;
        public string SelectedAvailableRoom
        {
            get => _selectedAvailableRoom;
            set { _selectedAvailableRoom = value; OnPropertyChanged(); }
        }

        private EntryViewModel _selectedEntry;
        public EntryViewModel SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (_selectedEntry != null)
                    _selectedEntry.PropertyChanged -= SelectedEntry_PropertyChanged;

                _selectedEntry = value;
                OnPropertyChanged();

                if (_selectedEntry != null)
                {
                    _selectedEntry.PropertyChanged += SelectedEntry_PropertyChanged;
                    SyncSchedulePickerToEntry();
                }
            }
        }

        public ICommand AddEntryCommand { get; }
        public ICommand RemoveEntryCommand { get; }
        public ICommand PickScheduleCommand { get; }
        public ICommand RemoveScheduleCommand { get; }
        public ICommand PickValueColumnCommand { get; }
        public ICommand RemoveValueColumnCommand { get; }
        public ICommand PickRoomColumnCommand { get; }
        public ICommand RemoveRoomColumnCommand { get; }
        public ICommand PickFilterRoomCommand { get; }
        public ICommand RemoveIncludeRoomCommand { get; }
        public ICommand PickExcludeRoomCommand { get; }
        public ICommand RemoveExcludeRoomCommand { get; }

        public ScheduleMappingWindowViewModel(Document doc = null)
        {
            _doc = doc;

            AddEntryCommand = new RelayCommand(_ => AddEntry());
            RemoveEntryCommand = new RelayCommand(e => RemoveEntry(e as EntryViewModel));
            PickScheduleCommand = new RelayCommand(_ => PickSchedule(SelectedAvailableSchedule));
            RemoveScheduleCommand = new RelayCommand(s => RemoveFrom(SelectedEntry?.Schedules, s as string));
            PickValueColumnCommand = new RelayCommand(_ => PickValueColumn(SelectedAvailableColumn));
            RemoveValueColumnCommand = new RelayCommand(c => RemoveFrom(SelectedEntry?.ValueColumns, c as string));
            PickRoomColumnCommand = new RelayCommand(_ => PickRoomColumn(SelectedAvailableColumn));
            RemoveRoomColumnCommand = new RelayCommand(c => RemoveFrom(SelectedEntry?.RoomColumns, c as string));
            PickFilterRoomCommand = new RelayCommand(_ => PickFilterRoom(SelectedAvailableRoom));
            RemoveIncludeRoomCommand = new RelayCommand(r => RemoveFrom(SelectedEntry?.IncludeRooms, r as string));
            PickExcludeRoomCommand = new RelayCommand(_ => PickExcludeRoom(SelectedAvailableRoom));
            RemoveExcludeRoomCommand = new RelayCommand(r => RemoveFrom(SelectedEntry?.ExcludeRooms, r as string));

            SeedKnownParams();
            LoadRevitApiData();
            LoadFromConfig();
        }

        private bool _syncingParamIdentity;

        private void SelectedEntry_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_syncingParamIdentity) return;
            if (e.PropertyName == nameof(EntryViewModel.ParamCode))
                SyncParamNameFromCode();
            else if (e.PropertyName == nameof(EntryViewModel.ParamName))
                SyncParamCodeFromName();
        }

        private void SeedKnownParams()
        {
            var defaults = GetDefaultEntries();
            KnownParamCodes.Clear();
            KnownParamNames.Clear();
            foreach (var e in defaults)
            {
                if (!string.IsNullOrWhiteSpace(e.ParamCode) && !KnownParamCodes.Contains(e.ParamCode))
                    KnownParamCodes.Add(e.ParamCode);
                if (!string.IsNullOrWhiteSpace(e.ParamName) && !KnownParamNames.Contains(e.ParamName))
                    KnownParamNames.Add(e.ParamName);
            }
        }

        private void SyncParamNameFromCode()
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(SelectedEntry.ParamCode))
                return;

            var match = GetDefaultEntries().FirstOrDefault(e =>
                string.Equals(e.ParamCode, SelectedEntry.ParamCode, StringComparison.OrdinalIgnoreCase));
            if (match == null || string.Equals(SelectedEntry.ParamName, match.ParamName, StringComparison.Ordinal))
                return;

            _syncingParamIdentity = true;
            try { SelectedEntry.ParamName = match.ParamName; }
            finally { _syncingParamIdentity = false; }
        }

        private void SyncParamCodeFromName()
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(SelectedEntry.ParamName))
                return;

            var match = GetDefaultEntries().FirstOrDefault(e =>
                string.Equals(e.ParamName, SelectedEntry.ParamName, StringComparison.OrdinalIgnoreCase));
            if (match == null || string.Equals(SelectedEntry.ParamCode, match.ParamCode, StringComparison.Ordinal))
                return;

            _syncingParamIdentity = true;
            try { SelectedEntry.ParamCode = match.ParamCode; }
            finally { _syncingParamIdentity = false; }
        }

        private void SyncSchedulePickerToEntry()
        {
            if (SelectedEntry?.Schedules == null || SelectedEntry.Schedules.Count == 0)
            {
                UpdateAvailableColumns();
                return;
            }

            var first = SelectedEntry.Schedules[0];
            if (AvailableSchedules.Contains(first) &&
                !string.Equals(SelectedAvailableSchedule, first, StringComparison.OrdinalIgnoreCase))
            {
                SelectedAvailableSchedule = first;
            }
            else
            {
                UpdateAvailableColumns();
            }
        }

        private void LoadRevitApiData()
        {
            if (_doc == null) return;

            try
            {
                var schedules = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(s => !s.IsTemplate && !s.Name.StartsWith("<"))
                    .Select(s => s.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                AvailableSchedules.Clear();
                foreach (var s in schedules)
                    AvailableSchedules.Add(s);

                if (AvailableSchedules.Count > 0 && string.IsNullOrWhiteSpace(SelectedAvailableSchedule))
                    SelectedAvailableSchedule = AvailableSchedules[0];

                var rooms = new FilteredElementCollector(_doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r != null && r.Area > 0)
                    .Select(r => RoomNameMatcher.GetBaseName(
                        r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                AvailableRooms.Clear();
                AvailableRoomsWithEmpty.Clear();
                AvailableRoomsWithEmpty.Add(string.Empty);
                foreach (var r in rooms)
                {
                    AvailableRooms.Add(r);
                    AvailableRoomsWithEmpty.Add(r);
                }

                if (AvailableRooms.Count > 0)
                    SelectedAvailableRoom = AvailableRooms[0];
            }
            catch
            {
                // Revit API unavailable — leave lists empty
            }
        }

        private void UpdateAvailableColumns()
        {
            AvailableColumns = new ObservableCollection<string>();
            SelectedAvailableColumn = null;
            if (_doc == null) return;

            var scheduleName = SelectedAvailableSchedule;
            if (string.IsNullOrWhiteSpace(scheduleName) && SelectedEntry?.Schedules?.Count > 0)
                scheduleName = SelectedEntry.Schedules[0];

            if (string.IsNullOrWhiteSpace(scheduleName)) return;

            try
            {
                var schedule = new FilteredElementCollector(_doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(s => string.Equals(s.Name, scheduleName, StringComparison.OrdinalIgnoreCase));

                if (schedule != null && RoomMeasurementsService.TryReadTable(schedule, out var headers, out _))
                {
                    var cols = new ObservableCollection<string>();
                    foreach (var h in headers.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                        cols.Add(h);
                    AvailableColumns = cols;
                    if (cols.Count > 0)
                        SelectedAvailableColumn = cols[0];
                }
            }
            catch
            {
                // ignore
            }
        }

        private void PickSchedule(string scheduleName)
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(scheduleName)) return;
            AddUnique(SelectedEntry.Schedules, scheduleName);
            SelectedAvailableSchedule = scheduleName;
        }

        private void PickValueColumn(string columnName)
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(columnName)) return;
            AddUnique(SelectedEntry.ValueColumns, columnName);
        }

        private void PickRoomColumn(string columnName)
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(columnName)) return;
            AddUnique(SelectedEntry.RoomColumns, columnName);
        }

        private void PickFilterRoom(string roomName)
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(roomName)) return;
            AddUnique(SelectedEntry.IncludeRooms, roomName);
            RemoveFrom(SelectedEntry.ExcludeRooms, roomName);
        }

        private void PickExcludeRoom(string roomName)
        {
            if (SelectedEntry == null || string.IsNullOrWhiteSpace(roomName)) return;
            AddUnique(SelectedEntry.ExcludeRooms, roomName);
            RemoveFrom(SelectedEntry.IncludeRooms, roomName);
        }

        private static void AddUnique(ObservableCollection<string> list, string item)
        {
            if (list == null || string.IsNullOrWhiteSpace(item)) return;
            if (list.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(item);
        }

        private static void RemoveFrom(ObservableCollection<string> list, string item)
        {
            if (list == null || string.IsNullOrWhiteSpace(item)) return;
            var existing = list.FirstOrDefault(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                list.Remove(existing);
        }

        private void AddEntry()
        {
            var code = KnownParamCodes.FirstOrDefault() ?? "PERIMETER_FLOOR";
            var name = GetDefaultEntries()
                .FirstOrDefault(e => e.ParamCode == code)?.ParamName ?? code;

            var entry = new EntryViewModel
            {
                ParamCode = code,
                ParamName = name,
                Mode = RoomMeasurementsScheduleMapping.ParseMode.GroupedByRoomHeader
            };
            Entries.Add(entry);
            SelectedEntry = entry;
        }

        private void RemoveEntry(EntryViewModel entry)
        {
            if (entry == null || !Entries.Contains(entry)) return;
            Entries.Remove(entry);
            if (SelectedEntry == entry)
                SelectedEntry = Entries.FirstOrDefault();
        }

        public void LoadFromConfig()
        {
            Entries.Clear();
            foreach (var item in RoomMeasurementsScheduleMapping.All)
            {
                if (!KnownParamCodes.Contains(item.ParamCode) && !string.IsNullOrWhiteSpace(item.ParamCode))
                    KnownParamCodes.Add(item.ParamCode);
                if (!KnownParamNames.Contains(item.ParamName) && !string.IsNullOrWhiteSpace(item.ParamName))
                    KnownParamNames.Add(item.ParamName);
                Entries.Add(new EntryViewModel(item));
            }

            if (Entries.Count > 0)
                SelectedEntry = Entries[0];
        }

        public void SaveToConfig()
        {
            var listToSave = Entries.Select(e => e.ToEntry()).ToList();

            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartRemont", "RevitPlugin", "schedule_mappings.json");

            var dir = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(listToSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);

            var field = typeof(RoomMeasurementsScheduleMapping).GetField(
                "_cachedEntries",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(null, null);
        }

        public void ResetToDefault()
        {
            Entries.Clear();
            foreach (var item in GetDefaultEntries())
                Entries.Add(new EntryViewModel(item));
            if (Entries.Count > 0)
                SelectedEntry = Entries[0];
        }

        private static List<RoomMeasurementsScheduleMapping.Entry> GetDefaultEntries()
        {
            var method = typeof(RoomMeasurementsScheduleMapping).GetMethod(
                "CreateDefaultEntries",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            return method?.Invoke(null, null) as List<RoomMeasurementsScheduleMapping.Entry>
                   ?? new List<RoomMeasurementsScheduleMapping.Entry>();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class EntryViewModel : INotifyPropertyChanged
    {
        private string _paramCode;
        public string ParamCode
        {
            get => _paramCode;
            set { if (_paramCode == value) return; _paramCode = value; OnPropertyChanged(); }
        }

        private string _paramName;
        public string ParamName
        {
            get => _paramName;
            set { if (_paramName == value) return; _paramName = value; OnPropertyChanged(); }
        }

        private RoomMeasurementsScheduleMapping.ParseMode _mode;
        public RoomMeasurementsScheduleMapping.ParseMode Mode
        {
            get => _mode;
            set { _mode = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Schedules { get; } = new();
        public ObservableCollection<string> ValueColumns { get; } = new();
        public ObservableCollection<string> RoomColumns { get; } = new();
        public ObservableCollection<string> IncludeRooms { get; } = new();
        public ObservableCollection<string> ExcludeRooms { get; } = new();

        private string _fixedRoomName = string.Empty;
        public string FixedRoomName
        {
            get => _fixedRoomName;
            set { _fixedRoomName = value ?? string.Empty; OnPropertyChanged(); }
        }

        private bool _valueIsInteger;
        public bool ValueIsInteger
        {
            get => _valueIsInteger;
            set { _valueIsInteger = value; OnPropertyChanged(); }
        }

        private bool _isMergedParameter;
        public bool IsMergedParameter
        {
            get => _isMergedParameter;
            set { _isMergedParameter = value; OnPropertyChanged(); }
        }

        public EntryViewModel() { }

        public EntryViewModel(RoomMeasurementsScheduleMapping.Entry entry)
        {
            ParamCode = entry.ParamCode;
            ParamName = entry.ParamName;
            Mode = entry.Mode;
            FixedRoomName = entry.FixedRoomName ?? string.Empty;
            ValueIsInteger = entry.ValueIsInteger;
            IsMergedParameter = entry.IsMergedParameter;
            Fill(Schedules, entry.ScheduleNamesExact);
            Fill(ValueColumns, entry.ValueColumnsExact);
            Fill(RoomColumns, entry.RoomColumnsExact);
            Fill(IncludeRooms, entry.RoomBaseNamesFilter);
            Fill(ExcludeRooms, entry.RoomBaseNamesExclude);
        }

        public RoomMeasurementsScheduleMapping.Entry ToEntry() =>
            new RoomMeasurementsScheduleMapping.Entry
            {
                ParamCode = ParamCode?.Trim(),
                ParamName = ParamName?.Trim(),
                Mode = Mode,
                ScheduleNamesExact = ToListOrNull(Schedules),
                ValueColumnsExact = ToListOrNull(ValueColumns),
                RoomColumnsExact = ToListOrNull(RoomColumns),
                FixedRoomName = string.IsNullOrWhiteSpace(FixedRoomName) ? null : FixedRoomName.Trim(),
                ValueIsInteger = ValueIsInteger,
                RoomBaseNamesFilter = ToListOrNull(IncludeRooms),
                RoomBaseNamesExclude = ToListOrNull(ExcludeRooms),
                IsMergedParameter = IsMergedParameter
            };

        private static void Fill(ObservableCollection<string> target, IEnumerable<string> source)
        {
            target.Clear();
            if (source == null) return;
            foreach (var item in source.Where(s => !string.IsNullOrWhiteSpace(s)))
                target.Add(item.Trim());
        }

        private static List<string> ToListOrNull(ObservableCollection<string> source)
        {
            var list = source?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            return list != null && list.Count > 0 ? list : null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        public RelayCommand(Action<object> execute) { _execute = execute; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter);
    }
}
