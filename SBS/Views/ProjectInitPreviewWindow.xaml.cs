using Autodesk.Revit.DB;

using SmartRemont.ExportRooms.DTO;

using SmartRemont.ExportRooms.Services;

using System;

using System.Collections.Generic;

using System.Globalization;

using System.Linq;

using System.Text;

using System.Threading;

using System.Threading.Tasks;

using System.Windows;



namespace SmartRemont.ExportRooms.Views

{

    public partial class ProjectInitPreviewWindow : Window

    {

        readonly int _clientRequestId;

        readonly Document _doc;

        readonly RevitMaterialReadResponse _materialsResponse;

        readonly CancellationTokenSource _preflightCts = new CancellationTokenSource();

        /// <summary>Пользователь подтвердил init несмотря на ошибки preflight (SR_ID / surfaces).</summary>
        public bool PreflightValidationIgnored { get; private set; }

        public ProjectInitPreviewWindow(

            Document doc,

            int clientRequestId,

            string targetPath,

            bool fileExists,

            RevitMaterialReadResponse materialsResponse)

        {

            InitializeComponent();

            WindowLayoutHelper.UseFullWorkArea(this);

            _doc = doc;

            _clientRequestId = clientRequestId;

            _materialsResponse = materialsResponse;



            SubtitleTextBlock.Text = BuildSubtitle(clientRequestId, materialsResponse);

            TargetPathTextBlock.Text = targetPath ?? "—";



            if (fileExists)

            {

                OverwriteWarningTextBlock.Text = "Файл уже существует и будет перезаписан.";

                OverwriteWarningTextBlock.Visibility = System.Windows.Visibility.Visible;

            }



            var groups = BuildGroups(materialsResponse);

            var stats = BuildStats(materialsResponse, groups);

            StepsTextBlock.Text = BuildStepsText(stats);

            SummaryTextBlock.Text = stats.SummaryLine;



            var syncable = ProjectInitMaterialsPreflightService.CountSyncableMaterials(materialsResponse?.Data);

            if (syncable <= 0)

            {

                EmptyTextBlock.Text = ProjectInitMaterialsPreflightService.BuildZeroSyncableMessage();

                EmptyTextBlock.Visibility = System.Windows.Visibility.Visible;

                MaterialGroupsItemsControl.Visibility = System.Windows.Visibility.Collapsed;

                InitButton.IsEnabled = false;

                PreDownloadStatusTextBlock.Text = "Нет материалов для загрузки в Revit.";

                return;

            }



            if (groups.Count == 0)

            {

                EmptyTextBlock.Visibility = System.Windows.Visibility.Visible;

                MaterialGroupsItemsControl.Visibility = System.Windows.Visibility.Collapsed;

            }

            else

            {

                MaterialGroupsItemsControl.ItemsSource = groups;

            }



            InitButton.IsEnabled = false;

            PreDownloadStatusTextBlock.Text = "Подготовка: загрузка файлов в кэш…";



            RevitMaterialsSyncOrchestrator.StartBackgroundPreDownload(

                clientRequestId,

                materialsResponse?.Data,

                materialsResponse?.SurfacesFileUrl,

                materialsResponse?.SurfacesFileHash);



            Loaded += async (_, _) => await RunPreflightAsync().ConfigureAwait(true);

            Closed += (_, _) => _preflightCts.Cancel();

        }



        async Task RunPreflightAsync()

        {

            try

            {

                var progress = new Progress<string>(message =>

                {

                    if (!string.IsNullOrWhiteSpace(message))

                        PreDownloadStatusTextBlock.Text = message;

                });



                var result = await ProjectInitMaterialsPreflightService.RunAsync(

                    _doc,

                    _materialsResponse,

                    _clientRequestId,

                    progress,

                    _preflightCts.Token).ConfigureAwait(true);



                PreDownloadStatusTextBlock.Text =

                    $"Кэш RFA: {result.DownloadReadyCount} из {RevitMaterialsSyncOrchestrator.CountSyncableMaterials(_materialsResponse?.Data)} готово";



                if (result.Issues.Count > 0)
                {
                    ValidationWarningBorder.Visibility = System.Windows.Visibility.Visible;
                    ValidationIssuesTextBlock.Text = BuildIssuesText(result.Issues);
                    InitButton.IsEnabled = false;
                    IgnoreValidationButton.Visibility = System.Windows.Visibility.Visible;
                    return;
                }



                InitButton.IsEnabled = result.CanInit;

                if (!result.CanInit)

                    PreDownloadStatusTextBlock.Text = ProjectInitMaterialsPreflightService.BuildZeroSyncableMessage();

            }

            catch (OperationCanceledException)

            {

                // window closed

            }

            catch (Exception ex)

            {

                ExportRoomsApplication._logger?.Warning(ex, "Project init preview preflight failed");

                ValidationWarningBorder.Visibility = System.Windows.Visibility.Visible;

                ValidationIssuesTextBlock.Text = "Не удалось выполнить проверку: " + ex.Message;

                InitButton.IsEnabled = false;

            }

        }



