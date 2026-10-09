using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class RevitMaterialsWindow : Window
    {
        readonly int _clientRequestId;
        readonly Document _doc;
        bool _loadInProgress;
        bool _syncInProgress;
        string _surfacesFileUrl;
        string _surfacesFileHash;
        string _tkFlagsError;
        bool _tkFlagsLoaded;
        bool _hasCompareKit;
        string _extraError;
        HashSet<int> _tkMaterialIds = new();
        List<RevitMaterialRowVm> _rows = new();
        List<MissingMaterialRowVm> _missingRows = new();
        List<ExtraMaterialRowVm> _extraRows = new();

        public RevitMaterialsWindow(int clientRequestId, Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkArea(this);
            _clientRequestId = clientRequestId;
            _doc = doc;
            Loaded += RevitMaterialsWindow_Loaded;
        }

        async void RevitMaterialsWindow_Loaded(object sender, RoutedEventArgs e) =>
            await LoadMaterialsAsync().ConfigureAwait(true);

        async void RetryButton_Click(object sender, RoutedEventArgs e) =>
            await LoadMaterialsAsync().ConfigureAwait(true);

        async void SyncButton_Click(object sender, RoutedEventArgs e) =>
            await SyncMaterialsAsync().ConfigureAwait(true);

        async Task LoadMaterialsAsync()
        {
            if (_loadInProgress)
                return;

            _loadInProgress = true;
            ShowLoading();
            SyncButton.IsEnabled = false;

            try
            {
                var response = await RevitMaterialsService.ReadAsync(_clientRequestId).ConfigureAwait(true);
                _surfacesFileUrl = response.SurfacesFileUrl?.Trim();
                _surfacesFileHash = response.SurfacesFileHash?.Trim();
                // Все материалы ТК, в том числе без модели: SR_ID из этого списка не считаются лишними.
                _tkMaterialIds = (response.Data ?? new List<RevitMaterialRowDto>())
                    .Where(r => r?.MaterialId != null)
                    .Select(r => r.MaterialId.Value)
                    .ToHashSet();
                _rows = (response.Data ?? new List<RevitMaterialRowDto>())
                    .Where(HasUrlOrSurface)
                    .Select(ToRowVm)
                    .ToList();

                var clientRequestId = response.ClientRequestId ?? _clientRequestId;
                ClientRequestTextBlock.Text = response.RemontId is int remontId && remontId > 0
                    ? $"Заявка #{clientRequestId} · Ремонт #{remontId}"
                    : $"Заявка #{clientRequestId}";
                ClientRequestTextBlock.Visibility = System.Windows.Visibility.Visible;

                if (_rows.Count == 0)
                {
                    ShowEmpty();
                    var rawCount = response.Data?.Count ?? 0;
                    StatusTextBlock.Text =
                        rawCount == 0
                            ? $"API вернул 0 материалов.\n{Configs.ApiOriginUrl}"
                            : $"Есть {rawCount} строк, но без URL/surface для синка.\n{Configs.ApiOriginUrl}";
                    AppMessageBox.Show(
                        this,
                        StatusTextBlock.Text
                        + "\n\nЕсли заявка боевая — в app.config / SmartRemont.ExportRooms.dll.config "
                        + "поставьте apiOriginUrl на prod (например https://myspace-api.smartremont.kz) и перезапустите Revit.",
                        "Нет материалов для синхронизации",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                RefreshProjectStatuses();
                RefreshExtraMaterials();
                ShowData(_rows);
                UpdateSummaryStatus();
                SyncButton.IsEnabled = _rows.Any(CanSyncRow);
                await ApplyTkFlagsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Revit materials read failed");
                var msg = ex.Message + $"\n\nAPI: {Configs.ApiOriginUrl}";
                ShowError(msg);
                StatusTextBlock.Text = string.Empty;
                AppMessageBox.Show(
                    this,
                    msg,
                    "Ошибка загрузки материалов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _loadInProgress = false;
            }
        }

        /// <summary>Метки ТК (подбор / наличие). Ошибка не мешает работе окна — колонка остаётся пустой.</summary>
        async Task ApplyTkFlagsAsync()
        {
            var flags = await ClientMaterialFlagsService.TryReadAsync(_clientRequestId).ConfigureAwait(true);
            _tkFlagsError = flags.Status ? null : flags.Error;
            _tkFlagsLoaded = flags.Status;
            if (!flags.Status)
            {
                CompareSourceTextBlock.Text = "Сверка с подбором недоступна";
                _missingRows = new List<MissingMaterialRowVm>();
                MissingDataGrid.ItemsSource = _missingRows;
                UpdateTabCounts();
                ApplyTab();
                UpdateSummaryStatus();
                return;
            }

            var etalon = flags.Data.Etalon?.PresetKitId is int ? flags.Data.Etalon : null;
            _hasCompareKit = etalon != null || flags.Data.KitChecked;
            CompareSourceTextBlock.Text = BuildCompareSourceText(flags.Data, etalon);
            _missingRows = (flags.Data.Missing ?? new List<ClientMaterialMissingRowDto>())
                .Select(m => new MissingMaterialRowVm(m, etalon != null))
                .ToList();
            MissingDataGrid.ItemsSource = _missingRows;
            MissingCalloutText.Text = etalon != null
                ? $"Эти конструктивы есть в эталонном пакете {FormatKit(etalon)}, но их нет в ТК заявки. "
                  + "ТК правится в MySpace; после правки откройте это окно заново и синхронизируйте."
                : "Эти конструктивы есть в подборе заявки, но их нет в ТК. "
                  + "ТК правится в MySpace; после правки откройте это окно заново и синхронизируйте.";
            UpdateTabCounts();
            ApplyTab();

            var byMaterial = ClientMaterialFlagsService.BuildByMaterial(flags.Data);
            foreach (var row in _rows)
            {
                if (row.Source?.MaterialId is int materialId && byMaterial.TryGetValue(materialId, out var info))
                    row.ApplyTkFlag(info.Tone, info.Text);
                else
                    row.ApplyTkFlag(MaterialFlagTone.None, null);
            }
        }

        void RefreshProjectStatuses()
        {
            var materialIds = _rows
                .Where(r => r.Source?.MaterialId != null)
                .Select(r => r.Source.MaterialId.Value)
                .ToList();

            var presence = RevitMaterialPresenceService.CheckMaterials(_doc, materialIds);

            foreach (var row in _rows)
            {
                if (row.Source?.MaterialId == null)
                {
                    row.ApplyPresence(false, null, null);
                    continue;
                }

                if (presence.TryGetValue(row.Source.MaterialId.Value, out var info))
                    row.ApplyPresence(info.IsInProject, info.Label, info.SrId);
                else
                    row.ApplyPresence(false, null, null);
            }
        }

        /// <summary>SR_ID в проекте, которых нет в ТК. Ошибка поиска не мешает остальному окну.</summary>
        void RefreshExtraMaterials()
        {
            List<ProjectExtraSrIdItem> items;
            try
            {
                items = RevitProjectExtraMaterialsService.Find(_doc, _tkMaterialIds);
                _extraError = null;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project extra SR_ID scan failed");
                items = new List<ProjectExtraSrIdItem>();
                _extraError = ex.Message;
            }

            _extraRows = items.Select(i => new ExtraMaterialRowVm(i)).ToList();
            ExtraDataGrid.ItemsSource = _extraRows;
            UpdateTabCounts();
            ApplyTab();
        }

        void Tab_Checked(object sender, RoutedEventArgs e) => ApplyTab();

        void ApplyTab()
        {
            // Checked стреляет ещё в InitializeComponent — элементы ниже по XAML могут быть null.
            if (MaterialsDataGrid == null || MissingDataGrid == null || ExtraDataGrid == null || TabEmptyTextBlock == null)
                return;

            static System.Windows.Visibility Show(bool on) =>
                on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            var materials = MaterialsTab.IsChecked == true;
            var missing = MissingTab.IsChecked == true;
            var extra = ExtraTab.IsChecked == true;

            string emptyText = null;
            if (missing && _missingRows.Count == 0)
            {
                emptyText = !string.IsNullOrWhiteSpace(_tkFlagsError)
                    ? $"Сверка с подбором недоступна: {_tkFlagsError}"
                    : !_tkFlagsLoaded
                        ? "Загрузка сверки…"
                        : _hasCompareKit
                            ? "Всё из пакета есть в ТК."
                            : "У заявки нет подбора — сверять не с чем.";
            }
            else if (extra && _extraRows.Count == 0)
            {
                emptyText = string.IsNullOrWhiteSpace(_extraError)
                    ? "В проекте нет элементов с SR_ID не из ТК."
                    : $"Не удалось проверить проект: {_extraError}";
            }

            MaterialsDataGrid.Visibility = Show(materials);
            MissingDataGrid.Visibility = Show(missing && emptyText == null);
            ExtraDataGrid.Visibility = Show(extra && emptyText == null);
            MissingCallout.Visibility = Show(missing && _missingRows.Count > 0);
            ExtraCallout.Visibility = Show(extra && _extraRows.Count > 0);
            TabEmptyTextBlock.Text = emptyText ?? string.Empty;
            TabEmptyTextBlock.Visibility = Show(emptyText != null);
        }

        void UpdateTabCounts()
        {
            MaterialsTabCountText.Text = _rows.Count.ToString(CultureInfo.InvariantCulture);
            MissingTabCountText.Text = _tkFlagsLoaded ? _missingRows.Count.ToString(CultureInfo.InvariantCulture) : "—";
            ExtraTabCountText.Text = string.IsNullOrWhiteSpace(_extraError)
                ? _extraRows.Count.ToString(CultureInfo.InvariantCulture)
                : "—";
        }

        static string BuildCompareSourceText(ClientMaterialFlagsResponse flags, ClientMaterialEtalonDto etalon)
        {
            string text;
            if (etalon != null)
                text = $"Сверка с эталонным пакетом {FormatKit(etalon)}";
            else if (flags.KitChecked)
                text = "Сверка с подбором заявки (эталонного пакета нет)";
            else
                return "У заявки нет подбора — сверять не с чем";

            var stale = ClientMaterialFlagsService.CountStale(flags);
            if (stale > 0)
                text += $" · неактуальных строк ТК: {stale}";

            if (etalon != null
                && flags.Summary != null
                && flags.Summary.TryGetValue("etalon_missing", out var ownKit)
                && ownKit > 0)
                text += $" · {ownKit} сверены с подбором заявки (в эталоне нет конструктива)";

            return text;
        }

        static string FormatKit(ClientMaterialEtalonDto kit) =>
            string.IsNullOrWhiteSpace(kit?.PresetKitName)
                ? $"{kit?.PresetKitId}"
                : $"{kit.PresetKitId} · {kit.PresetKitName.Trim()}";

        void ApplySyncItemResults(IReadOnlyList<RevitMaterialSyncItemResult> items)
        {
            var byId = (items ?? Array.Empty<RevitMaterialSyncItemResult>())
                .GroupBy(i => i.MaterialId)
                .ToDictionary(g => g.Key, g => g.Last());

            foreach (var row in _rows)
            {
                if (row.Source?.MaterialId == null)
                    continue;

                if (byId.TryGetValue(row.Source.MaterialId.Value, out var item))
                    row.ApplySyncResult(item.Success, item.ErrorMessage);
                else if (!row.HasSyncError)
                    row.ClearSyncError();
            }
        }

        void UpdateSummaryStatus(RevitMaterialsSyncResult syncResult = null)
        {
            var inProject = _rows.Count(r => r.IsInProject);
            var missing = _rows.Count - inProject;
            var text = $"В проекте: {inProject} · Нет в проекте: {missing}";
            if (_extraRows.Count > 0)
                text += $" · Не из ТК в проекте: {_extraRows.Count}";
            if (!string.IsNullOrWhiteSpace(_tkFlagsError))
                text += $" · Подбор / наличие недоступны: {_tkFlagsError}";

            if (syncResult == null)
            {
                StatusTextBlock.Text = text;
                return;
            }

            if (!string.IsNullOrWhiteSpace(syncResult.ErrorMessage)
                && syncResult.TotalSyncable == 0
                && syncResult.MaterialsLoaded == 0
                && syncResult.ErrorCount == 0)
            {
                StatusTextBlock.Text = syncResult.ErrorMessage;
                return;
            }

            if (syncResult.ErrorCount > 0)
            {
                text += $" · Ошибок: {syncResult.ErrorCount}";
                if (!string.IsNullOrWhiteSpace(syncResult.ErrorMessage))
                    text += $" · {syncResult.ErrorMessage}";
            }
            else
            {
                text += " · Синхронизация завершена";
            }

            StatusTextBlock.Text = text;
        }

        async Task SyncMaterialsAsync()
        {
            if (_syncInProgress || _rows.Count == 0)
                return;

            _syncInProgress = true;
            SyncButton.IsEnabled = false;

            if (!_rows.Any(CanSyncRow))
            {
                StatusTextBlock.Text = "Нет файлов для синхронизации.";
                _syncInProgress = false;
                SyncButton.IsEnabled = false;
                return;
            }

            try
            {
                foreach (var row in _rows)
                    row.ClearSyncError();

                var progress = new Progress<RevitMaterialsSyncProgress>(p =>
                {
                    ShowSyncProgress(p.Done, p.Total, p.Message);
                    SyncProgressBar.IsIndeterminate = string.Equals(
                        p.Phase,
                        "import",
                        StringComparison.OrdinalIgnoreCase);
                });

                var result = await RevitMaterialsSyncOrchestrator.SyncAllAsync(
                    _doc,
                    _clientRequestId,
                    _rows.Select(r => r.Source),
                    _surfacesFileUrl,
                    _surfacesFileHash,
                    progress).ConfigureAwait(true);

                SyncProgressBar.IsIndeterminate = false;
                ApplySyncItemResults(result.Items);
                RefreshProjectStatuses();
                RefreshExtraMaterials();
                UpdateSummaryStatus(result);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Revit materials sync failed");
                StatusTextBlock.Text = $"Ошибка синхронизации: {ex.Message}";
            }
            finally
            {
                HideSyncProgress();
                _syncInProgress = false;
                SyncButton.IsEnabled = _rows.Any(CanSyncRow);
            }
        }

        void ShowSyncProgress(int done, int total, string label)
        {
            SyncProgressPanel.Visibility = System.Windows.Visibility.Visible;
            SyncProgressBar.IsIndeterminate = false;
            SyncProgressBar.Maximum = Math.Max(total, 1);
            SyncProgressBar.Value = Math.Min(done, SyncProgressBar.Maximum);
            SyncProgressTextBlock.Text = label;
        }

        void HideSyncProgress()
        {
            SyncProgressPanel.Visibility = System.Windows.Visibility.Collapsed;
            SyncProgressBar.IsIndeterminate = false;
            SyncProgressBar.Value = 0;
        }

        void ShowLoading()
        {
            LoadingPanel.Visibility = System.Windows.Visibility.Visible;
            EmptyTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            ErrorPanel.Visibility = System.Windows.Visibility.Collapsed;
            DataPanel.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ShowEmpty()
        {
            LoadingPanel.Visibility = System.Windows.Visibility.Collapsed;
            EmptyTextBlock.Visibility = System.Windows.Visibility.Visible;
            ErrorPanel.Visibility = System.Windows.Visibility.Collapsed;
            DataPanel.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ShowError(string message)
        {
            LoadingPanel.Visibility = System.Windows.Visibility.Collapsed;
            EmptyTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            ErrorPanel.Visibility = System.Windows.Visibility.Visible;
            ErrorTextBlock.Text = message;
            DataPanel.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ShowData(List<RevitMaterialRowVm> rows)
        {
            LoadingPanel.Visibility = System.Windows.Visibility.Collapsed;
            EmptyTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            ErrorPanel.Visibility = System.Windows.Visibility.Collapsed;
            MaterialsDataGrid.ItemsSource = rows;
            DataPanel.Visibility = System.Windows.Visibility.Visible;
            UpdateTabCounts();
            ApplyTab();
        }

        static bool IsSurfaceRow(RevitMaterialRowDto row) =>
            string.Equals(row?.RevitFileType?.Trim(), "surface", StringComparison.OrdinalIgnoreCase);

        static bool HasUrlOrSurface(RevitMaterialRowDto row) =>
            row != null
            && row.MaterialId.HasValue
            && (IsSurfaceRow(row) || !string.IsNullOrWhiteSpace(row.RevitFileUrl));

        bool CanSyncRow(RevitMaterialRowVm row)
        {
            if (row?.Source?.MaterialId == null)
                return false;

            if (IsSurfaceRow(row.Source))
                return !string.IsNullOrWhiteSpace(_surfacesFileUrl);

            return !string.IsNullOrWhiteSpace(row.Source.RevitFileUrl);
        }

        static RevitMaterialRowVm ToRowVm(RevitMaterialRowDto row) =>
            new RevitMaterialRowVm
            {
                Source = row,
                MaterialIdDisplay = row.MaterialId?.ToString(CultureInfo.InvariantCulture) ?? "—",
                MaterialName = DisplayOrDash(row.MaterialName),
                TypeDisplay = BuildTypeDisplay(row)
            };

        static string BuildTypeDisplay(RevitMaterialRowDto row)
        {
            if (!string.IsNullOrWhiteSpace(row?.MaterialTypeCode))
                return row.MaterialTypeCode.Trim();

            if (!string.IsNullOrWhiteSpace(row?.RevitFileType))
                return row.RevitFileType.Trim();

            return "—";
        }

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }

    sealed class RevitMaterialRowVm : INotifyPropertyChanged
    {
        bool _isInProject;
        bool _hasSyncError;
        string _projectStatusDisplay = "—";
        string _detailDisplay = string.Empty;
        string _syncError;
        string _revitNameDisplay = "—";
        string _srIdInRevitDisplay = "—";

        public RevitMaterialRowDto Source { get; init; }
        public string MaterialIdDisplay { get; init; }
        public string MaterialName { get; init; }
        public string TypeDisplay { get; init; }

        public bool IsInProject
        {
            get => _isInProject;
            private set
            {
                if (_isInProject == value)
                    return;

                _isInProject = value;
                OnPropertyChanged();
            }
        }

        public bool HasSyncError
        {
            get => _hasSyncError;
            private set
            {
                if (_hasSyncError == value)
                    return;

                _hasSyncError = value;
                OnPropertyChanged();
            }
        }

        public string ProjectStatusDisplay
        {
            get => _projectStatusDisplay;
            private set
            {
                if (_projectStatusDisplay == value)
                    return;

                _projectStatusDisplay = value;
                OnPropertyChanged();
            }
        }

        public string DetailDisplay
        {
            get => _detailDisplay;
            private set
            {
                if (_detailDisplay == value)
                    return;

                _detailDisplay = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Имя семейства/типа/материала, под которым SR_ID найден в текущем проекте Revit.</summary>
        public string RevitNameDisplay
        {
            get => _revitNameDisplay;
            private set
            {
                if (_revitNameDisplay == value)
                    return;

                _revitNameDisplay = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Значение параметра SR_ID, фактически найденное на элементе в текущем проекте.</summary>
        public string SrIdInRevitDisplay
        {
            get => _srIdInRevitDisplay;
            private set
            {
                if (_srIdInRevitDisplay == value)
                    return;

                _srIdInRevitDisplay = value;
                OnPropertyChanged();
            }
        }

        string _tkFlagDisplay = "—";
        string _tkFlagTone = nameof(MaterialFlagTone.None);

        /// <summary>Метка ТК: «Заменён в подборе → 18947», «Нет в наличии» и т.п.</summary>
        public string TkFlagDisplay
        {
            get => _tkFlagDisplay;
            private set
            {
                if (_tkFlagDisplay == value)
                    return;

                _tkFlagDisplay = value;
                OnPropertyChanged();
            }
        }

        /// <summary>None | Mute | Warn | Bad — для DataTrigger в XAML.</summary>
        public string TkFlagTone
        {
            get => _tkFlagTone;
            private set
            {
                if (_tkFlagTone == value)
                    return;

                _tkFlagTone = value;
                OnPropertyChanged();
            }
        }

        public void ApplyTkFlag(MaterialFlagTone tone, string text)
        {
            TkFlagTone = tone.ToString();
            TkFlagDisplay = string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        public void ApplyPresence(bool isInProject, string revitLabel, int? srId)
        {
            IsInProject = isInProject;
            RevitNameDisplay = isInProject && !string.IsNullOrWhiteSpace(revitLabel) ? revitLabel.Trim() : "—";
            SrIdInRevitDisplay = isInProject && srId.HasValue
                ? srId.Value.ToString(CultureInfo.InvariantCulture)
                : "—";
            RefreshStatusLabels();
        }

        public void ApplySyncResult(bool success, string errorMessage)
        {
            _syncError = success ? null : (errorMessage?.Trim() ?? "Ошибка синхронизации");
            HasSyncError = !success;
            RefreshStatusLabels();
        }

        public void ClearSyncError()
        {
            if (!HasSyncError && string.IsNullOrEmpty(_syncError))
                return;

            _syncError = null;
            HasSyncError = false;
            RefreshStatusLabels();
        }

        void RefreshStatusLabels()
        {
            // Если материал уже в проекте по SR_ID — это успех; ошибку повторной загрузки не показываем.
            if (IsInProject)
            {
                if (HasSyncError)
                {
                    _syncError = null;
                    HasSyncError = false;
                }

                ProjectStatusDisplay = "В проекте";
                DetailDisplay = string.Empty;
                return;
            }

            if (HasSyncError)
            {
                ProjectStatusDisplay = "Ошибка";
                DetailDisplay = _syncError ?? "Ошибка синхронизации";
                return;
            }

            ProjectStatusDisplay = "Нет в проекте";
            DetailDisplay = BuildNotInProjectHint(Source);
        }

        static string BuildNotInProjectHint(RevitMaterialRowDto source)
        {
            if (source == null)
                return string.Empty;

            var type = source.RevitFileType?.Trim() ?? string.Empty;
            if (string.Equals(type, "surface", StringComparison.OrdinalIgnoreCase))
                return "Surface: нужен тип с этим SR_ID в surfaces.rvt";

            if (string.Equals(type, "rfa", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(source.RevitFileUrl)
                    ? "RFA: нет файла на сервере"
                    : "RFA: ещё не синхронизирован";
            }

            if (string.Equals(type, "none", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(type))
                return "Нет Revit-файла (тип none)";

            return string.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>Строка «Нет в ТК»: конструктив пакета сравнения, которого нет ни в одной строке ТК.</summary>
    sealed class MissingMaterialRowVm
    {
        public MissingMaterialRowVm(ClientMaterialMissingRowDto source, bool hasEtalon)
        {
            RoomDisplay = Dash(source?.RoomName);
            WorkSetDisplay = Dash(TkMaterialCompareService.StripHtml(source?.WorkSetName));

            var name = TkMaterialCompareService.StripHtml(source?.KitMaterialName);
            if (source?.KitMaterialId is int materialId)
                KitMaterialDisplay = string.IsNullOrWhiteSpace(name) ? $"{materialId}" : $"{materialId} · {name}";
            else if (source?.KitMaterialSetId is int setId)
                KitMaterialDisplay = $"Набор {setId}";
            else
                KitMaterialDisplay = "—";

            KindDisplay = source?.MissingKind switch
            {
                "etalon_new" => "Есть только в эталоне: его дополнили после создания заявки",
                _ when hasEtalon => "Есть в эталоне и в подборе заявки",
                _ => "Есть в подборе заявки",
            };
        }

        public string RoomDisplay { get; }
        public string WorkSetDisplay { get; }
        public string KitMaterialDisplay { get; }
        public string KindDisplay { get; }

        static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    }

    /// <summary>Строка «Не из ТК в проекте»: SR_ID в проекте, которого нет в ТК заявки. Только показ.</summary>
    sealed class ExtraMaterialRowVm
    {
        public ExtraMaterialRowVm(ProjectExtraSrIdItem item) => Item = item;

        public ProjectExtraSrIdItem Item { get; }
        public bool IsUnused => Item.IsUnused;
        public string SrIdDisplay => Item.SrId.ToString(CultureInfo.InvariantCulture);
        public string Label => Item.Label;
        public string KindDisplay => Item.KindDisplay;
        public string UsageDisplay => Item.UsageDisplay;
    }
}
