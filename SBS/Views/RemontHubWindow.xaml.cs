using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Хаб заявки: процесс по этапам (проект → ДС квадратуры → замеры → ДС ТК) и материалы.
    /// Состояние этапов считает <see cref="HubProcessService"/>, здесь — загрузка данных и отрисовка.
    /// </summary>
    public partial class RemontHubWindow : Window
    {
        readonly Document _doc;

        bool _initInProgress;
        bool _showInitOverlay;
        bool _refreshInProgress;
        CancellationTokenSource _initCts;

        // Скрытая кнопка «Привязать без шаблона»: 5 кликов по заголовку процесса за 3 секунды.
        const int BindWithoutInitClickCount = 5;
        static readonly TimeSpan BindWithoutInitClickWindow = TimeSpan.FromSeconds(3);
        readonly List<DateTime> _sectionLabelClicks = new();
        bool _bindWithoutInitRevealed;

        // Последние загруженные статусы — из них собирается процесс.
        bool _statesLoaded;
        (DsRoomChangeSnapshot Data, bool Status, string Error, int? RemontId) _ds;
        (List<MeasureRoomInfoDto> Data, bool Status, string Error) _measures;
        (DsTkChangeBindState State, bool Ok, string Error) _tk;

        public RemontHubWindow(Document doc)
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(CompanyLogoImage);
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;

            StepProject.PrimaryClick += (_, _) => InitProjectButton_Click(this, new RoutedEventArgs());
            StepProject.SecondaryClick += (_, _) => BindWithoutInitButton_Click(this, new RoutedEventArgs());
            StepDsArea.PrimaryClick += (_, _) => DsAreaChangeButton_Click(this, new RoutedEventArgs());
            StepMeasures.PrimaryClick += (_, _) => MeasuresButton_Click(this, new RoutedEventArgs());
            StepDsTk.PrimaryClick += (_, _) => RoomMaterialsButton_Click(this, new RoutedEventArgs());

            RenderProcess();
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
            BindRemontInfo(ExportRoomsApplication.SelectedRemont);
            RefreshProjectInitState();
            await EnrichSelectedRemontIfNeededAsync().ConfigureAwait(true);
            RefreshProjectInitState();

            if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
            {
                await FetchAsyncStates().ConfigureAwait(true);
            }
        }

        async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
            {
                RefreshProjectInitState();
                return;
            }

            await FetchAsyncStates().ConfigureAwait(true);
        }

        /// <summary>
        /// Перечитывает статусы этапов и материалов. Ошибки не всплывают окнами — показываются
        /// на своём этапе (или в карточке материалов), чтобы было видно, что именно не загрузилось.
        /// </summary>
        async Task FetchAsyncStates()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var clientRequestId = remont?.ClientRequestId ?? 0;
            if (clientRequestId <= 0 || _refreshInProgress) return;

            _refreshInProgress = true;
            RefreshButton.IsEnabled = false;
            RefreshButtonText.Text = "Обновление…";
            SetStatus("Обновление статусов…", true);

            try
            {
                var dsTask = DsRoomChangeService.TryReadAsync(clientRequestId);
                var measuresTask = MeasuresService.TryReadAsync(clientRequestId);
                var materialsTask = RevitMaterialsService.TryReadAsync(clientRequestId);
                var flagsTask = ClientMaterialFlagsService.TryReadAsync(clientRequestId);
                var tkTask = TryListTkAsync(clientRequestId);

                await Task.WhenAll(dsTask, measuresTask, materialsTask, flagsTask, tkTask).ConfigureAwait(true);

                _ds = await dsTask;
                _measures = await measuresTask;
                _tk = await tkTask;
                _statesLoaded = true;

                var materials = await materialsTask;
                var flags = await flagsTask;
                ApplyMaterialsState(materials.Data, materials.Status, materials.Error, clientRequestId);
                // Метки ТК — вспомогательные: ошибка не показывается, только в лог (внутри сервиса).
                if (materials.Status && flags.Status)
                    ApplyMaterialFlagsState(flags.Data, clientRequestId);

                if (!_tk.Ok && !string.IsNullOrWhiteSpace(_tk.Error))
                    ExportRoomsApplication._logger?.Warning("Hub: DS TK list failed: {Error}", _tk.Error);

                RenderProcess();
                SetStatus($"Статусы обновлены · {DateTime.Now:HH:mm}", true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Hub FetchAsyncStates failed");
                SetStatus("Не удалось обновить статусы: " + ex.Message, false);
            }
            finally
            {
                _refreshInProgress = false;
                RefreshButton.IsEnabled = !_initInProgress;
                RefreshButtonText.Text = "Обновить";
            }
        }

        static async Task<(DsTkChangeBindState State, bool Ok, string Error)> TryListTkAsync(int clientRequestId)
        {
            try
            {
                return await DsTkChangeService.TryListAsync(clientRequestId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Hub: DS TK list failed");
                return (null, false, ex.Message);
            }
        }

        /// <summary>Пересобирает процесс из последних загруженных статусов и модели.</summary>
        void RenderProcess()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var isInitialized = ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc);
            var session = ExportRoomsApplication.CurrentSession;

            var input = new HubProcessInput
            {
                IsInitialized = isInitialized,
                ProjectPath = _doc?.PathName,
                RemontId = remont?.RemontId ?? _ds.RemontId,
                HasDsAddGrant = session?.HasGrant("OA__RemontFormDSAdd") ?? false,
                HasDsEditGrant = session?.HasGrant("OA__RemontFormDSEdit") ?? false,
                DsLoaded = _statesLoaded && _ds.Status,
                Ds = _ds.Data,
                DsError = _statesLoaded ? _ds.Error : "статусы ещё загружаются",
                MeasuresLoaded = _statesLoaded && _measures.Status,
                MeasureRooms = _measures.Data,
                MeasuresError = _statesLoaded ? _measures.Error : "статусы ещё загружаются",
                RevitRooms = isInitialized ? TryCollectRevitRooms() : null,
                TkLoaded = _statesLoaded && _tk.Ok,
                Tk = _tk.State,
                TkError = _tk.Error
            };

            var process = HubProcessService.Build(input);
            var projectApproved = remont?.ProjectAccepted == 1;
            var actionsEnabled = !_initInProgress && !projectApproved;

            // Пока статусы не пришли, этапы после проекта не показываем как ошибку загрузки.
            var loading = isInitialized && !_statesLoaded;
            StepProject.Apply(process.Project, actionsEnabled && (remont?.ClientRequestId ?? 0) > 0);
            StepProject.SecondaryText = !isInitialized && _bindWithoutInitRevealed ? "Привязать без шаблона" : null;
            StepDsArea.Apply(loading ? Loading() : process.DsArea, actionsEnabled);
            StepMeasures.Apply(loading ? Locked("Откроется после утверждения ДС на изменение квадратуры.") : process.Measures, actionsEnabled);
            StepDsTk.Apply(loading ? Locked("Откроется после подтверждения замеров.") : process.DsTk, actionsEnabled);

            ProcessProgressText.Text = process.AllPassed
                ? "Все этапы пройдены"
                : $"Этап {process.CurrentStepNumber} из 4";

            ProjectApprovedOverlay.Visibility = projectApproved ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            MaterialsPanel.Visibility = isInitialized ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            RevitMaterialsButton.IsEnabled = !_initInProgress;
        }

        static HubStepStatus Loading() => new()
        {
            State = HubStepState.Waiting,
            Chip = "Загрузка…",
            Hint = "Получаем статус из MySpace."
        };

        static HubStepStatus Locked(string hint) => new()
        {
            State = HubStepState.Locked,
            Hint = hint
        };

        IReadOnlyList<RoomAreaItem> TryCollectRevitRooms()
        {
            try
            {
                return RoomAreaService.GetPreferredPhase(_doc) == null
                    ? null
                    : RoomAreaService.CollectRooms(_doc);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Hub: could not read Revit rooms");
                return null;
            }
        }

        void ApplyMaterialsState(RevitMaterialReadResponse data, bool status, string error, int clientRequestId)
        {
            if (!status)
            {
                ApplyMaterialsChip("Не загрузились", "ErrorBackgroundBrush", "ErrorBorderBrush", "ErrorTextBrush",
                    error ?? "Ошибка загрузки списка материалов.");
                return;
            }

            if (data?.Data == null || data.Data.Count == 0)
            {
                ApplyMaterialsChip("Нет материалов", "SurfaceHoverBrush", "CardBorderBrush", "TextSecondaryBrush",
                    $"Сервер вернул пустой список. API: {Configs.ApiOriginUrl}");
                return;
            }

            var lastSync = LocalSettingsService.GetLastMaterialSyncTime(clientRequestId);
            ApplyMaterialsChip(
                lastSync.HasValue ? $"Синхронизировано · {lastSync.Value:HH:mm}" : "Список загружен",
                "SuccessSoftBrush", "SuccessBorderBrush", "SuccessTextBrush",
                $"Материалов в списке: {data.Data.Count}.");
        }

        void ApplyMaterialFlagsState(ClientMaterialFlagsResponse flags, int clientRequestId)
        {
            var stale = ClientMaterialFlagsService.CountStale(flags);
            var unavailable = ClientMaterialFlagsService.CountUnavailable(flags);
            if (stale == 0 && unavailable == 0)
                return;

            var parts = new List<string>();
            if (stale > 0) parts.Add($"{stale} неактуальны");
            if (unavailable > 0) parts.Add($"{unavailable} нет в наличии");

            ApplyMaterialsChip(
                string.Join(" · ", parts),
                "WarningSoftBrush", "WarningBorderBrush", "WarningTextBrush",
                "Позиции ТК отличаются от подбора или недоступны. Замена — в MySpace через ДС.");
        }

        void ApplyMaterialsChip(string text, string bgKey, string borderKey, string fgKey, string hint)
        {
            MaterialsChip.Visibility = System.Windows.Visibility.Visible;
            MaterialsChip.Background = (Brush)FindResource(bgKey);
            MaterialsChip.BorderBrush = (Brush)FindResource(borderKey);
            MaterialsChipText.Foreground = (Brush)FindResource(fgKey);
            MaterialsChipText.Text = text;
            MaterialsHintText.Text = hint;
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
                SetStatus(error ?? "Не удалось загрузить карточку заявки.", isSuccess: false);
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

        void BindRemontInfo(RemontOption remont)
        {
            if (remont == null)
            {
                ClientRequestIdHeroText.Text = "Заявка #—";
                RemontIdHeroText.Text = string.Empty;
                RemontIdHeroText.Visibility = System.Windows.Visibility.Collapsed;
                ClientNameText.Text = "Клиент не указан";
                ResidentNameText.Text = "ЖК —";
                FlatNumText.Text = "кв. —";
                PresetNameText.Text = "пакет —";
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

            ClientNameText.Text = string.IsNullOrWhiteSpace(remont.ClientName) ? "Клиент не указан" : remont.ClientName.Trim();
            ResidentNameText.Text = "ЖК " + DisplayOrDash(remont.ResidentName);
            FlatNumText.Text = "кв. " + DisplayOrDash(remont.FlatNum);
            PresetNameText.Text = DisplayOrDash(string.IsNullOrEmpty(remont.PresetKitName) ? remont.PresetName : remont.PresetKitName);

            var metadata = ProjectRemontMetadataService.TryRead(_doc);
            var canUse = ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc);
            UpdateProjectInitializedBadge(canUse ? metadata : null);
            UpdateInitializedProjectPanel(canUse ? metadata : null);
            RenderProcess();
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
                : Path.GetFileName(_doc.PathName);
            InitializedProjectPathText.ToolTip = _doc?.PathName;

            var initializedAt = metadata.InitializedAt;
            if (!string.IsNullOrWhiteSpace(initializedAt)
                && DateTime.TryParse(initializedAt, out var parsed))
            {
                InitializedProjectMetaText.Text = $"Инициализирован {parsed.ToLocalTime():dd.MM.yyyy HH:mm}";
            }
            else
            {
                InitializedProjectMetaText.Text = "Привязан к заявке";
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
            ProjectInitializedBadgeText.Text = $"#{metadata.ClientRequestId}";
        }

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        void RefreshProjectInitState()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var selectedClientRequestId = remont?.ClientRequestId ?? 0;
            var isInitialized = ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc);

            CloseButton.IsEnabled = !_initInProgress;
            RefreshButton.IsEnabled = !_initInProgress && !_refreshInProgress;

            var metadata = isInitialized ? ProjectRemontMetadataService.TryRead(_doc) : null;
            UpdateProjectInitializedBadge(metadata);
            UpdateInitializedProjectPanel(metadata);
            RenderProcess();

            if (isInitialized && selectedClientRequestId > 0 && metadata != null
                && metadata.ClientRequestId != selectedClientRequestId)
            {
                SetStatus(
                    $"Проект привязан к заявке #{metadata.ClientRequestId}. Выбрана заявка #{selectedClientRequestId}.",
                    isSuccess: false);
            }
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
            CloseButton.IsEnabled = false;
            ResyncMaterialsButton.IsEnabled = false;
            RenderProcess();
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
            RenderProcess();
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

            // Статусы перечитываем всегда: этап мог измениться, даже если окно закрыли без отправки.
            if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
                await FetchAsyncStates().ConfigureAwait(true);

            if (summaryWindow.DialogResult == true)
                SetStatus(summaryWindow.LastSuccessMessage ?? "Площади отправлены", isSuccess: true);
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

            if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
                await FetchAsyncStates().ConfigureAwait(true);

            if (window.DialogResult == true)
                SetStatus(window.LastSuccessMessage ?? "Замеры отправлены", isSuccess: true);
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

        async void RoomMaterialsButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new DsTkChangeWindow(_doc);
            window.Owner = this;
            window.ShowDialog();

            if (ProjectRemontMetadataService.CanUseHubWorkFeatures(_doc))
                await FetchAsyncStates().ConfigureAwait(true);
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
