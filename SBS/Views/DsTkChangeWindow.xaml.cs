using Autodesk.Revit.DB;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SmartRemont.ExportRooms.Views
{
    public partial class DsTkChangeWindow : Window
    {
        readonly Document _doc;
        readonly int _clientRequestId;
        DsTkCompareResult _result;
        RoomSrIdSnapshot _revitSnapshot;
        TkQtyScheduleSnapshot _scheduleQty;
        List<DsTkChangeItem> _dsItems = new();
        DsTkChangeItem _boundDs;
        bool _loading;
        bool _dsBusy;

        public DsTkChangeWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            _clientRequestId = ExportRoomsApplication.SelectedRemont?.ClientRequestId ?? 0;
            ClientRequestBadge.Text = _clientRequestId > 0
                ? $"Заявка #{_clientRequestId}"
                : "Заявка #—";
            ApplyDsBadge(null);
            UpdateDsActionButtons();
            Loaded += DsTkChangeWindow_Loaded;
        }

        async void DsTkChangeWindow_Loaded(object sender, RoutedEventArgs e) =>
            await ReloadAsync().ConfigureAwait(true);

        async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
            await ReloadAsync().ConfigureAwait(true);

        void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        async void CreateDsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dsBusy || _clientRequestId <= 0)
                return;

            var session = ExportRoomsApplication.CurrentSession;
            if (session?.HasGrant("OA__RemontFormDSAdd") != true)
            {
                MessageBox.Show(
                    this,
                    "Нет права OA__RemontFormDSAdd — создание ДС недоступно.",
                    "Smart Remont",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_dsItems.Count == 0)
                await RefreshDsBindAsync().ConfigureAwait(true);

            var existingLocked = _dsItems.FirstOrDefault(i => i.IsLocked);
            if (existingLocked != null)
            {
                MessageBox.Show(
                    this,
                    $"Уже есть ДС №{existingLocked.DsId} ({existingLocked.StatusDisplay}).\n\n"
                    + "Новую TK_CHANGE создавать нельзя, пока эта не разсогласована / не закрыта.\n"
                    + "Рассогласование: в БД сбросить card_id и is_accept (см. подсказку в статусе).",
                    "ДС уже существует",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ApplyDsBadge(existingLocked);
                UpdateDsActionButtons();
                return;
            }

            var existingDraft = _dsItems.FirstOrDefault(i => i.CanEdit);
            if (existingDraft != null)
            {
                MessageBox.Show(
                    this,
                    $"Уже есть черновик ДС №{existingDraft.DsId}.\nВыберите его кнопкой «Выбрать…», а не создавайте второй.",
                    "Черновик уже есть",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _boundDs = existingDraft;
                ApplyDsBadge(_boundDs);
                UpdateDsActionButtons();
                return;
            }

            var confirm = MessageBox.Show(
                this,
                "Создать пустую ДС на изменение ТК (черновик) для этой заявки?",
                "Создать ДС",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            _dsBusy = true;
            UpdateDsActionButtons();
            try
            {
                var created = await DsTkChangeService.CreateEmptyAsync(_clientRequestId).ConfigureAwait(true);
                await RefreshDsBindAsync(preferDsId: created.DsId).ConfigureAwait(true);
                StatusText.Text = $"Создан черновик ДС №{created.DsId}. Можно отправить объёмы кнопкой «Отправить в ДС».";
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK create failed");
                MessageBox.Show(this, ex.Message, "Ошибка создания ДС", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _dsBusy = false;
                UpdateDsActionButtons();
            }
        }

        async void PickDsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dsBusy || _clientRequestId <= 0)
                return;

            try
            {
                if (_dsItems.Count == 0)
                    await RefreshDsBindAsync().ConfigureAwait(true);

                if (_dsItems.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        "ДС на изменение ТК пока нет. Нажмите «Создать ДС».",
                        "Smart Remont",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var pick = new DsTkPickWindow(_dsItems) { Owner = this };
                if (pick.ShowDialog() != true || pick.SelectedItem == null)
                    return;

                _boundDs = pick.SelectedItem;
                ApplyDsBadge(_boundDs);
                UpdateDsActionButtons();
                StatusText.Text = $"Привязана ДС №{_boundDs.DsId} ({_boundDs.StatusDisplay}).";
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK pick failed");
                MessageBox.Show(this, ex.Message, "Ошибка выбора ДС", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        void ExportJsonButton_Click(object sender, RoutedEventArgs e)
        {
            if (_result == null)
            {
                StatusText.Text = "Нет сводки для экспорта — сначала дождитесь сверки.";
                return;
            }

            var payload = BuildExportJson();

            var dlg = new SaveFileDialog
            {
                Title = "Сохранить JSON сверки ДС ТК",
                Filter = "JSON (*.json)|*.json",
                FileName = $"ds_tk_compare_{_clientRequestId}_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                DefaultExt = ".json",
                AddExtension = true
            };

            if (dlg.ShowDialog(this) != true)
                return;

            try
            {
                File.WriteAllText(dlg.FileName, payload.ToString(Formatting.Indented));
                StatusText.Text = $"JSON сохранён: {dlg.FileName}";
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK JSON export failed");
                StatusText.Text = "Не удалось сохранить JSON: " + ex.Message;
            }
        }

        JObject BuildExportJson()
        {
            var remont = ExportRoomsApplication.SelectedRemont;
            var root = new JObject
            {
                ["exported_at"] = DateTime.UtcNow.ToString("o"),
                ["client_request_id"] = _clientRequestId
            };
            SetIfHas(root, "remont_id", remont?.RemontId);
            SetIfHas(root, "document_title", _doc?.Title);
            if (_boundDs != null)
            {
                root["ds"] = new JObject
                {
                    ["ds_id"] = _boundDs.DsId,
                    ["status"] = _boundDs.StatusDisplay,
                    ["can_edit"] = _boundDs.CanEdit
                };
                SetIfHas((JObject)root["ds"], "ds_date", _boundDs.DsDate);
            }

            var scan = new JObject
            {
                ["elements_with_sr_id"] = _revitSnapshot?.ElementsWithSrId ?? 0
            };
            if ((_revitSnapshot?.UnassignedElements ?? 0) > 0)
                scan["unassigned_elements"] = _revitSnapshot.UnassignedElements;
            root["scan"] = scan;

            if (_scheduleQty != null)
            {
                root["schedule_qty"] = new JObject
                {
                    ["config_path"] = TkQtyScheduleMapping.ConfigPath,
                    ["lines"] = _scheduleQty.Lines.Count,
                    ["sources"] = new JArray(_scheduleQty.Sources.Select(s =>
                    {
                        var o = new JObject
                        {
                            ["code"] = s.Code,
                            ["found"] = s.Found,
                            ["line_count"] = s.LineCount
                        };
                        SetIfHas(o, "title", s.Title);
                        SetIfHas(o, "schedule_expected", s.ScheduleNameExpected);
                        SetIfHas(o, "schedule_found", s.ScheduleNameFound);
                        SetIfHas(o, "message", s.Message);
                        return o;
                    }))
                };
            }

            root["summary"] = new JObject
            {
                ["rooms"] = _result.Rooms.Count,
                ["match"] = _result.MatchCount,
                ["missing_in_revit"] = _result.MissingInRevitCount,
                ["not_expected_in_model"] = _result.NotExpectedInModelCount,
                ["extra_in_revit"] = _result.ExtraInRevitCount,
                ["qty_mismatch"] = _result.QtyMismatchCount,
                ["total"] = _result.TotalRows
            };
            SetIfHas((JObject)root["summary"], "note", _result.Note);

            var rooms = new JArray();
            foreach (var room in _result.Rooms)
            {
                var roomObj = new JObject { ["room_name"] = room.RoomName };
                var rows = new JArray();
                foreach (var row in room.Rows)
                    rows.Add(BuildExportRow(row));
                roomObj["rows"] = rows;
                rooms.Add(roomObj);
            }

            root["rooms"] = rooms;
            return root;
        }

        static JObject BuildExportRow(DsTkCompareRow row)
        {
            var obj = new JObject
            {
                ["status"] = row.StatusKey,
                ["status_display"] = row.StatusDisplay
            };

            if (row.MaterialId > 0)
                obj["material_id"] = row.MaterialId;
            if (row.ClientMaterialId is > 0)
                obj["client_material_id"] = row.ClientMaterialId.Value;
            if (row.MaterialSetId is > 0)
                obj["material_set_id"] = row.MaterialSetId.Value;
            obj["is_material_cnt_input"] = row.IsMaterialCntInput;

            SetIfHas(obj, "material_name", row.MaterialName);
            SetIfHas(obj, "work_set_name", row.WorkSetName);
            SetIfHas(obj, "kind", row.KindDisplay);
            SetIfHas(obj, "revit_file_type", row.RevitFileType);
            SetIfHas(obj, "revit_name", row.RevitName);
            SetIfHas(obj, "category", row.Category);
            SetIfHas(obj, "source_level", row.SourceLevel);

            if (row.Quantity > 0)
                obj["quantity"] = row.Quantity;

            if (row.TkQty != null)
                obj["tk_qty"] = row.TkQty.Value;
            if (row.ScheduleQty != null)
                obj["schedule_qty"] = row.ScheduleQty.Value;
            SetIfHas(obj, "qty_unit", row.QtyUnit);
            SetIfHas(obj, "qty_status", row.QtyStatusKey);
            SetIfHas(obj, "qty_status_display", row.QtyStatusDisplay);

            return obj;
        }

        static void SetIfHas(JObject obj, string name, int? value)
        {
            if (value is > 0)
                obj[name] = value.Value;
        }

        static void SetIfHas(JObject obj, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var trimmed = value.Trim();
            if (trimmed == "—")
                return;
            obj[name] = trimmed;
        }

        void ProblemsOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // IsChecked=True в XAML стреляет Checked ещё в InitializeComponent — контролы могут быть null.
            if (!IsLoaded || RoomsItemsControl == null || NoDataText == null)
                return;
            BindRooms();
        }

        async Task ReloadAsync()
        {
            if (_loading)
                return;

            if (_clientRequestId <= 0)
            {
                StatusText.Text = "Не указан ID заявки — сверка с ТК недоступна.";
                return;
            }

            if (ExportRoomsApplication.CurrentSession == null
                || string.IsNullOrWhiteSpace(ExportRoomsApplication.CurrentSession.AccessToken))
            {
                StatusText.Text = "Требуется авторизация.";
                return;
            }

            _loading = true;
            StatusText.Text = "Сканирование SR_ID в модели и загрузка ТК…";
            UpdateDsActionButtons();

            try
            {
                _revitSnapshot = RoomMaterialsService.CollectSrId(_doc);
                _scheduleQty = TkQtyScheduleService.Collect(_doc);
                var scheduleFound = _scheduleQty.Sources.Count(s => s.Found);
                var scheduleLines = _scheduleQty.Lines.Count;
                ScanInfoText.Text =
                    $"SR_ID в модели: {_revitSnapshot.ElementsWithSrId}"
                    + (_revitSnapshot.UnassignedElements > 0
                        ? $", без комнаты: {_revitSnapshot.UnassignedElements}"
                        : string.Empty)
                    + $", ведомости qty: {scheduleFound}/{_scheduleQty.Sources.Count} · строк {scheduleLines}";

                var tkTask = ClientMaterialTkService.ReadAsync(_clientRequestId);
                var materialsTask = RevitMaterialsService.TryReadAsync(_clientRequestId);

                // Сверка не ждёт office /ds/read/ — иначе при 403/timeout «ничего не грузится».
                await Task.WhenAll(tkTask, materialsTask).ConfigureAwait(true);

                var tk = await tkTask.ConfigureAwait(true);
                var (materials, materialsOk, materialsError) = await materialsTask.ConfigureAwait(true);

                var meta = BuildMaterialMeta(materialsOk ? materials : null);
                _result = DsTkCompareService.Compare(_revitSnapshot, tk, meta, _scheduleQty);

                var note = _result.Note;
                if (!materialsOk && !string.IsNullOrWhiteSpace(materialsError))
                    note += $" Тип файла (RFA/surface): недоступен ({materialsError}).";

                var failedSources = _scheduleQty.Sources
                    .Where(s => !s.Found || !string.IsNullOrWhiteSpace(s.Message))
                    .Take(3)
                    .Select(s => $"{s.Code}: {s.Message ?? "не найдена"}")
                    .ToList();
                if (failedSources.Count > 0)
                    note += " Ведомости: " + string.Join("; ", failedSources)
                            + (_scheduleQty.Sources.Count(s => !s.Found || !string.IsNullOrWhiteSpace(s.Message)) > 3
                                ? "…"
                                : string.Empty);

                StatusText.Text = note;
                UpdateStats();
                BindRooms();
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK compare failed");
                StatusText.Text = ex.Message;
                _result = null;
                _scheduleQty = null;
                UpdateStats();
                BindRooms();
            }
            finally
            {
                _loading = false;
                UpdateDsActionButtons();
            }

            // После сверки — привязка ДС (отдельный путь, ошибки не сбрасывают таблицу).
            await RefreshDsBindAsync().ConfigureAwait(true);
            if (_result != null && StatusText != null)
            {
                var note = StatusText.Text ?? string.Empty;
                if (_boundDs != null && !note.Contains("ДС: №", StringComparison.Ordinal))
                    StatusText.Text = note + $" ДС: №{_boundDs.DsId} ({_boundDs.StatusDisplay}).";
                else if (_dsItems.Count > 1 && _dsItems.Count(i => i.CanEdit) > 1
                         && !note.Contains("черновиков", StringComparison.OrdinalIgnoreCase))
                    StatusText.Text = note + " Несколько черновиков TK_CHANGE — выберите ДС кнопкой «Выбрать…».";
                else if (_dsItems.Count == 0
                         && !note.Contains("ДС не создана", StringComparison.Ordinal)
                         && DsBadgeText?.Text?.Contains("ошибка", StringComparison.OrdinalIgnoreCase) != true)
                    StatusText.Text = note + " ДС не создана — можно создать пустой черновик.";
            }
        }

        async Task RefreshDsBindAsync(int? preferDsId = null)
        {
            if (_clientRequestId <= 0)
            {
                _dsItems = new List<DsTkChangeItem>();
                _boundDs = null;
                ApplyDsBadge(null);
                UpdateDsActionButtons();
                return;
            }

            try
            {
                var items = await DsTkChangeService.ListTkChangeAsync(_clientRequestId).ConfigureAwait(true);
                _dsItems = items;

                if (preferDsId is > 0)
                {
                    _boundDs = items.FirstOrDefault(i => i.DsId == preferDsId.Value)
                               ?? _boundDs;
                }
                else if (_boundDs != null)
                {
                    _boundDs = items.FirstOrDefault(i => i.DsId == _boundDs.DsId)
                               ?? DsTkChangeBindState.PreferAutoSelect(items);
                }
                else
                {
                    _boundDs = DsTkChangeBindState.PreferAutoSelect(items);
                }

                // Нет черновика, но есть заблокированная ДС — покажем её в бейдже (не «не создана»).
                var badgeItem = _boundDs
                    ?? DsTkChangeBindState.PreferHubBadge(items);
                ApplyDsBadge(badgeItem);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK list failed");
                ApplyDsBadgeError(ex.Message);
            }

            UpdateDsActionButtons();
        }

        void ApplyDsBadge(DsTkChangeItem item)
        {
            if (DsBadgeText == null || DsBadgeBorder == null)
                return;

            if (item == null)
            {
                DsBadgeText.Text = "ДС: не создана";
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "#F1F5F9", "#475569", "#CBD5E1");
                return;
            }

            if (item.IsAccept == 1)
            {
                DsBadgeText.Text = $"ДС: утверждена №{item.DsId}";
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "#DCFCE7", "#166534", "#86EFAC");
                return;
            }

            if (item.IsAccept == 2)
            {
                DsBadgeText.Text = $"ДС: отказана №{item.DsId}";
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "#FEF2F2", "#DC2626", "#FECACA");
                return;
            }

            if (item.CardId != null)
            {
                DsBadgeText.Text = $"ДС: на согласовании №{item.DsId}";
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "#DBEAFE", "#1D4ED8", "#93C5FD");
                return;
            }

            DsBadgeText.Text = $"ДС: черновик №{item.DsId}";
            SetBadgeColors(DsBadgeBorder, DsBadgeText, "#FEF9C3", "#A16207", "#FDE68A");
        }

        void ApplyDsBadgeError(string message)
        {
            if (DsBadgeText == null || DsBadgeBorder == null)
                return;

            DsBadgeText.Text = "ДС: ошибка загрузки";
            DsBadgeText.ToolTip = message;
            SetBadgeColors(DsBadgeBorder, DsBadgeText, "#FEF2F2", "#DC2626", "#FECACA");
        }

        static void SetBadgeColors(System.Windows.Controls.Border border, TextBlock text, string bg, string fg, string borderBrush)
        {
            border.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bg));
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(borderBrush));
            text.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fg));
        }

        void QtyApplyMode_Checked(object sender, RoutedEventArgs e) =>
            UpdateDsActionButtons();

        async void ApplyQtyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dsBusy || _loading || _clientRequestId <= 0)
                return;

            if (_boundDs == null || !_boundDs.CanEdit)
            {
                MessageBox.Show(
                    this,
                    "Нужен редактируемый черновик ДС. Создайте ДС или выберите черновик.",
                    "Smart Remont",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var session = ExportRoomsApplication.CurrentSession;
            if (session?.HasGrant(DsTkChangeService.QtyUpdGrant) != true)
            {
                MessageBox.Show(
                    this,
                    $"Нет права {DsTkChangeService.QtyUpdGrant} — изменение объёмов в ДС недоступно.",
                    "Smart Remont",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_result == null)
            {
                MessageBox.Show(
                    this,
                    "Сначала дождитесь сверки.",
                    "Smart Remont",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var mode = ReadQtyApplyMode();
            if (mode == DsTkQtyApplyMode.Skip)
            {
                MessageBox.Show(
                    this,
                    "Выбран режим «Не передавать».\nВключите «Как в MySpace» или «Все qty≠».",
                    "Объёмы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var preview = DsTkChangeService.BuildQtyApplyPreview(_result, mode);
            if (preview.Candidates.Count == 0)
            {
                var hint = mode == DsTkQtyApplyMode.EditableOnly
                    ? "Нет qty≠ с полем ввода в MySpace (или нет client_material_id / объёма ведомости)."
                    : "Нет qty≠ для отправки (нужны расхождения с объёмом из ведомости и client_material_id).";
                MessageBox.Show(this, hint, "Объёмы", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var previewWindow = new DsTkQtyApplyPreviewWindow(
                preview,
                _boundDs.DsId,
                _boundDs.StatusDisplay)
            {
                Owner = this
            };
            if (previewWindow.ShowDialog() != true || !previewWindow.Confirmed)
                return;

            var candidates = preview.Candidates;
            _dsBusy = true;
            UpdateDsActionButtons();
            try
            {
                StatusText.Text = $"Отправка объёмов в ДС №{_boundDs.DsId}…";
                var apply = await DsTkChangeService
                    .ApplyQtyAsync(_clientRequestId, _boundDs.DsId, candidates)
                    .ConfigureAwait(true);

                var msg = $"Объёмы: отправлено {apply.Succeeded} из {apply.Attempted}.";
                if (apply.Failed > 0)
                    msg += $" Ошибок: {apply.Failed}.";
                StatusText.Text = msg + $" ДС №{_boundDs.DsId}.";

                if (apply.Failed > 0)
                {
                    var errText = string.Join("\n", apply.Errors.Take(12));
                    if (apply.Errors.Count > 12)
                        errText += $"\n… и ещё {apply.Errors.Count - 12}";
                    MessageBox.Show(
                        this,
                        msg + "\n\n" + errText,
                        "Частичная отправка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(
                        this,
                        msg + "\nПроверьте черновик в MySpace.",
                        "Объёмы отправлены",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK apply qty failed");
                MessageBox.Show(this, ex.Message, "Ошибка отправки объёмов", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _dsBusy = false;
                UpdateDsActionButtons();
            }
        }

        DsTkQtyApplyMode ReadQtyApplyMode()
        {
            if (QtyModeSkipRadio?.IsChecked == true)
                return DsTkQtyApplyMode.Skip;
            if (QtyModeAllRadio?.IsChecked == true)
                return DsTkQtyApplyMode.All;
            return DsTkQtyApplyMode.EditableOnly;
        }

        void UpdateDsActionButtons()
        {
            if (CreateDsButton == null || PickDsButton == null)
                return;

            var session = ExportRoomsApplication.CurrentSession;
            var hasAdd = session?.HasGrant("OA__RemontFormDSAdd") == true;
            var hasQtyUpd = session?.HasGrant(DsTkChangeService.QtyUpdGrant) == true;
            var busy = _loading || _dsBusy;
            var hasRequest = _clientRequestId > 0;
            var hasLocked = _dsItems.Any(i => i.IsLocked);
            var hasAny = _dsItems.Count > 0;
            var canEditBound = _boundDs != null && _boundDs.CanEdit;
            var mode = ReadQtyApplyMode();
            var preview = mode == DsTkQtyApplyMode.Skip || _result == null
                ? null
                : DsTkChangeService.BuildQtyApplyPreview(_result, mode);
            var candidates = preview?.Candidates.Count ?? 0;

            // Новую не создаём, если уже есть любая TK_CHANGE (черновик или прошедшая).
            CreateDsButton.IsEnabled = hasRequest && hasAdd && !busy && !hasAny;
            PickDsButton.IsEnabled = hasRequest && !busy && hasAny;
            CreateDsButton.ToolTip = !hasAdd
                ? "Нет права OA__RemontFormDSAdd"
                : hasLocked
                    ? "Уже есть ДС на согласовании/утверждённая — новую нельзя"
                    : hasAny
                        ? "Черновик уже есть — выберите его"
                        : "Создать пустой черновик TK_CHANGE";
            PickDsButton.ToolTip = hasLocked && !_dsItems.Any(i => i.CanEdit)
                ? "Есть только ДС не в статусе черновик — выбрать для правок нельзя"
                : "Выбрать существующий черновик ДС";

            if (QtyModeHintText != null)
            {
                QtyModeHintText.Text = mode switch
                {
                    DsTkQtyApplyMode.EditableOnly =>
                        "Только позиции с полем ввода объёма в MySpace. Перед отправкой покажем список изменений.",
                    DsTkQtyApplyMode.All =>
                        "Все qty≠ с ведомостью. В превью будет колонка, какие из них редактируются в MySpace.",
                    _ => "Объёмы в ДС не отправляются."
                };
            }

            if (QtyCandidateBadgeText != null)
            {
                if (mode == DsTkQtyApplyMode.Skip)
                {
                    QtyCandidateBadgeText.Text = "выкл";
                }
                else if (preview == null)
                {
                    QtyCandidateBadgeText.Text = "—";
                }
                else if (mode == DsTkQtyApplyMode.EditableOnly && preview.SkippedNotEditable > 0)
                {
                    QtyCandidateBadgeText.Text =
                        $"{candidates} уйдёт · {preview.SkippedNotEditable} пропуск";
                }
                else
                {
                    QtyCandidateBadgeText.Text = candidates == 1
                        ? "1 позиция"
                        : $"{candidates} позиций";
                }
            }

            if (ApplyQtyButton != null)
            {
                ApplyQtyButton.IsEnabled = hasRequest && canEditBound && hasQtyUpd && !busy
                                          && mode != DsTkQtyApplyMode.Skip
                                          && candidates > 0;
                ApplyQtyButton.Content = mode == DsTkQtyApplyMode.Skip
                    ? "Не передавать"
                    : "Проверить и отправить";
                ApplyQtyButton.ToolTip = !hasQtyUpd
                    ? $"Нет права {DsTkChangeService.QtyUpdGrant}"
                    : !canEditBound
                        ? "Нужен редактируемый черновик ДС"
                        : mode == DsTkQtyApplyMode.Skip
                            ? "Режим «Не передавать»"
                            : candidates == 0
                                ? "Нет qty≠ для выбранного режима"
                                : $"Открыть превью: {candidates} позиций → ДС №{_boundDs.DsId}";
            }

            var modeEnabled = !busy;
            if (QtyModeEditableRadio != null)
                QtyModeEditableRadio.IsEnabled = modeEnabled;
            if (QtyModeAllRadio != null)
                QtyModeAllRadio.IsEnabled = modeEnabled;
            if (QtyModeSkipRadio != null)
                QtyModeSkipRadio.IsEnabled = modeEnabled;
        }
        static Dictionary<int, RevitMaterialRowDto> BuildMaterialMeta(RevitMaterialReadResponse materials)
        {
            var map = new Dictionary<int, RevitMaterialRowDto>();
            if (materials?.Data == null)
                return map;

            foreach (var row in materials.Data)
            {
                if (row?.MaterialId is not > 0)
                    continue;
                map[row.MaterialId.Value] = row;
            }

            return map;
        }

        void UpdateStats()
        {
            if (_result == null)
            {
                StatRoomsValue.Text = "0";
                StatMatchValue.Text = "0";
                StatMissingRevitValue.Text = "0";
                StatNotExpectedValue.Text = "0";
                StatExtraValue.Text = "0";
                StatQtyMismatchValue.Text = "0";
                return;
            }

            StatRoomsValue.Text = _result.Rooms.Count.ToString(CultureInfo.InvariantCulture);
            StatMatchValue.Text = _result.MatchCount.ToString(CultureInfo.InvariantCulture);
            StatMissingRevitValue.Text = _result.MissingInRevitCount.ToString(CultureInfo.InvariantCulture);
            StatNotExpectedValue.Text = _result.NotExpectedInModelCount.ToString(CultureInfo.InvariantCulture);
            StatExtraValue.Text = _result.ExtraInRevitCount.ToString(CultureInfo.InvariantCulture);
            StatQtyMismatchValue.Text = _result.QtyMismatchCount.ToString(CultureInfo.InvariantCulture);
        }

        void BindRooms()
        {
            if (RoomsItemsControl == null || NoDataText == null)
                return;

            var problemsOnly = ProblemsOnlyCheckBox?.IsChecked == true;
            var rooms = (_result?.Rooms ?? new List<DsTkCompareRoom>())
                .Select(room =>
                {
                    var rows = problemsOnly
                        ? room.Rows.Where(r => r.IsProblem).ToList()
                        : room.Rows.ToList();

                    if (rows.Count == 0)
                        return null;

                    return new DsTkCompareRoomVm
                    {
                        RoomName = room.RoomName,
                        SummaryBadge = room.SummaryBadge,
                        Rows = rows
                    };
                })
                .Where(r => r != null)
                .ToList();

            RoomsItemsControl.ItemsSource = rooms;
            NoDataText.Visibility = rooms.Count == 0
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            NoDataText.Text = problemsOnly
                ? "Расхождений нет — проект совпадает с договором (ТК)."
                : "Нет данных для сверки (пустой ТК и/или нет SR_ID в модели).";
        }

        sealed class DsTkCompareRoomVm
        {
            public string RoomName { get; init; }
            public string SummaryBadge { get; init; }
            public List<DsTkCompareRow> Rows { get; init; }
        }
    }
}
