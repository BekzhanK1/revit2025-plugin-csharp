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

        const string InitProjectSubtitle = "Копия RVT по заявке, метаданные и материалы";
        const string DsAreaSubtitle = "Отправка площадей помещений в Smart Remont";
        const string MeasuresSubtitle = "Отправка замеров из ведомостей Revit";
        const string MeasuresFromCodeSubtitle = "Площадь стен из модели Revit";
        const string MeasuresCompareSubtitle = "Спецификация и код — в одной таблице с подсветкой";
        const string RoomMaterialsSubtitle = "Сверка материалов с технологической картой";
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
            RefreshProjectInitState();
            
            if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
            {
                await FetchAsyncStates().ConfigureAwait(true);
            }
        }

        async Task FetchAsyncStates()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var clientRequestId = remont?.ClientRequestId ?? 0;
            if (clientRequestId <= 0) return;

            SetStatus("Обновление статусов...", true);
            
            var dsTask = DsRoomChangeService.TryReadAsync(clientRequestId);
            var measuresTask = MeasuresService.TryReadAsync(clientRequestId);
            var materialsTask = RevitMaterialsService.TryReadAsync(clientRequestId);

            await Task.WhenAll(dsTask, measuresTask, materialsTask).ConfigureAwait(true);

            var ds = await dsTask;
            var measures = await measuresTask;
            var materials = await materialsTask;

            // Apply Tk (Not Implemented)
            ApplyBadge(RoomMaterialsButton, "🚧 Скоро", "#F8FAFC", "#94A3B8");
            RoomMaterialsButton.IsEnabled = false;

            // Apply Materials
            ApplyMaterialsState(materials.Data, materials.Status, materials.Error, clientRequestId);

            // Apply Measures
            ApplyMeasuresState(measures.Data, measures.Status, measures.Error);

            // Apply DS
            var resolvedRemontId = remont?.RemontId ?? ds.RemontId;
            ApplyDsState(ds.Data, ds.Status, resolvedRemontId);
            
            SetStatus(string.Empty, true);
        }

        void ApplyMaterialsState(RevitMaterialReadResponse data, bool status, string error, int clientRequestId)
        {
            if (!status)
            {
                ApplyBadge(RevitMaterialsButton, $"× Ошибка: {error}", "#FEF2F2", "#DC2626");
                return;
            }
            
            var lastSync = LocalSettingsService.GetLastMaterialSyncTime(clientRequestId);
            var timeStr = lastSync.HasValue ? $" · {lastSync.Value:HH:mm}" : "";

            if (data?.Data == null || data.Data.Count == 0)
            {
                ApplyBadge(RevitMaterialsButton, "Не синхронизировано", "#F1F5F9", "#475569");
            }
            else
            {
                ApplyBadge(RevitMaterialsButton, $"✔ Синхронизировано{timeStr}", "#DCFCE7", "#166534");
            }
        }

        void ApplyMeasuresState(System.Collections.Generic.List<SmartRemont.ExportRooms.DTO.MeasureRoomInfoDto> data, bool status, string error)
        {
            if (!status && (error?.Contains("планировк") == true || error?.Contains("plan") == true))
            {
                ApplyBadge(MeasuresButton, "Нет планировки", "#F1F5F9", "#475569", "У заявки нет планировки");
                MeasuresButton.IsEnabled = false;
                return;
            }
            
            RoomMeasurementsSnapshot snapshot;
            try
            {
                snapshot = RoomMeasurementsService.Collect(_doc);
            }
            catch (Exception ex)
            {
                ApplyBadge(MeasuresButton, "Ошибка замеров", "#FEE2E2", "#991B1B", ex.Message);
                return;
            }

            if (snapshot.Rooms.Count == 0)
            {
                ApplyBadge(MeasuresButton, "Нет замеров", "#F1F5F9", "#475569", "В ведомостях нет комнат");
                return;
            }

            int notInPlan = 0;
            if (data == null || data.Count == 0)
            {
                notInPlan = snapshot.Rooms.Count;
            }
            else
            {
                var backendRoomsByBaseName = new System.Collections.Generic.Dictionary<string, SmartRemont.ExportRooms.DTO.MeasureRoomInfoDto>(System.StringComparer.OrdinalIgnoreCase);
                foreach(var r in data)
                {
                    if (r == null || string.IsNullOrWhiteSpace(r.RoomName)) continue;
                    var baseName = SmartRemont.ExportRooms.Services.RoomNameMatcher.GetBaseName(r.RoomName);
                    if (!backendRoomsByBaseName.ContainsKey(baseName))
                        backendRoomsByBaseName[baseName] = r;
                }

                foreach(var room in snapshot.Rooms)
                {
                    var baseName = SmartRemont.ExportRooms.Services.RoomNameMatcher.GetBaseName(room.RoomName);
                    if (!backendRoomsByBaseName.TryGetValue(baseName, out var backendRoom) || backendRoom.PlanirovkaRoomId == 0)
                    {
                        notInPlan++;
                    }
                }
            }

            if (notInPlan > 0)
            {
                ApplyBadge(MeasuresButton, $"• {notInPlan} комнат не в планировке", "#FEF9C3", "#A16207");
            }
            else
            {
                ApplyBadge(MeasuresButton, "Готово к отправке", "#F1F5F9", "#475569");
            }
        }

        void ApplyDsState(DsRoomChangeSnapshot data, bool status, int? remontId)
        {
            if (remontId == null || remontId <= 0)
            {
                ApplyBadge(DsAreaChangeButton, "Нет ремонта", "#F1F5F9", "#475569", "Ремонт ещё не создан по заявке");
                DsAreaChangeButton.IsEnabled = false;
                return;
            }

            var session = ExportRoomsApplication.CurrentSession;
            bool hasAddGrant = session?.HasGrant("OA__RemontFormDSAdd") ?? false;
            bool hasEditGrant = session?.HasGrant("OA__RemontFormDSEdit") ?? false;

            if (data == null || data.DsId == null)
            {
                if (hasAddGrant)
                {
                    ApplyBadge(DsAreaChangeButton, "Не создана", "#F1F5F9", "#475569", "При отправке будет создана ДС на изменение площади");
                }
                else
                {
                    ApplyBadge(DsAreaChangeButton, "Нет прав", "#F1F5F9", "#475569");
                    DsAreaChangeButton.IsEnabled = false;
                }
                return;
            }

            var isAccept = data.Header?.IsAccept;
            if (data.Header?.CardId != null)
            {
                ApplyBadge(DsAreaChangeButton, $"• На согласовании №{data.DsId}", "#DBEAFE", "#1D4ED8", "ДС отправлена в канбан на согласование");
                DsAreaChangeButton.IsEnabled = false;
                return;
            }
            
            if (isAccept == 1)
            {
                ApplyBadge(DsAreaChangeButton, $"✔ Утверждена №{data.DsId}", "#DCFCE7", "#166534", "ДС утверждена — изменения только через MySpace");
                DsAreaChangeButton.IsEnabled = false;
                return;
            }

            if (isAccept == 2)
            {
                ApplyBadge(DsAreaChangeButton, $"× Отказана №{data.DsId}", "#FEF2F2", "#DC2626", "ДС отказана");
                DsAreaChangeButton.IsEnabled = false;
                return;
            }

            if (hasEditGrant)
            {
                ApplyBadge(DsAreaChangeButton, $"• Черновик №{data.DsId}", "#FEF9C3", "#A16207", "Можно обновить площади");
            }
            else
            {
                ApplyBadge(DsAreaChangeButton, "Нет прав", "#F1F5F9", "#475569");
                DsAreaChangeButton.IsEnabled = false;
            }
        }

        static void ApplyBadge(Button button, string text, string bgHex, string fgHex, string explanation = null)
        {
            button.ApplyTemplate();
            var badge = button.Template.FindName("DynamicBadge", button) as Border;
            var badgeText = button.Template.FindName("DynamicBadgeText", button) as TextBlock;
            var explText = button.Template.FindName("StatusExplanationText", button) as TextBlock;

            if (badge != null && badgeText != null)
            {
                badge.Visibility = System.Windows.Visibility.Visible;
                badgeText.Text = text;
                badge.Background = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(bgHex));
                badgeText.Foreground = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(fgHex));
            }

            if (explText != null)
            {
                if (!string.IsNullOrWhiteSpace(explanation))
                {
                    explText.Visibility = System.Windows.Visibility.Visible;
                    explText.Text = explanation;
                }
                else
                {
                    explText.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
        }

        async Task EnrichSelectedRemontIfNeededAsync()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont == null || remont.ClientRequestId <= 0)
                return;

            var placeholder = remont.RemontId is int remontId && remontId > 0
                ? $"Ремонт #{remontId}"
                : $"Заявка #{remont.ClientRequestId}";
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
            ConfigureFeatureButton(RoomMaterialsButton, "\uE719", RoomMaterialsSubtitle);
            ConfigureFeatureButton(DsAreaChangeButton, "\uE8A7", DsAreaSubtitle);
            ConfigureFeatureButton(MeasuresButton, "\uE8B7", MeasuresSubtitle);
            ConfigureFeatureButton(MeasuresFromCodeButton, "\uE8F1", MeasuresFromCodeSubtitle);
            ConfigureFeatureButton(MeasuresCompareButton, "\uE8AB", MeasuresCompareSubtitle);
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

        void BindRemontInfo(RemontOption remont)
        {
            if (remont == null)
            {
                ClientRequestIdHeroText.Text = "Заявка #—";
                RemontIdHeroText.Text = string.Empty;
                RemontIdHeroText.Visibility = System.Windows.Visibility.Collapsed;
                RemontNameText.Text = string.Empty;
                RemontNameText.Visibility = System.Windows.Visibility.Collapsed;
                ClientNameText.Text = "—";
                ResidentNameText.Text = "—";
                FlatNumText.Text = "—";
                PresetNameText.Text = "—";
                UpdateProjectInitializedBadge(null);
                return;
            }

            ClientRequestIdHeroText.Text = remont.ClientRequestId > 0
                ? $"Заявка #{remont.ClientRequestId}"
                : "Заявка #—";

            if (remont.RemontId is int remontId && remontId > 0)
            {
                RemontIdHeroText.Text = $"Ремонт #{remontId}";
                RemontIdHeroText.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                RemontIdHeroText.Text = string.Empty;
                RemontIdHeroText.Visibility = System.Windows.Visibility.Collapsed;
            }

            RemontNameText.Text = string.Empty;
            RemontNameText.Visibility = System.Windows.Visibility.Collapsed;

            ClientNameText.Text = DisplayOrDash(remont.ClientName);
            ResidentNameText.Text = DisplayOrDash(remont.ResidentName);
            FlatNumText.Text = DisplayOrDash(remont.FlatNum);
            PresetNameText.Text = DisplayOrDash(string.IsNullOrEmpty(remont.PresetKitName) ? remont.PresetName : remont.PresetKitName);

            bool isProjectApproved = remont.ProjectAccepted == 1;
            if (isProjectApproved)
            {
                ProjectApprovedOverlay.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                ProjectApprovedOverlay.Visibility = System.Windows.Visibility.Collapsed;
            }

            var metadata = ProjectRemontMetadataService.TryRead(_doc);
            UpdateProjectInitializedBadge(
                ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc) ? metadata : null);
        }

        void UpdateProjectInitializedBadge(ProjectRemontMetadata metadata)
        {
            if (metadata == null || metadata.ClientRequestId <= 0)
            {
                ProjectInitializedBadge.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            ProjectInitializedBadge.Visibility = System.Windows.Visibility.Visible;
            ProjectInitializedBadgeText.Text = $"Проект инициализирован · #{metadata.ClientRequestId}";
        }

        static string BuildSubtitle(RemontOption remont) =>
            string.IsNullOrWhiteSpace(remont?.Name) ? string.Empty : remont.Name.Trim();

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        void RefreshProjectInitState()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var selectedClientRequestId = remont?.ClientRequestId ?? 0;
            var isInitialized = ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc);

            ApplyHubMenuVisibility(isInitialized);

            if (!isInitialized)
            {
                ApplyInitFeatureBadge(InitProjectButton, null);
                InitProjectButton.IsEnabled = !_initInProgress && selectedClientRequestId > 0;
                return;
            }

            var metadata = ProjectRemontMetadataService.TryRead(_doc);
            ApplyInitFeatureBadge(InitProjectButton, metadata?.ClientRequestId);

            if (selectedClientRequestId > 0 && metadata != null && metadata.ClientRequestId != selectedClientRequestId)
            {
                SetStatus(
                    $"Проект привязан к заявке #{metadata.ClientRequestId}. Выбрана заявка #{selectedClientRequestId}.",
                    isSuccess: false);
            }
        }

        void ApplyHubMenuVisibility(bool isInitialized)
        {
            InitProjectButton.Visibility = isInitialized
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

            // После init — все функции доступны через client_request_id (PLUGIN_API.md).
            // ДС «изменение площади» дополнительно требует remont_id — гейтится внутри окна.
            var workButtons = new[]
            {
                RevitMaterialsButton,
                RoomMaterialsButton,
                DsAreaChangeButton,
                MeasuresButton
            };

            foreach (var button in workButtons)
            {
                button.Visibility = isInitialized
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;
            }

            FunctionsSectionLabel.Text = isInitialized ? "ФУНКЦИИ" : "ИНИЦИАЛИЗАЦИЯ";
        }

        static void ApplyInitFeatureBadge(Button button, int? clientRequestId)
        {
            button.ApplyTemplate();

            var badge = button.Template.FindName("SentBadge", button) as Border;
            var badgeText = button.Template.FindName("SentBadgeText", button) as TextBlock;
            if (badge == null || badgeText == null)
                return;

            if (clientRequestId == null || clientRequestId <= 0)
            {
                badge.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            badge.Visibility = System.Windows.Visibility.Visible;
            badgeText.Text = $"Инициализирован #{clientRequestId.Value}";
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
            var clientRequestId = remont?.ClientRequestId ?? 0;
            if (clientRequestId <= 0)
            {
                SetStatus("Не указан ID заявки", isSuccess: false);
                return;
            }

            if (ProjectRemontMetadataService.IsInitialized(_doc)
                && !ProjectRemontMetadataService.ValidateMatches(_doc, clientRequestId))
            {
                var existing = ProjectRemontMetadataService.TryRead(_doc);
                AppMessageDialog.Show(
                    this,
                    AppMessageKind.InDevelopment,
                    "Нельзя инициализировать",
                    $"Проект уже привязан к заявке #{existing?.ClientRequestId}.",
                    $"Выбрана заявка #{clientRequestId}. Откройте другой файл или выберите соответствующую заявку.");
                return;
            }

            var remontId = remont?.RemontId ?? 0;
            var targetPath = ProjectFileNamingService.BuildFullPath(
                clientRequestId,
                remontId,
                remont?.ResidentName,
                remont?.FlatNum);
            var fileExists = File.Exists(targetPath);

            SetStatus("Загрузка списка материалов...", isSuccess: true);

            RevitMaterialReadResponse materialsResponse;
            try
            {
                materialsResponse = await RevitMaterialsService.ReadAsync(clientRequestId).ConfigureAwait(true);
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

            var preview = new ProjectInitPreviewWindow(clientRequestId, targetPath, fileExists, materialsResponse)
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
            {
                SetStatus(summaryWindow.LastSuccessMessage ?? "Площади отправлены", isSuccess: true);
                if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
                {
                    await FetchAsyncStates().ConfigureAwait(true);
                }
            }
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
            {
                SetStatus(window.LastSuccessMessage ?? "Замеры отправлены", isSuccess: true);
                if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
                {
                    await FetchAsyncStates().ConfigureAwait(true);
                }
            }
        }

        void MeasuresFromCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new RoomMeasurementsFromCodeWindow(_doc);
            window.Owner = this;
            window.ShowDialog();

            if (window.DialogResult == true)
                SetStatus(window.LastSuccessMessage ?? "Замеры по коду отправлены", isSuccess: true);
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

        async void RevitMaterialsButton_Click(object sender, RoutedEventArgs e)
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont == null || remont.ClientRequestId <= 0)
            {
                SetStatus("Не указан ID заявки", isSuccess: false);
                return;
            }

            var window = new RevitMaterialsWindow(remont.ClientRequestId, _doc);
            window.Owner = this;
            window.ShowDialog();
            
            // После закрытия окна перечитаем статусы
            if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
            {
                await FetchAsyncStates().ConfigureAwait(true);
            }
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
