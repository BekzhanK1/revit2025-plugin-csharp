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
                        $"Заявка: {response.ClientRequestId.Value} · Ремонт: {response.RemontId ?? _remontId}";
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

                ShowData(_rows);
                StatusTextBlock.Text = $"Материалов: {_rows.Count}";
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

        async Task SyncMaterialsAsync()
        {
            if (_syncInProgress || _rows.Count == 0)
                return;

            _syncInProgress = true;
            SyncButton.IsEnabled = false;

            foreach (var row in _rows)
                row.SyncStatusDisplay = "Ожидает";

            var rfaRows = _rows
                .Where(r => r.Source?.MaterialId != null
                            && !IsSurfaceRow(r.Source)
                            && !string.IsNullOrWhiteSpace(r.Source.RevitFileUrl))
                .ToList();

            var surfaceRows = _rows
                .Where(r => r.Source?.MaterialId != null && IsSurfaceRow(r.Source))
                .ToList();

            if (rfaRows.Count == 0 && surfaceRows.Count == 0)
            {
                StatusTextBlock.Text = "Нет файлов для синхронизации.";
                _syncInProgress = false;
                SyncButton.IsEnabled = _rows.Any(CanSyncRow);
                return;
            }

            var rowByMaterialId = _rows
                .Where(r => r.Source?.MaterialId != null)
                .ToDictionary(r => r.Source.MaterialId.Value);

            try
            {
                var downloadTotal = rfaRows.Count + (surfaceRows.Count > 0 ? 1 : 0);
                var downloadDone = 0;
                ShowSyncProgress(0, downloadTotal, $"Скачивание: 0 из {downloadTotal}");

                var progress = new Progress<(int materialId, int done, int total, bool downloading)>(update =>
                {
                    ShowSyncProgress(
                        downloadDone,
                        downloadTotal,
                        $"Скачивание: {downloadDone} из {downloadTotal}");

                    if (update.downloading &&
                        rowByMaterialId.TryGetValue(update.materialId, out var row))
                        row.SyncStatusDisplay = "Скачивается";
                });

                var downloadResults = await RevitMaterialsDownloadService
                    .SyncAsync(rfaRows.Select(r => r.Source), progress)
                    .ConfigureAwait(true);

                downloadDone = rfaRows.Count;

                string surfacesRvtPath = null;
                if (surfaceRows.Count > 0)
                {
                    foreach (var row in surfaceRows)
                        row.SyncStatusDisplay = "Ожидает";

                    var surfacesDownload = await RevitMaterialsDownloadService
                        .EnsureSurfacesLibraryAsync(_remontId, _surfacesFileUrl, _surfacesFileHash)
                        .ConfigureAwait(true);

                    downloadDone = downloadTotal;
                    ShowSyncProgress(downloadDone, downloadTotal, $"Скачивание: {downloadDone} из {downloadTotal}");

                    if (!surfacesDownload.Success)
                    {
                        foreach (var row in surfaceRows)
                        {
                            row.SyncStatusDisplay = string.IsNullOrWhiteSpace(surfacesDownload.ErrorMessage)
                                ? "Ошибка surfaces.rvt"
                                : $"Ошибка: {surfacesDownload.ErrorMessage}";
                        }
                    }
                    else
                    {
                        surfacesRvtPath = surfacesDownload.FilePath;
                        foreach (var row in surfaceRows)
                            row.SyncStatusDisplay = surfacesDownload.Skipped ? "Из кэша" : "Скачано";
                    }
                }

                var skippedCount = 0;
                var downloadedCount = 0;

                foreach (var result in downloadResults)
                {
                    if (!rowByMaterialId.TryGetValue(result.MaterialId, out var row))
                        continue;

                    if (!result.Success)
                    {
                        row.SyncStatusDisplay = string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? "Ошибка"
                            : $"Ошибка: {result.ErrorMessage}";
                        continue;
                    }

                    if (result.Skipped)
                    {
                        row.SyncStatusDisplay = "Из кэша";
                        skippedCount++;
                    }
                    else
                    {
                        row.SyncStatusDisplay = "Скачано";
                        downloadedCount++;
                    }
                }

                var importItems = downloadResults
                    .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FilePath))
                    .Select(r => (r.MaterialId, r.FilePath, r.RevitFileType))
                    .ToList();

                var importCount = importItems.Count + (surfaceRows.Count > 0 && surfacesRvtPath != null ? surfaceRows.Count : 0);
                ShowSyncProgress(importCount, importCount, "Загрузка в проект...");
                SyncProgressBar.IsIndeterminate = true;

                var importResults = RevitFamilyImportService.LoadFamiliesIntoDocument(_doc, importItems);

                List<SurfaceImportResult> surfaceImportResults = null;
                if (surfaceRows.Count > 0 && !string.IsNullOrWhiteSpace(surfacesRvtPath))
                {
                    surfaceImportResults = RevitSurfaceImportService.CopyMaterialsIntoDocument(
                        _doc,
                        surfacesRvtPath,
                        surfaceRows.Select(r => r.Source.MaterialId.Value));
                }

                SyncProgressBar.IsIndeterminate = false;

                var loadedCount = 0;
                var alreadyInProjectCount = 0;
                var skippedImportCount = 0;
                var errorCount = downloadResults.Count(r => !r.Success);

                foreach (var import in importResults)
                {
                    if (!rowByMaterialId.TryGetValue(import.MaterialId, out var row))
                        continue;

                    if (import.NotSupported)
                    {
                        row.SyncStatusDisplay = "Готово (surface, импорт не поддержан)";
                        skippedImportCount++;
                        continue;
                    }

                    if (import.Success)
                    {
                        if (import.AlreadyInProject)
                        {
                            row.SyncStatusDisplay = string.IsNullOrWhiteSpace(import.FamilyName)
                                ? "Уже в проекте"
                                : $"Уже в проекте ({import.FamilyName})";
                            alreadyInProjectCount++;
                        }
                        else
                        {
                            row.SyncStatusDisplay = string.IsNullOrWhiteSpace(import.FamilyName)
                                ? "Загружено в проект"
                                : $"Загружено ({import.FamilyName})";
                            loadedCount++;
                        }

                        continue;
                    }

                    row.SyncStatusDisplay = string.IsNullOrWhiteSpace(import.ErrorMessage)
                        ? "Ошибка загрузки"
                        : $"Ошибка загрузки: {import.ErrorMessage}";
                    errorCount++;
                }

                if (surfaceImportResults != null)
                {
                    foreach (var import in surfaceImportResults)
                    {
                        if (!rowByMaterialId.TryGetValue(import.MaterialId, out var row))
                            continue;

                        if (import.Success)
                        {
                            if (import.AlreadyInProject)
                            {
                                row.SyncStatusDisplay = string.IsNullOrWhiteSpace(import.MaterialName)
                                    ? "Уже в проекте"
                                    : $"Уже в проекте ({import.MaterialName})";
                                alreadyInProjectCount++;
                            }
                            else
                            {
                                row.SyncStatusDisplay = string.IsNullOrWhiteSpace(import.MaterialName)
                                    ? "Скопировано в проект"
                                    : $"Скопировано ({import.MaterialName})";
                                loadedCount++;
                            }

                            continue;
                        }

                        row.SyncStatusDisplay = string.IsNullOrWhiteSpace(import.ErrorMessage)
                            ? "Ошибка копирования"
                            : $"Ошибка: {import.ErrorMessage}";
                        errorCount++;
                    }
                }
                else if (surfaceRows.Count > 0 && string.IsNullOrWhiteSpace(surfacesRvtPath))
                    errorCount += surfaceRows.Count;

                StatusTextBlock.Text =
                    $"Загружено: {loadedCount} · Уже в проекте: {alreadyInProjectCount} · Из кэша: {skippedCount} · Скачано: {downloadedCount} · Без импорта: {skippedImportCount} · Ошибок: {errorCount}";
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
                MaterialTypeCodeDisplay = DisplayOrDash(row.MaterialTypeCode),
                RevitFileTypeDisplay = DisplayOrDash(row.RevitFileType),
                RevitAssetNameDisplay = DisplayOrDash(row.RevitAssetName)
            };

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
        string _syncStatusDisplay = "—";

        public RevitMaterialRowDto Source { get; init; }
        public string MaterialIdDisplay { get; init; }
        public string MaterialName { get; init; }
        public string MaterialTypeCodeDisplay { get; init; }
        public string RevitFileTypeDisplay { get; init; }
        public string RevitAssetNameDisplay { get; init; }

        public string SyncStatusDisplay
        {
            get => _syncStatusDisplay;
            set
            {
                if (_syncStatusDisplay == value)
                    return;

                _syncStatusDisplay = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
