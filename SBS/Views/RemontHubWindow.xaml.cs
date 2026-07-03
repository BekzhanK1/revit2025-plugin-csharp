using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public partial class RemontHubWindow : Window
    {
        readonly Document _doc;

        const string InitProjectSubtitle = "Копия RVT, remont_id в модели, все материалы";
        const string DsAreaSubtitle = "Отправка площадей помещений в Smart Remont";
        const string MeasuresSubtitle = "Отправка замеров из ведомостей Revit";
        const string MeasuresFromCodeSubtitle = "Площадь стен из модели Revit";
        const string MeasuresCompareSubtitle = "Спецификация и код — в одной таблице с подсветкой";
        const string RoomMaterialsSubtitle = "ДС на изменение технологической карты";
        const string RevitMaterialsSubtitle = "Загрузка RFA и surface-типов из Smart Remont";
        const string TypeParametersSubtitle = "ID материала и ID типа материала выбранного типа";

        bool _initInProgress;

        public RemontHubWindow(Document doc)
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(CompanyLogoImage);
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            ApplyHubMenuVisibility(ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc));
            Loaded += RemontHubWindow_Loaded;
            Closing += (_, _) =>
            {
                // Гарантируем Result.Succeeded, чтобы Revit не откатил транзакции сессии.
                if (DialogResult == null)
                    DialogResult = true;
            };
        }

        async void RemontHubWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetupFeatureButtons();
            BindRemontInfo(ExportRoomsApplication.SelectedRemont);
            RefreshProjectInitState();
            await EnrichSelectedRemontIfNeededAsync().ConfigureAwait(true);
            await RefreshEventStatusesAsync().ConfigureAwait(true);
            RefreshProjectInitState();
        }

        async Task EnrichSelectedRemontIfNeededAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont?.RemontId is not int remontId || remontId <= 0)
                return;

            var placeholder = $"Ремонт #{remontId}";
            if (!string.IsNullOrWhiteSpace(remont.Name)
                && !string.Equals(remont.Name.Trim(), placeholder, StringComparison.Ordinal))
                return;

            await ProjectRemontBindingService.TryEnrichFromQuickSearchAsync(remont).ConfigureAwait(true);
            BindRemontInfo(remont);
        }
        void SetupFeatureButtons()
        {
            ConfigureFeatureButton(InitProjectButton, "\uE8C8", InitProjectSubtitle);
            ConfigureFeatureButton(RevitMaterialsButton, "\uE7B8", RevitMaterialsSubtitle);
            ConfigureFeatureButton(DsAreaChangeButton, "\uE8A7", DsAreaSubtitle);
            ConfigureFeatureButton(MeasuresButton, "\uE8B7", MeasuresSubtitle);
            ConfigureFeatureButton(RoomMaterialsButton, "\uE719", RoomMaterialsSubtitle);
            // TODO: plugin-ui-redesign — Замеры по коду
            MeasuresFromCodeButton.Visibility = System.Windows.Visibility.Collapsed;
            ConfigureFeatureButton(MeasuresFromCodeButton, "\uE8F1", MeasuresFromCodeSubtitle);
            // TODO: plugin-ui-redesign — Сравнение замеров
            MeasuresCompareButton.Visibility = System.Windows.Visibility.Collapsed;
            ConfigureFeatureButton(MeasuresCompareButton, "\uE8AB", MeasuresCompareSubtitle);
            // TODO: plugin-ui-redesign — Изменение параметров типов
            TypeParametersButton.Visibility = System.Windows.Visibility.Collapsed;
            ConfigureFeatureButton(TypeParametersButton, "\uE8B9", TypeParametersSubtitle);
        }

        static void ConfigureFeatureButton(Button button, string iconGlyph, string subtitle)
        {
            button.ApplyTemplate();
            if (button.Template.FindName("FeatureIcon", button) is TextBlock icon)
                icon.Text = iconGlyph;
            if (button.Template.FindName("FeatureSubtitle", button) is TextBlock sub)
                sub.Text = subtitle;
        }

        async Task RefreshEventStatusesAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont?.RemontId == null || remont.RemontId <= 0)
            {
                RevitEventStatusUi.ApplyFeatureBadge(DsAreaChangeButton, null);
                RevitEventStatusUi.ApplyFeatureBadge(MeasuresButton, null);
                return;
            }

            var remontId = remont.RemontId.Value;
            RevitEventStatusDataDto dsStatus = null;
            RevitEventStatusDataDto measuresStatus = null;

            try
            {
                var dsTask = RevitEventsService.GetStatusAsync(remontId, RevitEventTypes.DsAreaChange);
                var measuresTask = RevitEventsService.GetStatusAsync(remontId, RevitEventTypes.Measures);
                await Task.WhenAll(dsTask, measuresTask).ConfigureAwait(true);
                dsStatus = dsTask.Result;
                measuresStatus = measuresTask.Result;
            }
            catch (System.Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось загрузить статус revit_events");
            }

            ApplyFeatureStatus(DsAreaChangeButton, "\uE8A7", DsAreaSubtitle, dsStatus);
            ApplyFeatureStatus(MeasuresButton, "\uE8B7", MeasuresSubtitle, measuresStatus);
        }

        static void ApplyFeatureStatus(
            Button button,
            string iconGlyph,
            string baseSubtitle,
            RevitEventStatusDataDto status)
        {
            ConfigureFeatureButton(button, iconGlyph, baseSubtitle);

            var suffix = RevitEventStatusFormatter.FormatSubtitleSuffix(status);
            if (!string.IsNullOrEmpty(suffix) &&
                button.Template.FindName("FeatureSubtitle", button) is TextBlock sub)
                sub.Text = baseSubtitle + " · " + suffix;

            RevitEventStatusUi.ApplyFeatureBadge(button, status);
        }

        void BindRemontInfo(RemontOption remont)
        {
            if (remont == null)
            {
                RemontIdHeroText.Text = "Ремонт #—";
                ClientRequestIdHeroText.Text = "Заявка #—";
                RemontNameText.Text = string.Empty;
                RemontNameText.Visibility = System.Windows.Visibility.Collapsed;
                ClientNameText.Text = "—";
                ResidentNameText.Text = "—";
                FlatNumText.Text = "—";
                PresetNameText.Text = "—";
                UpdateProjectInitializedBadge(null);
                return;
            }

            RemontIdHeroText.Text = remont.RemontId.HasValue && remont.RemontId.Value > 0
                ? $"Ремонт #{remont.RemontId.Value}"
                : "Ремонт #—";
            ClientRequestIdHeroText.Text = remont.ClientRequestId > 0
                ? $"Заявка #{remont.ClientRequestId}"
                : "Заявка #—";

            var name = BuildSubtitle(remont);
            if (string.IsNullOrEmpty(name))
            {
                RemontNameText.Text = string.Empty;
                RemontNameText.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                RemontNameText.Text = name;
                RemontNameText.Visibility = System.Windows.Visibility.Visible;
            }

            ClientNameText.Text = DisplayOrDash(remont.ClientName);
            ResidentNameText.Text = DisplayOrDash(remont.ResidentName);
            FlatNumText.Text = DisplayOrDash(remont.FlatNum);
            PresetNameText.Text = DisplayOrDash(remont.PresetName);

            var metadata = ProjectRemontMetadataService.TryRead(_doc);
            UpdateProjectInitializedBadge(
                ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc) ? metadata : null);
        }

        void UpdateProjectInitializedBadge(ProjectRemontMetadata metadata)
        {
            if (metadata == null || metadata.RemontId <= 0)
            {
                ProjectInitializedBadge.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            ProjectInitializedBadge.Visibility = System.Windows.Visibility.Visible;
            ProjectInitializedBadgeText.Text = $"Проект инициализирован · #{metadata.RemontId}";
        }

        static string BuildSubtitle(RemontOption remont) =>
            string.IsNullOrWhiteSpace(remont?.Name) ? string.Empty : remont.Name.Trim();

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        void RefreshProjectInitState()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var selectedRemontId = remont?.RemontId ?? remont?.Id ?? 0;
            var isInitialized = ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc);

            ApplyHubMenuVisibility(isInitialized);

            if (!isInitialized)
            {
                ApplyInitFeatureBadge(InitProjectButton, null);
                InitProjectButton.IsEnabled = !_initInProgress && selectedRemontId > 0;
                return;
            }

            var metadata = ProjectRemontMetadataService.TryRead(_doc);
            ApplyInitFeatureBadge(InitProjectButton, metadata?.RemontId);

            if (selectedRemontId > 0 && metadata != null && metadata.RemontId != selectedRemontId)
            {
                SetStatus(
                    $"Проект привязан к ремонту #{metadata.RemontId}. Выбран ремонт #{selectedRemontId}.",
                    isSuccess: false);
            }
        }

        void ApplyHubMenuVisibility(bool isInitialized)
        {
            InitProjectButton.Visibility = isInitialized
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            var workButtons = new[]
            {
                RevitMaterialsButton,
                DsAreaChangeButton,
                MeasuresButton,
                RoomMaterialsButton
            };

            foreach (var button in workButtons)
            {
                button.Visibility = isInitialized
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }

            FunctionsSectionLabel.Text = isInitialized ? "ФУНКЦИИ" : "ИНИЦИАЛИЗАЦИЯ";
        }

        static void ApplyInitFeatureBadge(Button button, int? remontId)
        {
            button.ApplyTemplate();

            var badge = button.Template.FindName("SentBadge", button) as Border;
            var badgeText = button.Template.FindName("SentBadgeText", button) as TextBlock;
            if (badge == null || badgeText == null)
                return;

            if (remontId == null || remontId <= 0)
            {
                badge.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            badge.Visibility = System.Windows.Visibility.Visible;
            badgeText.Text = $"Инициализирован #{remontId.Value}";
            badge.Background = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#DCFCE7"));
            badge.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#BBF7D0"));
            badgeText.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#166534"));
        }

        static string ResolveResidentName(RemontOption remont)
        {
            if (!string.IsNullOrWhiteSpace(remont?.ResidentName))
                return remont.ResidentName.Trim();

            if (!string.IsNullOrWhiteSpace(remont?.Name))
                return remont.Name.Trim();

            return null;
        }

        async void InitProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_initInProgress)
                return;

            var remont = ExportRoomsApplication.SelectedRemont;
            var remontId = remont?.RemontId ?? remont?.Id ?? 0;
            if (remontId <= 0)
            {
                SetStatus("Не указан ID ремонта", isSuccess: false);
                return;
            }

            if (ProjectRemontMetadataService.IsInitialized(_doc)
                && !ProjectRemontMetadataService.ValidateMatches(_doc, remontId))
            {
                var existing = ProjectRemontMetadataService.TryRead(_doc);
                AppMessageDialog.Show(
                    this,
                    AppMessageKind.InDevelopment,
                    "Нельзя инициализировать",
                    $"Проект уже привязан к ремонту #{existing?.RemontId}.",
                    $"Выбран ремонт #{remontId}. Откройте другой файл или выберите соответствующий ремонт.");
                return;
            }

            var targetPath = ProjectFileNamingService.BuildFullPath(remontId, ResolveResidentName(remont));
            var fileExists = File.Exists(targetPath);

            SetStatus("Загрузка списка материалов...", isSuccess: true);

            RevitMaterialReadResponse materialsResponse;
            try
            {
                materialsResponse = await RevitMaterialsService.ReadAsync(remontId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project init preview: materials read failed");
                SetStatus("Не удалось загрузить материалы: " + ex.Message, isSuccess: false);
                MessageBox.Show(
                    ex.Message,
                    "Ошибка загрузки материалов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var preview = new ProjectInitPreviewWindow(remontId, targetPath, fileExists, materialsResponse)
            {
                Owner = this
            };

            if (preview.ShowDialog() != true)
            {
                SetStatus(string.Empty, isSuccess: true);
                return;
            }

            _initInProgress = true;
            InitProjectButton.IsEnabled = false;
            SetStatus("Подготовка к инициализации...", isSuccess: true);

            var progress = new Progress<string>(message => SetStatus(message, isSuccess: true));

            ProjectInitResult result;
            try
            {
                result = await ProjectInitService.InitializeProjectAsync(
                    _doc,
                    remont,
                    overwriteExistingFile: fileExists,
                    progress,
                    materialsResponse).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init failed");
                SetStatus("Ошибка инициализации: " + ex.Message, isSuccess: false);
                _initInProgress = false;
                RefreshProjectInitState();
                return;
            }

            _initInProgress = false;
            RefreshProjectInitState();

            if (result.RemontConflict)
            {
                AppMessageDialog.Show(
                    this,
                    AppMessageKind.InDevelopment,
                    "Нельзя инициализировать",
                    result.ErrorMessage);
                SetStatus(result.ErrorMessage, isSuccess: false);
                return;
            }

            if (!result.Success)
            {
                if (result.FileAlreadyExists && !fileExists)
                {
                    SetStatus(result.ErrorMessage ?? "Файл уже существует", isSuccess: false);
                    return;
                }

                SetStatus(result.ErrorMessage ?? "Инициализация не удалась", isSuccess: false);
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    MessageBox.Show(
                        result.ErrorMessage,
                        "Ошибка инициализации",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            var details = BuildInitSuccessDetails(result);
            ProjectPostInitExitService.RequestShutdownRevitAfterPluginExit(result.NewFilePath);
            AppMessageDialog.ShowSuccess(
                this,
                "Проект инициализирован",
                $"Загружено материалов: {result.MaterialsLoaded}",
                details,
                buttonText: "Закрыть");

            DialogResult = true;
            Close();
        }

        string BuildInitSuccessDetails(ProjectInitResult result)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(result.NewFilePath))
                lines.Add(result.NewFilePath);

            if (result.IsWorksharedWarning)
                lines.Add(ProjectCopyService.WorksharedUnsupportedMessage);

            lines.Add("Нажмите «Закрыть» — Revit завершит работу.");
            lines.Add("Затем откройте сохранённый файл вручную через Файл → Открыть.");

            return string.Join("\n\n", lines);
        }

        async void DsAreaChangeButton_Click(object sender, RoutedEventArgs e)
        {
            var summaryWindow = new SelectedRemontSummaryWindow(_doc);
            summaryWindow.Owner = this;
            summaryWindow.ShowDialog();

            if (summaryWindow.DialogResult == true)
                SetStatus(summaryWindow.LastSuccessMessage ?? "Площади отправлены", isSuccess: true);

            await RefreshEventStatusesAsync().ConfigureAwait(true);
        }

        void SetStatus(string message, bool isSuccess)
        {
            if (isSuccess)
            {
                StatusBanner.Visibility = System.Windows.Visibility.Visible;
                StatusPlainHost.Visibility = System.Windows.Visibility.Collapsed;
                StatusTextBlock.Text = message;
            }
            else
            {
                StatusBanner.Visibility = System.Windows.Visibility.Collapsed;
                StatusPlainHost.Visibility = System.Windows.Visibility.Visible;
                StatusPlainText.Text = message;
            }
        }

        async void MeasuresButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new RoomMeasurementsWindow(_doc);
            window.Owner = this;
            window.ShowDialog();

            if (window.DialogResult == true)
                SetStatus(window.LastSuccessMessage ?? "Замеры отправлены", isSuccess: true);

            await RefreshEventStatusesAsync().ConfigureAwait(true);
        }

        async void MeasuresFromCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new RoomMeasurementsFromCodeWindow(_doc);
            window.Owner = this;
            window.ShowDialog();

            if (window.DialogResult == true)
                SetStatus(window.LastSuccessMessage ?? "Замеры по коду отправлены", isSuccess: true);

            await RefreshEventStatusesAsync().ConfigureAwait(true);
        }

        void MeasuresCompareButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new RoomMeasurementsCompareWindow(_doc);
            window.Owner = this;
            window.ShowDialog();
        }

        void RoomMaterialsButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new RoomMaterialsWindow(_doc);
            window.Owner = this;
            window.ShowDialog();
        }

        void RevitMaterialsButton_Click(object sender, RoutedEventArgs e)
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont?.RemontId == null || remont.RemontId <= 0)
            {
                SetStatus("Не указан ID ремонта", isSuccess: false);
                return;
            }

            var window = new RevitMaterialsWindow(remont.RemontId.Value, _doc);
            window.Owner = this;
            window.ShowDialog();
        }

        void TypeParametersButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new TypeParameterChangeWindow(_doc);
            window.Owner = this;
            window.ShowDialog();
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Всегда возвращаем true, чтобы команда вернула Result.Succeeded.
            // Result.Cancelled откатывает все транзакции сессии, включая уже закоммиченные.
            DialogResult = true;
            Close();
        }
    }
}
