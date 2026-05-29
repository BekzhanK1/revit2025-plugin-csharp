using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Microsoft.Win32;
using Newtonsoft.Json;
using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace SmartRemont.ExportRooms.Views
{
    // ─── Simple view-models ────────────────────────────────────────────────────

    /// <summary>Editable parameter name mapping shown in the UI.</summary>
    public class ParameterRowVm : INotifyPropertyChanged
    {
        private string _value;

        public string Key   { get; init; }
        public string Label { get; init; }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One row in the preview list.</summary>
    public class RoomPreviewVm
    {
        public string ApartmentNumber { get; set; }
        public string DisplayName     { get; set; }
        public string LevelName       { get; set; }
        public string AreaStr         { get; set; }
    }

    public class ScheduleMappingRowVm : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _discipline;
        private string _workType;
        private string _colMaterialName;
        private string _colMaterialCode;
        private string _colQuantity;
        private string _colUnit;
        private string _colRoomName;
        private string _colRoomNumber;
        private string _colApartment;

        public string ScheduleName { get; init; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public string Discipline
        {
            get => _discipline;
            set { _discipline = value; OnPropertyChanged(); }
        }

        public string WorkType
        {
            get => _workType;
            set { _workType = value; OnPropertyChanged(); }
        }

        public string ColMaterialName
        {
            get => _colMaterialName;
            set { _colMaterialName = value; OnPropertyChanged(); }
        }

        public string ColMaterialCode
        {
            get => _colMaterialCode;
            set { _colMaterialCode = value; OnPropertyChanged(); }
        }

        public string ColQuantity
        {
            get => _colQuantity;
            set { _colQuantity = value; OnPropertyChanged(); }
        }

        public string ColUnit
        {
            get => _colUnit;
            set { _colUnit = value; OnPropertyChanged(); }
        }

        public string ColRoomName
        {
            get => _colRoomName;
            set { _colRoomName = value; OnPropertyChanged(); }
        }

        public string ColRoomNumber
        {
            get => _colRoomNumber;
            set { _colRoomNumber = value; OnPropertyChanged(); }
        }

        public string ColApartment
        {
            get => _colApartment;
            set { _colApartment = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ─── Window ────────────────────────────────────────────────────────────────

    public partial class ExportSmartRemontRoomsWindow : Window
    {
        // ── State ──────────────────────────────────────────────────────────────
        private readonly Document _doc;
        private List<Room> _allRooms = new();
        private List<Room> _filteredRooms = new();

        // Parameter rows bound to the ItemsControl
        private readonly ObservableCollection<ParameterRowVm> _paramRows = new();

        private readonly ObservableCollection<ScheduleMappingRowVm> _scheduleRows = new();
        private readonly IReadOnlyList<string> _disciplines = new[]
        {
            "Floors",
            "Ceilings",
            "WallPaint",
            "Wallpaper",
            "FloorTile",
            "WallTile",
            "Baseboard",
            "Molding",
            "Adhesives",
            "Grout",
            "Primer",
            "Doors",
            "Windows",
            "Electrical",
            "Plumbing"
        };

        // ── Constructor ────────────────────────────────────────────────────────
        public ExportSmartRemontRoomsWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);

            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "Document is null — окно не может быть открыто");
            
            _doc = doc;
            DataContext = this;

            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                TxtOutputPath.Text = Path.Combine(desktop,
                    $"SmartRemont_Rooms_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                BuildParameterRows();
                LoadPhases();
                LoadSchedules();
                LoadMappingIfExists();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка инициализации окна:\n\n{ex.GetType().Name}: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}",
                    "Диагностика",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                throw;
            }
        }

        public ObservableCollection<ScheduleMappingRowVm> ScheduleRows => _scheduleRows;
        public IReadOnlyList<string> Disciplines => _disciplines;

        // ── Setup ──────────────────────────────────────────────────────────────

        private void BuildParameterRows()
        {
            _paramRows.Add(new ParameterRowVm { Key = "ApartmentNumber", Label = "Номер квартиры",  Value = "ADSK_Номер квартиры" });
            _paramRows.Add(new ParameterRowVm { Key = "FloorFinish",     Label = "Отделка пола",    Value = "Отделка пола" });
            _paramRows.Add(new ParameterRowVm { Key = "WallFinish",      Label = "Отделка стен",    Value = "Отделка стен" });
            _paramRows.Add(new ParameterRowVm { Key = "CeilingFinish",   Label = "Отделка потолка", Value = "Отделка потолка" });
            _paramRows.Add(new ParameterRowVm { Key = "Level",           Label = "Уровень (доп.)",  Value = "Уровень" });
            _paramRows.Add(new ParameterRowVm { Key = "IfcGuid",         Label = "IFC GUID",        Value = "IfcGUID" });

            ParameterRows.ItemsSource = _paramRows;

            // Refresh preview whenever any param name changes
            foreach (var row in _paramRows)
                row.PropertyChanged += (_, _) => RefreshPreview();
        }

        private void LoadPhases()
        {
            var phases = new FilteredElementCollector(_doc)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .OrderBy(p => p.Name)
                .ToList();

            CmbPhase.ItemsSource = phases;

            // Pre-select "После монтажных работ" if it exists, otherwise first phase
            var preferred = phases.FirstOrDefault(p =>
                p.Name.Equals("После монтажных работ", StringComparison.OrdinalIgnoreCase));

            CmbPhase.SelectedItem = preferred ?? phases.FirstOrDefault();
        }

        private void LoadSchedules()
        {
            _scheduleRows.Clear();

            var schedules = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(IsExportable)
                .OrderBy(s => s.Name)
                .ToList();

            foreach (var s in schedules)
            {
                _scheduleRows.Add(new ScheduleMappingRowVm
                {
                    ScheduleName = s.Name,
                    IsEnabled = false,
                    Discipline = string.Empty,
                    WorkType = string.Empty,
                    ColMaterialName = "Тип",
                    ColMaterialCode = "ID",
                    ColQuantity = "Площадь, м²",
                    ColUnit = "Ед. изм.",
                    ColRoomName = "Наименование помещений",
                    ColRoomNumber = "№",
                    ColApartment = "ADSK_Номер квартиры"
                });
            }
        }

        private string GetMappingPath()
        {
            var outputPath = TxtOutputPath?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(outputPath))
                return null;

            var dir = Path.GetDirectoryName(outputPath);
            var baseName = Path.GetFileNameWithoutExtension(outputPath);
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(baseName))
                return null;

            return Path.Combine(dir, baseName + ".mapping.json");
        }

        private void LoadMappingIfExists()
        {
            try
            {
                var mappingPath = GetMappingPath();
                if (string.IsNullOrWhiteSpace(mappingPath) || !File.Exists(mappingPath))
                    return;

                var json = File.ReadAllText(mappingPath);
                var cfg = JsonConvert.DeserializeObject<ScheduleMappingConfig>(json);
                if (cfg?.Schedules == null || cfg.Schedules.Count == 0)
                    return;

                var byName = cfg.Schedules
                    .Where(s => !string.IsNullOrWhiteSpace(s.ScheduleName))
                    .ToDictionary(s => s.ScheduleName, StringComparer.OrdinalIgnoreCase);

                foreach (var row in _scheduleRows)
                {
                    if (!byName.TryGetValue(row.ScheduleName, out var m)) continue;

                    row.IsEnabled = m.IsEnabled;
                    row.Discipline = m.Discipline ?? string.Empty;
                    row.WorkType = m.WorkType ?? string.Empty;
                    row.ColMaterialName = m.ColMaterialName ?? row.ColMaterialName;
                    row.ColMaterialCode = m.ColMaterialCode ?? row.ColMaterialCode;
                    row.ColQuantity = m.ColQuantity ?? row.ColQuantity;
                    row.ColUnit = m.ColUnit ?? row.ColUnit;
                    row.ColRoomName = m.ColRoomName ?? row.ColRoomName;
                    row.ColRoomNumber = m.ColRoomNumber ?? row.ColRoomNumber;
                    row.ColApartment = m.ColApartment ?? row.ColApartment;
                }
            }
            catch
            {
                // don't block UI if config is corrupted
            }
        }

        private void SaveMapping()
        {
            var mappingPath = GetMappingPath();
            if (string.IsNullOrWhiteSpace(mappingPath))
                return;

            var cfg = new ScheduleMappingConfig
            {
                Schedules = _scheduleRows
                    .Select(r => new ScheduleMapping
                    {
                        ScheduleName = r.ScheduleName,
                        IsEnabled = r.IsEnabled,
                        Discipline = r.Discipline,
                        WorkType = r.WorkType,
                        ColMaterialName = r.ColMaterialName,
                        ColMaterialCode = r.ColMaterialCode,
                        ColQuantity = r.ColQuantity,
                        ColUnit = r.ColUnit,
                        ColRoomName = r.ColRoomName,
                        ColRoomNumber = r.ColRoomNumber,
                        ColApartment = r.ColApartment
                    })
                    .ToList()
            };

            File.WriteAllText(mappingPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
        }

        private static bool IsExportable(ViewSchedule schedule)
        {
            if (schedule == null) return false;
            if (schedule.IsTemplate) return false;
            if (schedule.IsTitleblockRevisionSchedule) return false;
            if (schedule.IsInternalKeynoteSchedule) return false;

            var defn = schedule.Definition;
            if (defn == null) return false;

            var categoryId = defn.CategoryId;
            if (categoryId == new ElementId(BuiltInCategory.OST_Sheets)) return false;
            if (categoryId == new ElementId(BuiltInCategory.OST_Revisions)) return false;
            if (categoryId == new ElementId(BuiltInCategory.OST_Views)) return false;

            return true;
        }

        private List<SmartRemontWorkItemDto> ExportWorkItemsFromSelectedSchedules()
        {
            var result = new List<SmartRemontWorkItemDto>();

            var enabledRows = _scheduleRows.Where(r => r.IsEnabled).ToList();
            if (enabledRows.Count == 0)
                return result;

            var schedulesByName = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(IsExportable)
                .ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var row in enabledRows)
            {
                if (!schedulesByName.TryGetValue(row.ScheduleName, out var schedule))
                    continue;

                result.AddRange(ReadScheduleWorkItems(schedule, row));
            }

            return result;
        }

        private List<SmartRemontWorkItemDto> ReadScheduleWorkItems(
            ViewSchedule schedule,
            ScheduleMappingRowVm mapping)
        {
            var items = new List<SmartRemontWorkItemDto>();

            TableData td;
            try { td = schedule.GetTableData(); }
            catch { return items; }

            if (td == null) return items;

            var body = td.GetSectionData(SectionType.Body);
            if (body == null) return items;

            int nRows = body.NumberOfRows;
            int nCols = body.NumberOfColumns;
            if (nRows <= 0 || nCols <= 0) return items;

            var headerRowIndex = 0;
            var headerToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < nCols; c++)
            {
                var header = (schedule.GetCellText(SectionType.Body, headerRowIndex, c) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(header)) continue;
                if (!headerToIndex.ContainsKey(header))
                    headerToIndex[header] = c;
            }

            int? colMaterialName = ResolveColumn(headerToIndex, mapping.ColMaterialName);
            int? colMaterialCode = ResolveColumn(headerToIndex, mapping.ColMaterialCode);
            int? colQty          = ResolveColumn(headerToIndex, mapping.ColQuantity);
            int? colUnit         = ResolveColumn(headerToIndex, mapping.ColUnit);
            int? colRoomName     = ResolveColumn(headerToIndex, mapping.ColRoomName);
            int? colRoomNumber   = ResolveColumn(headerToIndex, mapping.ColRoomNumber);
            int? colApartment    = ResolveColumn(headerToIndex, mapping.ColApartment);

            for (int r = 1; r < nRows; r++)
            {
                var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < nCols; c++)
                {
                    var h = (schedule.GetCellText(SectionType.Body, headerRowIndex, c) ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(h)) continue;
                    raw[h] = (schedule.GetCellText(SectionType.Body, r, c) ?? string.Empty).Trim();
                }

                string materialName = GetCell(schedule, r, colMaterialName);
                string materialCode = GetCell(schedule, r, colMaterialCode);
                string qtyText      = GetCell(schedule, r, colQty);
                string unit         = GetCell(schedule, r, colUnit);

                // Skip empty / group header / totals
                if (string.IsNullOrWhiteSpace(materialName) &&
                    string.IsNullOrWhiteSpace(materialCode) &&
                    string.IsNullOrWhiteSpace(qtyText))
                    continue;

                var qty = ParseNullableDouble(qtyText);

                // If quantity is missing and material name is empty -> most likely a group label
                if (qty == null && string.IsNullOrWhiteSpace(materialName))
                    continue;

                var wi = new SmartRemontWorkItemDto
                {
                    SourceSchedule = schedule.Name,
                    Discipline = (mapping.Discipline ?? string.Empty).Trim(),
                    WorkType = (mapping.WorkType ?? string.Empty).Trim(),
                    MaterialName = (materialName ?? string.Empty).Trim(),
                    MaterialCode = (materialCode ?? string.Empty).Trim(),
                    Quantity = qty,
                    Unit = (unit ?? string.Empty).Trim(),
                    RoomName = (GetCell(schedule, r, colRoomName) ?? string.Empty).Trim(),
                    RoomNumber = (GetCell(schedule, r, colRoomNumber) ?? string.Empty).Trim(),
                    RawValues = raw
                };

                var apt = GetCell(schedule, r, colApartment);
                if (!string.IsNullOrWhiteSpace(apt))
                    wi.RawValues["ApartmentNumber"] = apt.Trim();

                items.Add(wi);
            }

            return items;
        }

        private static int? ResolveColumn(Dictionary<string, int> headerToIndex, string headerName)
        {
            if (string.IsNullOrWhiteSpace(headerName)) return null;
            return headerToIndex.TryGetValue(headerName.Trim(), out var idx) ? idx : null;
        }

        private static string GetCell(ViewSchedule schedule, int row, int? col)
        {
            if (col == null) return string.Empty;
            return schedule.GetCellText(SectionType.Body, row, col.Value);
        }

        private static readonly Regex NumberRegex = new Regex(@"[-+]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        private static double? ParseNullableDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var m = NumberRegex.Match(s);
            if (!m.Success) return null;

            var token = m.Value.Replace(',', '.');
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            return null;
        }

        // ── Event handlers ─────────────────────────────────────────────────────

        private void CmbPhase_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_doc == null) return;
            if (CmbPhase.SelectedItem is not Phase phase) return;

            // Load rooms for chosen phase
            _allRooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r != null &&
                            r.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() == phase.Id)
                .ToList();

            // Show phase room count hint
            PhaseInfoBorder.Visibility = System.Windows.Visibility.Visible;
            TxtPhaseInfo.Text = $"Помещений в фазе «{phase.Name}»: {_allRooms.Count}";

            RefreshPreview();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => RefreshPreview();

        private void TxtApartmentFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => RefreshPreview();

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title            = "Сохранить JSON",
                Filter           = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName         = Path.GetFileName(TxtOutputPath.Text),
                InitialDirectory = Path.GetDirectoryName(TxtOutputPath.Text)
            };

            if (dlg.ShowDialog() == true)
                TxtOutputPath.Text = dlg.FileName;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSchedulesSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _scheduleRows)
                row.IsEnabled = true;
        }

        private void BtnSchedulesSelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var row in _scheduleRows)
                row.IsEnabled = false;
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtStatus.Text      = "Идёт экспорт…";
                BtnExport.IsEnabled = false;

                var selectedPhase = CmbPhase.SelectedItem as Phase;
                SaveMapping();

                // Build finish map only when the option is enabled (can be slow on large models)
                Dictionary<long, List<SmartRemontFinishItemDto>> finishMap = null;
                if (ChkIncludeFinishes?.IsChecked == true && selectedPhase != null)
                {
                    TxtStatus.Text = "Сбор элементов отделки…";
                    finishMap = BuildRoomFinishMap(_filteredRooms, selectedPhase);
                }

                var roomDtos = _filteredRooms
                    .Select(r => MapRoomToDto(r, finishMap))
                    .OrderBy(r => r.ApartmentNumber)
                    .ThenBy(r => r.Number)
                    .ThenBy(r => r.Name)
                    .ToList();

                TxtStatus.Text = "Чтение спецификаций…";
                var workItems = ExportWorkItemsFromSelectedSchedules();

                var payload = new SmartRemontRoomsExportDto
                {
                    GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Rooms       = roomDtos,
                    WorkItems   = workItems
                };

                File.WriteAllText(TxtOutputPath.Text,
                    JsonConvert.SerializeObject(payload, Formatting.Indented));

                int finishCount = roomDtos.Sum(r => r.Finishes?.Count ?? 0);
                var msg = $"Экспорт завершён.\n\nПомещений: {roomDtos.Count}";
                if (finishMap != null) msg += $"\nЭлементов отделки: {finishCount}";
                if (workItems.Count > 0) msg += $"\nМатериалов/работ (строк): {workItems.Count}";
                msg += $"\n\n{TxtOutputPath.Text}";

                MessageBox.Show(msg, "SmartRemont — готово", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                TxtStatus.Text      = $"Ошибка: {ex.Message}";
                BtnExport.IsEnabled = _filteredRooms.Count > 0;
                MessageBox.Show(ex.Message, "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Preview refresh ────────────────────────────────────────────────────

        private void RefreshPreview()
        {

            // Guard: не запускаться пока конструктор не завершил инициализацию
            if (_paramRows == null || _paramRows.Count == 0) return;
            if (TxtRoomCount == null) return;

            // Apply filters
            _filteredRooms = _allRooms.ToList();

            if (ChkOnlyPlaced?.IsChecked == true)
                _filteredRooms = _filteredRooms.Where(r => r.Area > 0).ToList();

            // Apartment filter (comma-separated)
            var filterText = TxtApartmentFilter?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(filterText))
            {
                var apartNums = filterText
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var aptParamName = ParamValue("ApartmentNumber");
                _filteredRooms = _filteredRooms
                    .Where(r => apartNums.Contains(GetParameterString(r, aptParamName)))
                    .ToList();
            }

            // Stats
            int roomCount = _filteredRooms.Count;
            var aptParam  = ParamValue("ApartmentNumber");

            var apartmentSet = _filteredRooms
                .Select(r => GetParameterString(r, aptParam))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet();

            var levelSet = _filteredRooms
                .Select(r => r.LevelId != ElementId.InvalidElementId
                    ? (_doc.GetElement(r.LevelId) as Level)?.Name ?? ""
                    : "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToHashSet();

            double totalArea = _filteredRooms
                .Sum(r => UnitUtils.ConvertFromInternalUnits(
                    r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0,
                    UnitTypeId.SquareMeters));

            TxtRoomCount.Text     = roomCount.ToString();
            TxtApartmentCount.Text = apartmentSet.Count.ToString();
            TxtLevelCount.Text    = levelSet.Count.ToString();
            TxtTotalArea.Text     = Math.Round(totalArea, 1).ToString("0.0");

            // Preview list (capped at 200 for performance)
            const int previewCap = 200;
            var previewItems = _filteredRooms
                .Take(previewCap)
                .Select(r =>
                {
                    var nameP  = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    var numP   = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    var levelN = r.LevelId != ElementId.InvalidElementId
                        ? (_doc.GetElement(r.LevelId) as Level)?.Name ?? ""
                        : "";
                    var area   = UnitUtils.ConvertFromInternalUnits(
                        r.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0,
                        UnitTypeId.SquareMeters);

                    return new RoomPreviewVm
                    {
                        ApartmentNumber = GetParameterString(r, aptParam),
                        DisplayName     = string.IsNullOrWhiteSpace(numP) ? nameP : $"{numP} — {nameP}",
                        LevelName       = levelN,
                        AreaStr         = $"{Math.Round(area, 1)} м²"
                    };
                })
                .ToList();

            PreviewList.ItemsSource = previewItems;

            TxtPreviewNote.Text = roomCount > previewCap
                ? $"Показано первые {previewCap} из {roomCount} помещений"
                : roomCount == 0
                    ? "Нет помещений, соответствующих фильтрам"
                    : string.Empty;

            // Enable / disable export
            BtnExport.IsEnabled = roomCount > 0;
            TxtStatus.Text      = roomCount == 0 ? "Нет помещений для экспорта" : string.Empty;
        }

        // ── Mapping ────────────────────────────────────────────────────────────

        private SmartRemontRoomDto MapRoomToDto(
            Room room,
            Dictionary<long, List<SmartRemontFinishItemDto>> finishMap)
        {
            var apartmentNumber = GetParameterString(room, ParamValue("ApartmentNumber"));
            var floorFinish     = GetParameterString(room, ParamValue("FloorFinish"));
            var wallFinish      = GetParameterString(room, ParamValue("WallFinish"));
            var ceilingFinish   = GetParameterString(room, ParamValue("CeilingFinish"));
            var levelStr        = GetParameterString(room, ParamValue("Level"));
            var ifcGuid         = GetParameterString(room, ParamValue("IfcGuid"));

            var areaP        = room.get_Parameter(BuiltInParameter.ROOM_AREA);
            var perimeterP   = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
            var heightP      = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
            var upperOffsetP = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);

            var areaInt      = areaP?.AsDouble()      ?? 0d;
            var perimeterInt = perimeterP?.AsDouble() ?? 0d;
            var heightInt    = heightP?.AsDouble()    ?? 0d;
            if (heightInt <= 0 && upperOffsetP?.HasValue == true)
                heightInt = upperOffsetP.AsDouble();

            var areaM2      = Math.Round(UnitUtils.ConvertFromInternalUnits(areaInt,      UnitTypeId.SquareMeters), 2);
            var perimeterM  = Math.Round(UnitUtils.ConvertFromInternalUnits(perimeterInt, UnitTypeId.Meters),       2);
            var heightM     = Math.Round(UnitUtils.ConvertFromInternalUnits(heightInt,    UnitTypeId.Meters),       2);
            // Gross wall area: perimeter × height (no opening deduction since param is often unpopulated)
            var wallAreaM2  = Math.Round(perimeterM * heightM, 2);

            var levelName = string.Empty;
            if (room.LevelId != ElementId.InvalidElementId)
                levelName = (_doc.GetElement(room.LevelId) as Level)?.Name ?? string.Empty;

            var contours = ChkIncludeContours?.IsChecked == true
                ? GetRoomContours(room)
                : new List<List<SmartRemontRoomPointDto>>();

            List<SmartRemontFinishItemDto> finishes = null;
            finishMap?.TryGetValue(room.Id.Value, out finishes);

            return new SmartRemontRoomDto
            {
                RevitId         = room.Id.Value,
                UniqueId        = room.UniqueId ?? string.Empty,
                ApartmentNumber = apartmentNumber,
                Number          = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? string.Empty,
                Name            = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()   ?? string.Empty,
                LevelName       = levelName,
                AreaM2          = areaM2,
                PerimeterM      = perimeterM,
                HeightM         = heightM,
                WallAreaM2      = wallAreaM2,
                FloorFinish     = floorFinish,
                WallFinish      = wallFinish,
                CeilingFinish   = ceilingFinish,
                Level           = levelStr,
                IfcGUID         = ifcGuid,
                Finishes        = finishes ?? new List<SmartRemontFinishItemDto>(),
                Contours        = contours
            };
        }

        // ── Finish element collector ───────────────────────────────────────────

        /// <summary>
        /// Builds a map of roomId → finish elements by spatially locating
        /// Floor, Ceiling, and GenericModel elements into their containing rooms.
        /// </summary>
        private Dictionary<long, List<SmartRemontFinishItemDto>> BuildRoomFinishMap(
            IList<Room> rooms, Phase phase)
        {
            var map = rooms.ToDictionary(r => r.Id.Value, _ => new List<SmartRemontFinishItemDto>());
            var roomIds = map.Keys.ToHashSet();

            void AddToMap(long roomId, SmartRemontFinishItemDto item)
            {
                if (roomIds.Contains(roomId))
                    map[roomId].Add(item);
            }

            Room RoomAt(XYZ pt)
            {
                try { return _doc.GetRoomAtPoint(pt, phase); }
                catch { return null; }
            }

            // ── Floors ───────────────────────────────────────────────────────
            var floors = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .ToList();

            foreach (var el in floors)
            {
                try
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb == null) continue;
                    // Sample a point slightly above the floor surface
                    var pt = new XYZ(
                        (bb.Min.X + bb.Max.X) / 2,
                        (bb.Min.Y + bb.Max.Y) / 2,
                        bb.Min.Z + 0.1);
                    var room = RoomAt(pt);
                    if (room == null) continue;

                    var area = UnitUtils.ConvertFromInternalUnits(
                        el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0,
                        UnitTypeId.SquareMeters);
                    var typeName = (_doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? string.Empty;

                    AddToMap(room.Id.Value, new SmartRemontFinishItemDto
                    {
                        Category = "Floor",
                        TypeName = typeName,
                        RevitId  = el.Id.Value,
                        AreaM2   = Math.Round(area, 3)
                    });
                }
                catch { /* skip malformed elements */ }
            }

            // ── Ceilings ─────────────────────────────────────────────────────
            var ceilings = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_Ceilings)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .ToList();

            foreach (var el in ceilings)
            {
                try
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb == null) continue;
                    // Sample a point slightly below ceiling (inside room volume)
                    var pt = new XYZ(
                        (bb.Min.X + bb.Max.X) / 2,
                        (bb.Min.Y + bb.Max.Y) / 2,
                        bb.Min.Z - 0.1);
                    var room = RoomAt(pt);
                    if (room == null) continue;

                    var area = UnitUtils.ConvertFromInternalUnits(
                        el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0,
                        UnitTypeId.SquareMeters);
                    var typeName = (_doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? string.Empty;

                    AddToMap(room.Id.Value, new SmartRemontFinishItemDto
                    {
                        Category = "Ceiling",
                        TypeName = typeName,
                        RevitId  = el.Id.Value,
                        AreaM2   = Math.Round(area, 3)
                    });
                }
                catch { /* skip */ }
            }

            // ── GenericModel (baseboards, moldings, etc.) ─────────────────────
            var genericModels = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .Cast<Element>()
                .ToList();

            foreach (var el in genericModels)
            {
                try
                {
                    XYZ pt = null;
                    switch (el.Location)
                    {
                        case LocationPoint lp: pt = lp.Point; break;
                        case LocationCurve lc: pt = lc.Curve.Evaluate(0.5, true); break;
                    }
                    if (pt == null)
                    {
                        var bb = el.get_BoundingBox(null);
                        if (bb == null) continue;
                        pt = new XYZ(
                            (bb.Min.X + bb.Max.X) / 2,
                            (bb.Min.Y + bb.Max.Y) / 2,
                            (bb.Min.Z + bb.Max.Z) / 2);
                    }

                    var room = RoomAt(pt);
                    if (room == null) continue;

                    var lengthParam = el.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    var length = lengthParam != null && lengthParam.HasValue
                        ? UnitUtils.ConvertFromInternalUnits(lengthParam.AsDouble(), UnitTypeId.Meters)
                        : 0;

                    var area = UnitUtils.ConvertFromInternalUnits(
                        el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0,
                        UnitTypeId.SquareMeters);

                    var typeName = (_doc.GetElement(el.GetTypeId()) as ElementType)?.Name ?? string.Empty;

                    AddToMap(room.Id.Value, new SmartRemontFinishItemDto
                    {
                        Category = "GenericModel",
                        TypeName = typeName,
                        RevitId  = el.Id.Value,
                        AreaM2   = Math.Round(area, 3),
                        LengthM  = Math.Round(length, 3)
                    });
                }
                catch { /* skip */ }
            }

            return map;
        }

        private static List<List<SmartRemontRoomPointDto>> GetRoomContours(Room room)
        {
            var result  = new List<List<SmartRemontRoomPointDto>>();
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var loops = room.GetBoundarySegments(options);
            if (loops == null) return result;

            foreach (var loop in loops)
            {
                var poly = new List<SmartRemontRoomPointDto>();
                foreach (var seg in loop)
                {
                    var curve = seg.GetCurve();
                    poly.Add(ToPoint(curve.GetEndPoint(0)));
                }
                result.Add(poly);
            }
            return result;
        }

        private static SmartRemontRoomPointDto ToPoint(XYZ p) => new()
        {
            X = Math.Round(UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Meters), 3),
            Y = Math.Round(UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Meters), 3)
        };

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>Returns the current user-entered parameter name for a given key.</summary>
        private string ParamValue(string key)
            => _paramRows.FirstOrDefault(r => r.Key == key)?.Value ?? string.Empty;

        private static string GetParameterString(Element element, string parameterName)
        {
            if (element == null || string.IsNullOrWhiteSpace(parameterName))
                return string.Empty;

            var param = element.LookupParameter(parameterName);
            if (param == null || !param.HasValue)
                return string.Empty;

            return param.StorageType == StorageType.String
                ? param.AsString() ?? string.Empty
                : param.AsValueString() ?? param.AsString() ?? string.Empty;
        }
    }
}