        static string BuildIssuesText(IReadOnlyList<ProjectInitPreflightIssue> issues)

        {

            var sb = new StringBuilder();

            sb.AppendLine("Исправьте проблемы до инициализации:");

            foreach (var issue in issues.Take(12))

            {

                sb.Append("• #").Append(issue.MaterialId).Append(": ").AppendLine(issue.Message);

            }



            if (issues.Count > 12)
                sb.AppendLine($"... и ещё {issues.Count - 12}");



            return sb.ToString().TrimEnd();

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



        static List<ProjectInitPreviewGroupVm> BuildGroups(RevitMaterialReadResponse response)

        {

            var rowsByCategory = (response?.Data ?? new List<RevitMaterialRowDto>())

                .Select(BuildRow)

                .GroupBy(r => r.CategoryKey)

                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.MaterialIdSort).ToList());



            var groups = new List<ProjectInitPreviewGroupVm>();

            foreach (var definition in GroupDefinitions)

            {

                if (!rowsByCategory.TryGetValue(definition.Key, out var rows) || rows.Count == 0)

                    continue;



                groups.Add(new ProjectInitPreviewGroupVm

                {

                    CategoryKey = definition.Key,

                    Title = definition.Title,

                    Description = definition.Description,

                    SummaryBadge = $"{rows.Count} шт.",

                    Rows = rows

                });

            }



            return groups;

        }



        static ProjectInitPreviewRowVm BuildRow(RevitMaterialRowDto row)

        {

            var category = Classify(row);

            return new ProjectInitPreviewRowVm

            {

                CategoryKey = category,

                MaterialIdSort = row?.MaterialId ?? int.MaxValue,

                MaterialIdDisplay = row?.MaterialId?.ToString(CultureInfo.InvariantCulture) ?? "—",

                MaterialName = DisplayOrDash(row?.MaterialName),

                DetailDisplay = BuildDetailDisplay(row, category)

            };

        }



        static InitPreviewMaterialCategory Classify(RevitMaterialRowDto row)

        {

            var type = row?.RevitFileType?.Trim() ?? string.Empty;

            if (string.Equals(type, "surface", StringComparison.OrdinalIgnoreCase))

                return InitPreviewMaterialCategory.Surface;



            if (string.Equals(type, "no_model", StringComparison.OrdinalIgnoreCase))

                return InitPreviewMaterialCategory.NoModel;



            if (string.Equals(type, "none", StringComparison.OrdinalIgnoreCase)

                || string.IsNullOrWhiteSpace(type))

            {

                return InitPreviewMaterialCategory.MissingMarkup;

            }



            if (string.Equals(type, "rfa", StringComparison.OrdinalIgnoreCase))

            {

                return string.IsNullOrWhiteSpace(row.RevitFileUrl)

                    ? InitPreviewMaterialCategory.NoModel

                    : InitPreviewMaterialCategory.Model3D;

            }



            return InitPreviewMaterialCategory.MissingMarkup;

        }



        static string BuildDetailDisplay(RevitMaterialRowDto row, InitPreviewMaterialCategory category)

        {

            var type = row?.RevitFileType?.Trim() ?? string.Empty;



            switch (category)

            {

                case InitPreviewMaterialCategory.Model3D:

                    return string.IsNullOrWhiteSpace(row?.RevitAssetName)

                        ? "RFA на сервере"

                        : row.RevitAssetName.Trim();

                case InitPreviewMaterialCategory.Surface:

                    return string.IsNullOrWhiteSpace(row?.MaterialTypeCode)

                        ? "Тип в surfaces.rvt"

                        : row.MaterialTypeCode.Trim();

                case InitPreviewMaterialCategory.NoModel:

                    if (string.Equals(type, "no_model", StringComparison.OrdinalIgnoreCase))

                        return "revit_file_type = no_model — 3D не требуется";

                    return "RFA без файла на сервере";

                case InitPreviewMaterialCategory.MissingMarkup:

                    return string.IsNullOrWhiteSpace(type)

                        ? "revit_file_type не задан"

                        : string.Equals(type, "none", StringComparison.OrdinalIgnoreCase)

                            ? "revit_file_type = none — разметка не назначена"

                            : $"revit_file_type = {type}";

                default:

                    return string.Empty;

            }

        }



