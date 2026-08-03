using SmartRemont.ExportRooms.DTO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class ProjectInitPreviewWindow : Window
    {
        readonly int _clientRequestId;

        public ProjectInitPreviewWindow(
            int clientRequestId,
            string targetPath,
            bool fileExists,
            RevitMaterialReadResponse materialsResponse)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _clientRequestId = clientRequestId;

            SubtitleTextBlock.Text = BuildSubtitle(clientRequestId, materialsResponse);
            TargetPathTextBlock.Text = targetPath ?? "—";

            if (fileExists)
            {
                OverwriteWarningTextBlock.Text = "Файл уже существует и будет перезаписан.";
                OverwriteWarningTextBlock.Visibility = Visibility.Visible;
            }

            var rows = BuildRows(materialsResponse);
            var stats = BuildStats(materialsResponse, rows);
            StepsTextBlock.Text = BuildStepsText(stats);
            SummaryTextBlock.Text = stats.SummaryLine;

            if (rows.Count == 0)
            {
                EmptyTextBlock.Visibility = Visibility.Visible;
                MaterialsDataGrid.Visibility = Visibility.Collapsed;
                return;
            }

            MaterialsDataGrid.ItemsSource = rows;
        }

        static string BuildSubtitle(int clientRequestId, RevitMaterialReadResponse response)
        {
            var crId = response?.ClientRequestId is > 0
                ? response.ClientRequestId.Value
                : clientRequestId;
            var remontId = response?.RemontId ?? 0;

            if (crId > 0 && remontId > 0)
                return $"Заявка #{crId} · Ремонт #{remontId}";

            if (crId > 0)
                return $"Заявка #{crId}";

            return "Проверьте список материалов перед созданием проекта.";
        }

        static List<ProjectInitPreviewRowVm> BuildRows(RevitMaterialReadResponse response)
        {
            return (response?.Data ?? new List<RevitMaterialRowDto>())
                .Select(row => new ProjectInitPreviewRowVm
                {
                    MaterialIdDisplay = row.MaterialId?.ToString(CultureInfo.InvariantCulture) ?? "—",
                    MaterialName = DisplayOrDash(row.MaterialName),
                    TypeDisplay = BuildTypeDisplay(row),
                    FileTypeDisplay = BuildFileTypeDisplay(row),
                    AssetDisplay = DisplayOrDash(row.RevitAssetName)
                })
                .ToList();
        }

        static PreviewStats BuildStats(RevitMaterialReadResponse response, List<ProjectInitPreviewRowVm> rows)
        {
            var data = response?.Data ?? new List<RevitMaterialRowDto>();
            var rfaCount = data.Count(r =>
                r.MaterialId.HasValue
                && !IsSurfaceRow(r)
                && !string.IsNullOrWhiteSpace(r.RevitFileUrl));
            var surfaceCount = data.Count(r => r.MaterialId.HasValue && IsSurfaceRow(r));
            var hasSurfacesLibrary = !string.IsNullOrWhiteSpace(response?.SurfacesFileUrl);

            return new PreviewStats
            {
                Total = rows.Count,
                RfaCount = rfaCount,
                SurfaceCount = surfaceCount,
                HasSurfacesLibrary = hasSurfacesLibrary,
                SummaryLine =
                    $"Всего: {rows.Count} · RFA: {rfaCount} · Surface: {surfaceCount}"
                    + (hasSurfacesLibrary ? " · surfaces.rvt: да" : " · surfaces.rvt: нет")
            };
        }

        string BuildStepsText(PreviewStats stats) =>
            "Будет выполнено:\n"
            + $"• SaveAs копии проекта\n"
            + $"• Запись client_request_id #{_clientRequestId} в модель\n"
            + $"• Загрузка материалов: {stats.Total} шт. (RFA: {stats.RfaCount}, surface: {stats.SurfaceCount})\n"
            + $"• Библиотека surfaces.rvt: {(stats.HasSurfacesLibrary ? "да" : "нет")}";

        static bool IsSurfaceRow(RevitMaterialRowDto row) =>
            string.Equals(row?.RevitFileType?.Trim(), "surface", StringComparison.OrdinalIgnoreCase);

        static string BuildTypeDisplay(RevitMaterialRowDto row)
        {
            if (!string.IsNullOrWhiteSpace(row?.MaterialTypeCode))
                return row.MaterialTypeCode.Trim();

            if (!string.IsNullOrWhiteSpace(row?.RevitFileType))
                return row.RevitFileType.Trim();

            return "—";
        }

        static string BuildFileTypeDisplay(RevitMaterialRowDto row)
        {
            if (IsSurfaceRow(row))
                return "surface";

            if (!string.IsNullOrWhiteSpace(row?.RevitFileType))
                return row.RevitFileType.Trim();

            return string.IsNullOrWhiteSpace(row?.RevitFileUrl) ? "—" : "rfa";
        }

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        void InitButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        sealed class PreviewStats
        {
            public int Total { get; init; }
            public int RfaCount { get; init; }
            public int SurfaceCount { get; init; }
            public bool HasSurfacesLibrary { get; init; }
            public string SummaryLine { get; init; }
        }
    }

    sealed class ProjectInitPreviewRowVm
    {
        public string MaterialIdDisplay { get; init; }
        public string MaterialName { get; init; }
        public string TypeDisplay { get; init; }
        public string FileTypeDisplay { get; init; }
        public string AssetDisplay { get; init; }
    }
}
