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
        List<RevitMaterialRowVm> _rows = new();

        public RevitMaterialsWindow(int clientRequestId, Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
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
                _rows = (response.Data ?? new List<RevitMaterialRowDto>())
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
                    StatusTextBlock.Text = string.Empty;
                    return;
                }

                RefreshProjectStatuses();
                ShowData(_rows);
                UpdateSummaryStatus();
                SyncButton.IsEnabled = _rows.Any(CanSyncRow);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Revit materials read failed");
                ShowError(ex.Message);
                StatusTextBlock.Text = string.Empty;
            }
            finally
            {
                _loadInProgress = false;
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
                    row.ApplyPresence(false);
                    continue;
                }

                if (presence.TryGetValue(row.Source.MaterialId.Value, out var info))
                    row.ApplyPresence(info.IsInProject);
                else
                    row.ApplyPresence(false);
            }
        }

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
            MaterialsDataGrid.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ShowEmpty()
        {
            LoadingPanel.Visibility = System.Windows.Visibility.Collapsed;
            EmptyTextBlock.Visibility = System.Windows.Visibility.Visible;
            ErrorPanel.Visibility = System.Windows.Visibility.Collapsed;
            MaterialsDataGrid.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ShowError(string message)
        {
            LoadingPanel.Visibility = System.Windows.Visibility.Collapsed;
            EmptyTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            ErrorPanel.Visibility = System.Windows.Visibility.Visible;
            ErrorTextBlock.Text = message;
            MaterialsDataGrid.Visibility = System.Windows.Visibility.Collapsed;
        }

        void ShowData(List<RevitMaterialRowVm> rows)
        {
            LoadingPanel.Visibility = System.Windows.Visibility.Collapsed;
            EmptyTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            ErrorPanel.Visibility = System.Windows.Visibility.Collapsed;
            MaterialsDataGrid.ItemsSource = rows;
            MaterialsDataGrid.Visibility = System.Windows.Visibility.Visible;
        }

        static bool IsSurfaceRow(RevitMaterialRowDto row) =>
            string.Equals(row?.RevitFileType?.Trim(), "surface", StringComparison.OrdinalIgnoreCase);

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

        public void ApplyPresence(bool isInProject)
        {
            IsInProject = isInProject;
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
}
