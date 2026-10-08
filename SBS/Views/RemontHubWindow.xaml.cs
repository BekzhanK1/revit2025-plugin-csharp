using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public partial class RemontHubWindow : Window
    {
        readonly Document _doc;

        const string InitProjectSubtitle = "Проект из шаблона .rte по грейду, метаданные и материалы";
        const string BindWithoutInitSubtitle = "Записать заявку в открытый файл без шаблона и материалов";
        const string DsAreaSubtitle = "Отправка площадей помещений в Smart Remont";
        const string MeasuresSubtitle = "Отправка замеров из ведомостей Revit";
        const string MeasuresFromCodeSubtitle = "Площадь стен из модели Revit";
        const string MeasuresCompareSubtitle = "Спецификация и код — в одной таблице с подсветкой";
        const string RoomMaterialsSubtitle = "Сверка с договором (ТК) и привязка ДС TK_CHANGE";
        const string RevitMaterialsSubtitle = "Загрузка RFA и surface-типов из Smart Remont";
        const string TypeParametersSubtitle = "ID материала и ID типа материала выбранного типа";

        bool _initInProgress;
        bool _showInitOverlay;
        CancellationTokenSource _initCts;

        // Скрытая карточка «Привязать заявку без шаблона»: 5 кликов по заголовку раздела за 3 секунды.
        const int BindWithoutInitClickCount = 5;
        static readonly TimeSpan BindWithoutInitClickWindow = TimeSpan.FromSeconds(3);
        readonly System.Collections.Generic.List<DateTime> _sectionLabelClicks = new();
        bool _bindWithoutInitRevealed;

        public RemontHubWindow(Document doc)
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(CompanyLogoImage);
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            ApplyHubMenuVisibility(ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc));
            Loaded += RemontHubWindow_Loaded;
            Closing += (_, e) =>
            {
                // Крестик / Alt+F4 во время init или re-sync: окно не закрываем, иначе процесс
                // продолжит работать с Revit без окна и упадёт на DialogResult закрытого окна.
                if (_initInProgress)
                {
                    e.Cancel = true;
                    SetStatus("Дождитесь окончания или нажмите «Отмена» на индикаторе.", isSuccess: false);
                    return;
                }

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

            try
            {
                // Сначала только /revit/plugin/* — office DS list не должен блокировать хаб.
                var dsTask = DsRoomChangeService.TryReadAsync(clientRequestId);
                var measuresTask = MeasuresService.TryReadAsync(clientRequestId);
                var materialsTask = RevitMaterialsService.TryReadAsync(clientRequestId);
                var flagsTask = ClientMaterialFlagsService.TryReadAsync(clientRequestId);

                await Task.WhenAll(dsTask, measuresTask, materialsTask, flagsTask).ConfigureAwait(true);

                var ds = await dsTask;
                var measures = await measuresTask;
                var materials = await materialsTask;
                var flags = await flagsTask;

                ApplyMaterialsState(materials.Data, materials.Status, materials.Error, clientRequestId);
                // Метки ТК — вспомогательные: ошибка не попадает в окно проблем, только в лог (внутри сервиса).
                if (materials.Status && flags.Status)
                    ApplyMaterialFlagsState(flags.Data, clientRequestId);
                ApplyMeasuresState(measures.Data, measures.Status, measures.Error);

                var resolvedRemontId = remont?.RemontId ?? ds.RemontId;
                ApplyDsState(ds.Data, ds.Status, resolvedRemontId);

                var problems = new System.Collections.Generic.List<string>();
                if (!materials.Status)
                    problems.Add("Материалы: " + (materials.Error ?? "ошибка"));
                else if (materials.Data?.Data == null || materials.Data.Data.Count == 0)
                    problems.Add(
                        "Материалы: API вернул 0 строк.\n"
                        + $"Сейчас apiOriginUrl = {Configs.ApiOriginUrl}\n"
                        + "На testapi у этой заявки часто нет каталога — нужен prod (myspace-api.smartremont.kz).");

                if (!ds.Status)
                    problems.Add("ДС площади: " + (ds.Error ?? "ошибка"));

                if (!measures.Status
                    && measures.Error?.IndexOf("планировк", StringComparison.OrdinalIgnoreCase) < 0
                    && measures.Error?.IndexOf("plan", StringComparison.OrdinalIgnoreCase) < 0)
                    problems.Add("Замеры: " + (measures.Error ?? "ошибка"));

                // Отдельно и с коротким timeout — бейдж ТК ДС.
                try
                {
                    var tkDs = await DsTkChangeService.TryListAsync(clientRequestId).ConfigureAwait(true);
                    ApplyTkDsState(tkDs.State, tkDs.Ok, tkDs.Error);
                    if (!tkDs.Ok && !string.IsNullOrWhiteSpace(tkDs.Error))
                        problems.Add("ДС ТК (список): " + tkDs.Error);
                }
                catch (Exception tkEx)
                {
                    ExportRoomsApplication._logger?.Warning(tkEx, "Hub TK DS badge failed");
                    ApplyBadge(RoomMaterialsButton, "Сверка", "#F1F5F9", "#475569", "Список ДС недоступен: " + tkEx.Message);
                    RoomMaterialsButton.IsEnabled = true;
                    problems.Add("ДС ТК (список): " + tkEx.Message);
                }

                if (problems.Count > 0)
                {
                    AppMessageBox.Show(
                        this,
                        string.Join("\n\n", problems) + $"\n\nAPI: {Configs.ApiOriginUrl}",
                        "Smart Remont — проблемы загрузки",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    SetStatus("Есть ошибки загрузки — см. окно", false);
                    return;
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Hub FetchAsyncStates failed");
                SetStatus("Ошибка обновления статусов: " + ex.Message, true);
                AppMessageBox.Show(
                    this,
                    ex.Message + $"\n\nAPI: {Configs.ApiOriginUrl}",
                    "Ошибка обновления статусов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            SetStatus(string.Empty, true);
        }

        void ApplyTkDsState(DsTkChangeBindState state, bool ok, string error)
        {
            RoomMaterialsButton.IsEnabled = true;

            if (!ok)
            {
                // Список office DS часто недоступен без MySpace-грантов — сверку не блокируем.
                ApplyBadge(
                    RoomMaterialsButton,
                    "Сверка",
                    "#F1F5F9",
                    "#475569",
                    string.IsNullOrWhiteSpace(error)
                        ? "Сверка доступна; список ДС не загрузился"
                        : "Сверка доступна. ДС: " + error);
                return;
            }

            var items = state?.Items ?? new System.Collections.Generic.List<DsTkChangeItem>();
            if (items.Count == 0)
            {
                ApplyBadge(
                    RoomMaterialsButton,
                    "Не создана",
                    "#F1F5F9",
                    "#475569",
                    "Откройте сверку и создайте пустую ДС или выберите существующую");
                return;
            }

            var editableCount = items.Count(i => i.CanEdit);
            if (editableCount > 1)
            {
                ApplyBadge(
                    RoomMaterialsButton,
                    $"• {editableCount} черновика",
                    "#FEF9C3",
                    "#A16207",
                    "Несколько черновиков TK_CHANGE — выберите в окне сверки");
                return;
            }

            var badgeItem = DsTkChangeBindState.PreferHubBadge(items);
            if (badgeItem == null)
            {
                ApplyBadge(RoomMaterialsButton, "Не создана", "#F1F5F9", "#475569");
                return;
            }

            if (badgeItem.IsAccept == 1)
            {
                ApplyBadge(
                    RoomMaterialsButton,
                    $"✔ Утверждена №{badgeItem.DsId}",
                    "#DCFCE7",
                    "#166534",
                    "ДС утверждена — сверка доступна");
                return;
            }

            if (badgeItem.IsAccept == 2)
            {
                ApplyBadge(
                    RoomMaterialsButton,
                    $"× Отказана №{badgeItem.DsId}",
                    "#FEF2F2",
                    "#DC2626",
                    "ДС отказана");
                return;
            }

            if (badgeItem.CardId != null)
            {
                ApplyBadge(
                    RoomMaterialsButton,
                    $"• На согласовании №{badgeItem.DsId}",
                    "#DBEAFE",
                    "#1D4ED8",
                    "ДС на согласовании — сверка доступна, правки только в MySpace после решения");
                return;
            }

            ApplyBadge(
                RoomMaterialsButton,
                $"• Черновик №{badgeItem.DsId}",
                "#FEF9C3",
                "#A16207",
                "Черновик привязан — сверка и (скоро) правки объёмов");
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

        void ApplyMaterialFlagsState(ClientMaterialFlagsResponse flags, int clientRequestId)
        {
            var stale = ClientMaterialFlagsService.CountStale(flags);
            var unavailable = ClientMaterialFlagsService.CountUnavailable(flags);
            if (stale == 0 && unavailable == 0)
                return;

            var parts = new System.Collections.Generic.List<string>();
            if (stale > 0) parts.Add($"{stale} неактуальны");
            if (unavailable > 0) parts.Add($"{unavailable} нет в наличии");

            var lastSync = LocalSettingsService.GetLastMaterialSyncTime(clientRequestId);
            var syncText = lastSync.HasValue ? $"Синхронизировано · {lastSync.Value:HH:mm}. " : string.Empty;

            ApplyBadge(
                RevitMaterialsButton,
                "⚠ " + string.Join(" · ", parts),
                "#FFF8EB",
                "#92400E",
                syncText + "Позиции ТК отличаются от подбора или недоступны. Замена — в MySpace через ДС.");
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

            var measuresConfirmed = data != null && data.Any(r => r != null && r.IsMeasureConfirm == 1);

            if (notInPlan > 0)
            {
                ApplyBadge(MeasuresButton, $"• {notInPlan} комнат не в планировке", "#FEF9C3", "#A16207");
            }
            else if (measuresConfirmed)
            {
                ApplyBadge(MeasuresButton, "Уже отправлено", "#DCFCE7", "#166534", "В MySpace отмечено «Замеры подтверждены»");
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

            var needsEnrich =
                string.IsNullOrWhiteSpace(remont.ClientName)
                || string.IsNullOrWhiteSpace(remont.ResidentName)
                || string.IsNullOrWhiteSpace(remont.FlatNum)
                || IsPlaceholderRemontName(remont);

            if (!needsEnrich)
                return;

            var (ok, error) = await ProjectRemontBindingService
                .TryEnrichFromQuickSearchAsync(remont)
                .ConfigureAwait(true);
            BindRemontInfo(remont);

            if (!ok)
            {
                AppMessageBox.Show(
                    this,
                    error ?? "Не удалось загрузить карточку заявки.",
                    "Ошибка загрузки заявки",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        static bool IsPlaceholderRemontName(RemontOption remont)
        {
            if (string.IsNullOrWhiteSpace(remont?.Name))
                return true;

            var name = remont.Name.Trim();
            if (remont.RemontId is int remontId && remontId > 0
                && string.Equals(name, $"Ремонт #{remontId}", StringComparison.Ordinal))
                return true;

            return string.Equals(name, $"Заявка #{remont.ClientRequestId}", StringComparison.Ordinal);
        }
        void SetupFeatureButtons()
        {
            ConfigureFeatureButton(InitProjectButton, "\uE8C8", InitProjectSubtitle);
            ConfigureFeatureButton(BindWithoutInitButton, "\uE71B", BindWithoutInitSubtitle);
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
            UpdateInitializedProjectPanel(
                ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc) ? metadata : null);
        }

        void UpdateInitializedProjectPanel(ProjectRemontMetadata metadata)
        {
            if (metadata == null || metadata.ClientRequestId <= 0)
            {
                InitializedProjectPanel.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            InitializedProjectPanel.Visibility = System.Windows.Visibility.Visible;
            InitializedProjectPathText.Text = string.IsNullOrWhiteSpace(_doc?.PathName)
                ? "Путь к файлу не сохранён — выполните Save."
                : _doc.PathName;

            var initializedAt = metadata.InitializedAt;
            if (!string.IsNullOrWhiteSpace(initializedAt)
                && DateTime.TryParse(initializedAt, out var parsed))
            {
                InitializedProjectMetaText.Text =
                    $"Инициализирован: {parsed.ToLocalTime():dd.MM.yyyy HH:mm} · заявка #{metadata.ClientRequestId}";
            }
            else
            {
                InitializedProjectMetaText.Text = $"Заявка #{metadata.ClientRequestId}";
            }

            ResyncMaterialsButton.IsEnabled = !_initInProgress;
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

            CloseButton.IsEnabled = !_initInProgress;
            ApplyHubMenuVisibility(isInitialized);

            if (!isInitialized)
            {
                ApplyInitFeatureBadge(InitProjectButton, null);
                InitProjectButton.IsEnabled = !_initInProgress && selectedClientRequestId > 0;
                BindWithoutInitButton.IsEnabled = !_initInProgress && selectedClientRequestId > 0;
                return;
            }

            var metadata = ProjectRemontMetadataService.TryRead(_doc);
            ApplyInitFeatureBadge(InitProjectButton, metadata?.ClientRequestId);
            UpdateInitializedProjectPanel(metadata);

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
            BindWithoutInitButton.Visibility = !isInitialized && _bindWithoutInitRevealed
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

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

        void FunctionsSectionLabel_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_bindWithoutInitRevealed || ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
                return;

            var now = DateTime.UtcNow;
            _sectionLabelClicks.RemoveAll(t => now - t > BindWithoutInitClickWindow);
            _sectionLabelClicks.Add(now);
            if (_sectionLabelClicks.Count < BindWithoutInitClickCount)
                return;

            _sectionLabelClicks.Clear();
            _bindWithoutInitRevealed = true;
            RefreshProjectInitState();
        }

        /// <summary>
        /// Записывает заявку в ExtensibleStorage открытого файла и сохраняет его — без шаблона .rte
        /// и загрузки материалов. Для проектов, собранных вручную или до появления инициализации.
        /// </summary>
        async void BindWithoutInitButton_Click(object sender, RoutedEventArgs e)
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

            if (_doc == null || _doc.IsFamilyDocument)
            {
                SetStatus("Откройте файл проекта (.rvt), а не семейство.", isSuccess: false);
                return;
            }

            if (string.IsNullOrWhiteSpace(_doc.PathName))
            {
                SetStatus("Файл не сохранён на диске. Сохраните его (Save As) и повторите привязку.", isSuccess: false);
                return;
            }

            var remontId = remont.RemontId ?? 0;
            var question = $"Привязать открытый файл к заявке #{clientRequestId} без шаблона и загрузки материалов?\n\n{_doc.PathName}";
            if (remontId <= 0)
                question += "\n\nНомер ремонта не найден: ДС на изменение квадратуры будет недоступна.";
            question += "\n\nФайл будет сохранён.";

            if (AppMessageBox.Show(this, question, "Привязка заявки", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes)
                return;

            try
            {
                ProjectRemontMetadataService.Write(_doc, new ProjectRemontMetadata
                {
                    RemontId = remontId,
                    ClientRequestId = clientRequestId
                });
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex,
                    "Bind without init failed: client_request_id={ClientRequestId}, path={Path}",
                    clientRequestId,
                    _doc.PathName);
                SetStatus($"Не удалось записать заявку в файл: {ex.Message}", isSuccess: false);
                return;
            }

            string saveError = null;
            try
            {
                _doc.Save();
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
                ExportRoomsApplication._logger?.Warning(ex,
                    "Bind without init: metadata written but save failed, path={Path}",
                    _doc.PathName);
            }

            ExportRoomsApplication._logger?.Information(
                "Project bound without init: client_request_id={ClientRequestId}, remont_id={RemontId}, path={Path}",
                clientRequestId,
                remontId,
                _doc.PathName);

            _bindWithoutInitRevealed = false;
            BindRemontInfo(remont);
            RefreshProjectInitState();

            if (saveError != null)
            {
                SetStatus($"Заявка #{clientRequestId} привязана, но файл не сохранился: {saveError}. Сохраните его вручную (Ctrl+S).",
                    isSuccess: false);
                return;
            }

            AppMessageDialog.ShowSuccess(
                this,
                "Заявка привязана",
                $"Файл привязан к заявке #{clientRequestId}",
                _doc.PathName);
            await FetchAsyncStates().ConfigureAwait(true);
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

            if (remont.GradeId <= 0)
            {
                SetStatus("У заявки не указан грейд. Найдите заявку заново.", isSuccess: false);
                return;
            }

            ShowInitProgress("Проверка шаблона проекта...", indeterminate: true);
            try
            {
                await ProjectTemplateService.EnsureGradeHasTemplateAsync(remont.GradeId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project template check failed");
                HideInitProgress();
                SetStatus(ex.Message, isSuccess: false);
                AppMessageBox.Show(
                    ex.Message,
                    "Шаблон проекта",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ShowInitProgress("Загрузка списка материалов...", indeterminate: true);

            RevitMaterialReadResponse materialsResponse;
            try
            {
                materialsResponse = await RevitMaterialsService.ReadAsync(clientRequestId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Project init preview: materials read failed");
                HideInitProgress();
                SetStatus("Не удалось загрузить материалы: " + ex.Message, isSuccess: false);
                AppMessageBox.Show(
                    ex.Message,
                    "Ошибка загрузки материалов",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            HideInitProgress();

            // Окно закрыли, пока читались шаблон и материалы — превью открывать некуда.
            if (!IsVisible)
                return;

            if (ProjectInitMaterialsPreflightService.CountSyncableMaterials(materialsResponse.Data) <= 0)
            {
                SetStatus(ProjectInitMaterialsPreflightService.BuildZeroSyncableMessage(), isSuccess: false);
                AppMessageBox.Show(
                    ProjectInitMaterialsPreflightService.BuildZeroSyncableMessage(),
                    "Нет материалов для init",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // remont_id в списке заявок может быть null — используем тот же fallback на
            // materialsResponse.RemontId, что и ProjectInitService, иначе preview покажет
            // путь, отличающийся от реального места сохранения файла.
            var remontId = remont?.RemontId ?? materialsResponse.RemontId ?? 0;
            var targetPath = ProjectFileNamingService.BuildFullPath(
                clientRequestId,
                remontId,
                remont?.ResidentName,
                remont?.FlatNum);
            var fileExists = File.Exists(targetPath);

            var preview = new ProjectInitPreviewWindow(_doc, clientRequestId, targetPath, fileExists, materialsResponse)
            {
                Owner = this
            };

            if (preview.ShowDialog() != true)
            {
                ClearStatus();
                return;
            }

            _initInProgress = true;
            InitProjectButton.IsEnabled = false;
            CloseButton.IsEnabled = false;
            ResyncMaterialsButton.IsEnabled = false;
            BeginInitProgress("Подготовка к инициализации...", indeterminate: true);

            var progress = new Progress<ProjectInitProgress>(UpdateInitProgress);

            ProjectInitResult result = null;
            try
            {
                result = await ProjectInitService.InitializeProjectAsync(
                    _doc,
                    remont,
                    overwriteExistingFile: fileExists,
                    progress,
                    materialsResponse,
                    _initCts.Token,
                    ignorePreflightValidation: preview.PreflightValidationIgnored).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Project init failed");
                SetStatus("Ошибка инициализации: " + ex.Message, isSuccess: false);
                return;
            }
            finally
            {
                EndInitProgress();
                _initInProgress = false;
                RefreshProjectInitState();
            }

            if (result == null)
                return;

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
                if (result.Cancelled)
                {
                    SetStatus(result.ErrorMessage ?? "Инициализация отменена", isSuccess: false);
                    return;
                }

                if (result.FileAlreadyExists && !fileExists)
                {
                    SetStatus(result.ErrorMessage ?? "Файл уже существует", isSuccess: false);
                    return;
                }

                SetStatus(result.ErrorMessage ?? "Инициализация не удалась", isSuccess: false);
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    AppMessageBox.Show(
                        this,
                        result.ErrorMessage,
                        "Ошибка инициализации",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            var details = BuildInitSuccessDetails(result);
            ProjectPostInitOpenService.RequestOpenProjectAfterPluginExit(result.NewFilePath);
            var successTitle = result.Errors > 0
                ? "Проект инициализирован с ошибками"
                : "Проект инициализирован";
            var successSummary = result.Errors > 0
                ? $"Загружено материалов: {result.MaterialsLoaded}, ошибок: {result.Errors}"
                : $"Загружено материалов: {result.MaterialsLoaded}";
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                details = result.ErrorMessage + "\n\n" + details;

            AppMessageDialog.ShowSuccess(
                this,
                successTitle,
                successSummary,
                details,
                buttonText: "Открыть проект");

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

            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                lines.Add("Прежний файл проекта сохранён как: " + result.BackupPath);

            lines.Add("Нажмите «Открыть проект» — плагин закроется, и Revit откроет новый проект. "
                      + "Остальные файлы закроются, кроме тех, где есть несохранённые изменения.");

            return string.Join("\n\n", lines);
        }

        void BeginInitProgress(string message, bool indeterminate)
        {
            _initCts?.Cancel();
            _initCts?.Dispose();
            _initCts = new CancellationTokenSource();
            _showInitOverlay = true;

            InitLoaderOverlay.CancelRequested -= InitLoaderOverlay_CancelRequested;
            InitLoaderOverlay.CancelRequested += InitLoaderOverlay_CancelRequested;

            StatusBanner.Visibility = System.Windows.Visibility.Collapsed;
            StatusPlainHost.Visibility = System.Windows.Visibility.Collapsed;
            InitLoaderOverlay.Show(message, indeterminate, allowCancel: true);
        }

        void InitLoaderOverlay_CancelRequested(object sender, EventArgs e)
        {
            if (_initCts != null && !_initCts.IsCancellationRequested)
                _initCts.Cancel();
        }

        void UpdateInitProgress(ProjectInitProgress progress)
        {
            if (progress == null || !_showInitOverlay)
                return;

            if (progress.Indeterminate || progress.Total <= 0)
                InitLoaderOverlay.Show(progress.Message, indeterminate: true, allowCancel: true);
            else
                InitLoaderOverlay.UpdateProgress(progress.Done, progress.Total, progress.Message);
        }

        void EndInitProgress()
        {
            _showInitOverlay = false;
            InitLoaderOverlay.CancelRequested -= InitLoaderOverlay_CancelRequested;
            _initCts?.Dispose();
            _initCts = null;
            InitLoaderOverlay.HideImmediate();
        }

        void ShowInitProgress(string message, bool indeterminate)
        {
            StatusBanner.Visibility = System.Windows.Visibility.Collapsed;
            StatusPlainHost.Visibility = System.Windows.Visibility.Collapsed;
            InitLoaderOverlay.Show(message, indeterminate, allowCancel: false);
        }

        void HideInitProgress() => EndInitProgress();

        async void ResyncMaterialsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_initInProgress)
                return;

            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont == null || remont.ClientRequestId <= 0)
            {
                SetStatus("Не указан ID заявки", isSuccess: false);
                return;
            }

            if (_doc.IsWorkshared)
            {
                SetStatus("Worksharing не поддерживается для re-sync.", isSuccess: false);
                return;
            }

            if (!ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc)
                || !ProjectRemontMetadataService.ValidateMatches(_doc, remont.ClientRequestId))
            {
                SetStatus("Re-sync доступен только для инициализированного проекта текущей заявки.", isSuccess: false);
                return;
            }

            var confirm = AppMessageBox.Show(
                this,
                "Повторная strict-синхронизация материалов без SaveCopyAs. Продолжить?",
                "Re-sync материалов",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            _initInProgress = true;
            ResyncMaterialsButton.IsEnabled = false;
            CloseButton.IsEnabled = false;
            BeginInitProgress("Re-sync материалов...", indeterminate: true);

            var progress = new Progress<ProjectInitProgress>(UpdateInitProgress);
            ProjectInitResult result = null;
            try
            {
                result = await ProjectInitService.ResyncMaterialsAsync(
                    _doc,
                    remont,
                    progress,
                    cancellationToken: _initCts.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus("Re-sync не удался: " + ex.Message, isSuccess: false);
                return;
            }
            finally
            {
                EndInitProgress();
                _initInProgress = false;
                RefreshProjectInitState();
            }

            if (result == null)
                return;

            if (!result.Success)
            {
                SetStatus(result.ErrorMessage ?? "Re-sync не удался", isSuccess: false);
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    AppMessageBox.Show(
                        this,
                        result.ErrorMessage,
                        result.Cancelled ? "Re-sync отменён" : "Ошибка re-sync",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            SetStatus($"Re-sync завершён: загружено {result.MaterialsLoaded} материалов", isSuccess: true);
            await FetchAsyncStates().ConfigureAwait(true);
        }

        void OpenProjectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var path = _doc?.PathName;
            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus("Путь к файлу проекта не сохранён.", isSuccess: false);
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                SetStatus("Папка проекта не найдена.", isSuccess: false);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }

        void ClearStatus()
        {
            StatusBanner.Visibility = System.Windows.Visibility.Collapsed;
            StatusPlainHost.Visibility = System.Windows.Visibility.Collapsed;
            StatusTextBlock.Text = string.Empty;
            StatusPlainText.Text = string.Empty;
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
            var window = new DsTkChangeWindow(_doc);
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