        static PreviewStats BuildStats(RevitMaterialReadResponse response, List<ProjectInitPreviewGroupVm> groups)

        {

            var counts = groups.ToDictionary(

                g => g.CategoryKey,

                g => g.Rows.Count);



            int Count(InitPreviewMaterialCategory key) =>

                counts.TryGetValue(key, out var value) ? value : 0;



            var model3D = Count(InitPreviewMaterialCategory.Model3D);

            var surface = Count(InitPreviewMaterialCategory.Surface);

            var noModel = Count(InitPreviewMaterialCategory.NoModel);

            var missingMarkup = Count(InitPreviewMaterialCategory.MissingMarkup);

            var total = model3D + surface + noModel + missingMarkup;

            var hasSurfacesLibrary = !string.IsNullOrWhiteSpace(response?.SurfacesFileUrl);

            var syncable = ProjectInitMaterialsPreflightService.CountSyncableMaterials(response?.Data);



            return new PreviewStats

            {

                Total = total,

                Model3DCount = model3D,

                SurfaceCount = surface,

                NoModelCount = noModel,

                MissingMarkupCount = missingMarkup,

                HasSurfacesLibrary = hasSurfacesLibrary,

                SyncableCount = syncable,

                SummaryLine =

                    $"К загрузке в Revit: {syncable} · 3D: {model3D} · surface: {surface} · "

                    + $"нет модели: {noModel} · без разметки: {missingMarkup}"

                    + (hasSurfacesLibrary ? " · surfaces.rvt: да" : " · surfaces.rvt: нет")

            };

        }



        string BuildStepsText(PreviewStats stats) =>

            "Будет выполнено:\n"

            + $"• SaveAs копии проекта\n"

            + $"• Запись client_request_id #{_clientRequestId} в модель\n"

            + $"• Strict-загрузка материалов: {stats.SyncableCount} шт. "

            + $"(3D: {stats.Model3DCount}, surface: {stats.SurfaceCount})\n"

            + $"• Библиотека surfaces.rvt: {(stats.HasSurfacesLibrary ? "да" : "нет")}";



        static string DisplayOrDash(string value) =>

            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();



        void InitButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        void IgnoreValidationButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                this,
                "Обнаружены проблемы с SR_ID или surfaces.rvt — часть материалов может не загрузиться.\n\n"
                + "Вы уверены, что хотите продолжить инициализацию?",
                "Продолжить без исправления?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
                return;

            PreflightValidationIgnored = true;
            DialogResult = true;
            Close();
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }



        static readonly (InitPreviewMaterialCategory Key, string Title, string Description)[] GroupDefinitions =

        {

            (

                InitPreviewMaterialCategory.Model3D,

                "3D-модель",

                "Семейства RFA с файлом на сервере — будут загружены в проект."

            ),

            (

                InitPreviewMaterialCategory.Surface,

                "Поверхность",

                "Surface-типы из surfaces.rvt по SR_ID."

            ),

            (

                InitPreviewMaterialCategory.NoModel,

                "Нет модели",

                "no_model или RFA без файла — в Revit не загружаются, но могут быть в ТК."

            ),

            (

                InitPreviewMaterialCategory.MissingMarkup,

                "Отсутствует разметка",

                "revit_file_type = none или не задан — разметка Revit ещё не проставлена."

            )

        };



        sealed class PreviewStats

        {

            public int Total { get; init; }

            public int Model3DCount { get; init; }

            public int SurfaceCount { get; init; }

            public int NoModelCount { get; init; }

            public int MissingMarkupCount { get; init; }

            public int SyncableCount { get; init; }

            public bool HasSurfacesLibrary { get; init; }

            public string SummaryLine { get; init; }

        }

    }



    enum InitPreviewMaterialCategory

    {

        Model3D,

        Surface,

        NoModel,

        MissingMarkup

    }



    sealed class ProjectInitPreviewGroupVm

    {

        public InitPreviewMaterialCategory CategoryKey { get; init; }

        public string Title { get; init; }

        public string Description { get; init; }

        public string SummaryBadge { get; init; }

        public List<ProjectInitPreviewRowVm> Rows { get; init; }

    }



    sealed class ProjectInitPreviewRowVm

    {

        public InitPreviewMaterialCategory CategoryKey { get; init; }

        public int MaterialIdSort { get; init; }

        public string MaterialIdDisplay { get; init; }

        public string MaterialName { get; init; }

        public string DetailDisplay { get; init; }

    }

}


