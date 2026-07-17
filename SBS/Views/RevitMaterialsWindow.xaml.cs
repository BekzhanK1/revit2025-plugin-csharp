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
        readonly int _remontId;
        readonly Document _doc;
        bool _loadInProgress;
        bool _syncInProgress;
        string _surfacesFileUrl;
        string _surfacesFileHash;
        List<RevitMaterialRowVm> _rows = new();

        public RevitMaterialsWindow(int remontId, Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _remontId = remontId;
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
                var response = await RevitMaterialsService.ReadAsync(_remontId).ConfigureAwait(true);
                _surfacesFileUrl = response.SurfacesFileUrl?.Trim();
                _surfacesFileHash = response.SurfacesFileHash?.Trim();
                _rows = (response.Data ?? new List<RevitMaterialRowDto>())
                    .Select(ToRowVm)
                    .ToList();

                if (response.ClientRequestId.HasValue)
                {
                    ClientRequestTextBlock.Text =
                        $"Заявка #{response.ClientRequestId.Value} · Ремонт #{response.RemontId ?? _remontId}";
                    ClientRequestTextBlock.Visibility = System.Windows.Visibility.Visible;
                }
                else
                {
                    ClientRequestTextBlock.Visibility = System.Windows.Visibility.Collapsed;
                }

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
                    row.ApplyPresence(false, null);
                    continue;
                }

                if (presence.TryGetValue(row.Source.MaterialId.Value, out var info))
                    row.ApplyPresence(info.IsInProject, info.Label);
                else
                    row.ApplyPresence(false, null);
            }
        }

        void UpdateSummaryStatus()
        {
            var inProject = _rows.Count(r => r.IsInProject);
            var missing = _rows.Count - inProject;
            StatusTextBlock.Text = $"В проекте: {inProject} · Нет в проекте: {missing}";
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
                    _remontId,
                    _rows.Select(r => r.Source),
                    _surfacesFileUrl,
                    _surfacesFileHash,
                    progress).ConfigureAwait(true);

                SyncProgressBar.IsIndeterminate = false;
                RefreshProjectStatuses();
                UpdateSummaryStatus();

                if (!string.IsNullOrWhiteSpace(result.ErrorMessage)
                    && result.TotalSyncable == 0
                    && result.MaterialsLoaded == 0
                    && result.ErrorCount == 0)
                {
                    StatusTextBlock.Text = result.ErrorMessage;
                }
                else if (result.ErrorCount > 0 && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    StatusTextBlock.Text += $" · {result.ErrorMessage}";
                }
                else
                {
                    StatusTextBlock.Text += result.ErrorCount > 0
                        ? $" · Ошибок: {result.ErrorCount}"
                        : " · Синхронизация завершена";
                }
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
        string _projectStatusDisplay = "—";

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

        public void ApplyPresence(bool isInProject, string label)
        {
            IsInProject = isInProject;
            ProjectStatusDisplay = isInProject
                ? (string.IsNullOrWhiteSpace(label) ? "В проекте" : $"В проекте")
                : "Нет в проекте";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
