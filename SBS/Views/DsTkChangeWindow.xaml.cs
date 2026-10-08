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
        ClientMaterialTkSnapshot _tkSnapshot;
        ClientMaterialTkSnapshot _tkFlattened;
        Dictionary<int, RevitMaterialRowDto> _materialMeta = new();
        List<DsTkChangeItem> _dsItems = new();
        DsTkChangeItem _boundDs;
        /// <summary>Неутверждённая ДС площади: пока она есть, ДС ТК не отправляем.</summary>
        DsTkChangeItem _openRoomChange;
        /// <summary>Что должно стоять в ДС по модели (только строки с полем ввода).</summary>
        DsTkTargetResult _target;
        List<string> _modelRoomNames = new();
        bool _loading;
        bool _dsBusy;

        public DsTkChangeWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkArea(this);
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

        void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = MoreButton.ContextMenu;
            if (menu == null)
                return;
            menu.PlacementTarget = MoreButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        async void CreateDsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dsBusy || _clientRequestId <= 0)
                return;

            var session = ExportRoomsApplication.CurrentSession;
            if (session?.HasGrant("OA__RemontFormDSAdd") != true)
            {
                AppMessageBox.Show(
                    this,
                    "Нет права создавать ДС.",
                    "Smart Remont",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_dsItems.Count == 0)
                await RefreshDsBindAsync().ConfigureAwait(true);

            // Утверждённые ДС не мешают: у заявки их бывает несколько. Мешает только черновик.
            var existingDraft = DsTkChangeBindState.PreferAutoSelect(_dsItems);
            if (existingDraft != null)
            {
                AppMessageBox.Show(
                    this,
                    $"Уже есть черновик ДС №{existingDraft.DsId}. Объёмы уйдут в него, второй не нужен.",
                    "Черновик уже есть",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _boundDs = existingDraft;
                ApplyDsBadge(_boundDs);
                UpdateDsActionButtons();
                return;
            }

            var confirm = AppMessageBox.Show(
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
                await RebuildCompareAsync().ConfigureAwait(true);
                StatusText.Text = $"Создан черновик ДС №{created.DsId}. Объёмы сверяются с ДС.";
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK create failed");
                AppMessageBox.Show(this, ex.Message, "Ошибка создания ДС", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    AppMessageBox.Show(
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
                await RebuildCompareAsync().ConfigureAwait(true);
                StatusText.Text = $"Привязана ДС №{_boundDs.DsId} ({_boundDs.StatusDisplay}). Объёмы сверяются с ДС.";
                UpdateDsActionButtons();
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK pick failed");
                AppMessageBox.Show(this, ex.Message, "Ошибка выбора ДС", MessageBoxButton.OK, MessageBoxImage.Error);
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
                ["qty_project_alert"] = _result.QtyProjectAlertCount,
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
            root["tk"] = BuildExportTk(_tkFlattened ?? _tkSnapshot);
            return root;
        }

        static JObject BuildExportTk(ClientMaterialTkSnapshot tk)
        {
            var obj = new JObject
            {
                ["flattened"] = true,
                ["rows"] = new JArray()
            };
            if (tk?.ClientRequestId is > 0)
                obj["client_request_id"] = tk.ClientRequestId.Value;
            if (tk?.Rows == null || tk.Rows.Count == 0)
                return obj;

            var rows = (JArray)obj["rows"];
            foreach (var row in tk.Rows)
            {
                if (row == null)
                    continue;
                var o = new JObject
                {
                    ["is_set_member"] = row.IsSetMember
                };
                if (row.ClientMaterialId is > 0)
                    o["client_material_id"] = row.ClientMaterialId.Value;
                if (row.MaterialId is > 0)
                    o["material_id"] = row.MaterialId.Value;
                if (row.MaterialSetId is > 0)
                    o["material_set_id"] = row.MaterialSetId.Value;
                if (row.WorkSetId is > 0)
                    o["work_set_id"] = row.WorkSetId.Value;
                if (row.TkChangeId is > 0)
                    o["tk_change_id"] = row.TkChangeId.Value;
                if (row.MaterialCnt != null)
                    o["material_cnt"] = row.MaterialCnt.Value;
                o["is_material_cnt_input"] = row.IsMaterialCntInput == true;
                SetIfHas(o, "room_name", row.RoomName);
                SetIfHas(o, "work_set_name", row.WorkSetName);
                SetIfHas(o, "material_name", row.MaterialName);
                SetIfHas(o, "set_name", row.SetName);
                rows.Add(o);
            }

            obj["row_count"] = rows.Count;
            obj["set_member_count"] = tk.Rows.Count(r => r?.IsSetMember == true);
            return obj;
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
            obj["is_set_member"] = row.IsSetMember;
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

            if (RoomAreaService.GetPreferredPhase(_doc) == null)
            {
                StatusText.Text = $"Фаза «{RoomAreaService.PreferredPhaseName}» не найдена.";
                return;
            }

            _loading = true;
            StatusText.Text = "Сканирование SR_ID в модели и загрузка ТК…";
            UpdateDsActionButtons();

            try
            {
                _revitSnapshot = RoomMaterialsService.CollectSrId(_doc);
                _scheduleQty = TkQtyScheduleService.Collect(_doc);
                _modelRoomNames = RoomAreaService.CollectRooms(_doc)
                    .Select(r => r.RoomName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var scheduleFound = _scheduleQty.Sources.Count(s => s.Found);
                var scheduleLines = _scheduleQty.Lines.Count;
                ScanInfoMenuItem.Header =
                    $"SR_ID в модели: {_revitSnapshot.ElementsWithSrId}"
                    + (_revitSnapshot.UnassignedElements > 0
                        ? $", без комнаты: {_revitSnapshot.UnassignedElements}"
                        : string.Empty)
                    + $", ведомости qty: {scheduleFound}/{_scheduleQty.Sources.Count} · строк {scheduleLines}";

                var tkTask = ClientMaterialTkService.ReadAsync(_clientRequestId);
                var materialsTask = RevitMaterialsService.TryReadAsync(_clientRequestId);

                // Сверка не ждёт office /ds/read/ — иначе при 403/timeout «ничего не грузится».
                await Task.WhenAll(tkTask, materialsTask).ConfigureAwait(true);

                _tkSnapshot = await tkTask.ConfigureAwait(true);
                var (materials, materialsOk, materialsError) = await materialsTask.ConfigureAwait(true);
                _materialMeta = BuildMaterialMeta(materialsOk ? materials : null);

                // Сначала ДС (для эталона объёмов), потом сверка.
                await RefreshDsBindAsync().ConfigureAwait(true);
                await RebuildCompareAsync().ConfigureAwait(true);
                UpdateStatusAndAlerts(materialsOk, materialsError);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK compare failed");
                StatusText.Text = ex.Message;
                HideMismatchWarning();
                _result = null;
                _target = null;
                _scheduleQty = null;
                _tkSnapshot = null;
                _tkFlattened = null;
                UpdateStats();
                BindRooms();
            }
            finally
            {
                _loading = false;
                UpdateDsActionButtons();
            }
        }

        void UpdateStatusAndAlerts(bool materialsOk, string materialsError)
        {
            if (_result == null)
            {
                StatusText.Text = "Нет данных сверки.";
                HideMismatchWarning();
                return;
            }

            if (_boundDs == null)
                StatusText.Text = "Нет черновика ДС на изменение ТК — создайте его, чтобы сверить объёмы.";
            else if (_target == null)
                StatusText.Text = "Объёмы ДС не прочитаны — нажмите «Обновить».";
            else
            {
                var positions = _target.ToSend.Count;
                StatusText.Text = positions > 0
                    ? $"Уйдёт в ДС №{_boundDs.DsId}: {positions} поз. ({_target.ChangedLineCount} строк) + замеры комнат."
                    : $"Объёмы модели совпадают с ДС №{_boundDs.DsId}.";
            }

            if (!string.IsNullOrWhiteSpace(materialsError) && !materialsOk)
                StatusText.Text += $" Тип файла: {materialsError}";

            var groups = BuildWarningGroups();
            if (groups.Count == 0)
            {
                HideMismatchWarning();
                return;
            }

            if (MismatchWarningPanel == null || WarningGroupsItemsControl == null)
                return;

            WarningGroupsItemsControl.ItemsSource = groups;
            MismatchWarningPanel.Visibility = System.Windows.Visibility.Visible;
        }

        /// <summary>
        /// Сначала то, что останавливает отправку, потом предупреждения. Одна причина — один пункт:
        /// строка ведомости без помещения блокирует все позиции этой ведомости, но исправлять её один раз.
        /// </summary>
        List<WarningGroupVm> BuildWarningGroups()
        {
            var groups = new List<WarningGroupVm>();

            if (_openRoomChange != null)
            {
                groups.Add(new WarningGroupVm
                {
                    IsBlocking = true,
                    Title = $"ДС «Изменение площади» №{_openRoomChange.DsId} не утверждена ({_openRoomChange.StatusDisplay}). "
                            + "Сначала утвердите её: при утверждении она пересчитает ТК и затрёт объёмы ДС ТК."
                });
            }

            if (_target == null)
                return groups;

            var blockedCauses = GroupByReason(_target.Blocked);
            if (blockedCauses.Count > 0)
            {
                groups.Add(new WarningGroupVm
                {
                    IsBlocking = true,
                    Title = $"Исправьте модель — отправка остановлена: {blockedCauses.Count} "
                            + Plural(blockedCauses.Count, "причина", "причины", "причин")
                            + $", {_target.Blocked.Count} поз.",
                    Items = LimitItems(blockedCauses)
                });
            }

            var skippedCauses = GroupByReason(_target.Skipped);
            if (skippedCauses.Count > 0)
            {
                groups.Add(new WarningGroupVm
                {
                    Title = $"Не уйдёт из модели, правится в MySpace: {_target.Skipped.Count} поз.",
                    Items = LimitItems(skippedCauses)
                });
            }

            var extra = _target.ExtraInModel
                .GroupBy(i => i.MaterialName ?? "—")
                .Select(g => new WarningItemVm
                {
                    Text = g.Key,
                    Detail = JoinRooms(g.Select(i => i.RoomName))
                })
                .ToList();
            if (extra.Count > 0)
            {
                groups.Add(new WarningGroupVm
                {
                    Title = $"В модели есть, в ТК нет — замените или добавьте в MySpace: {extra.Count}",
                    Items = LimitItems(extra)
                });
            }

            var reportedSources = new HashSet<string>(
                _target.Blocked
                    .Select(p => p.BlockedBySourceCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c)),
                StringComparer.OrdinalIgnoreCase);
            var unassigned = _target.Unassigned
                .Where(i => string.IsNullOrWhiteSpace(i.SourceCode) || !reportedSources.Contains(i.SourceCode))
                .Select(i => new WarningItemVm { Text = i.MaterialName ?? "—", Detail = i.Text })
                .ToList();
            if (unassigned.Count > 0)
            {
                groups.Add(new WarningGroupVm
                {
                    Title = $"Строки ведомостей без помещения — объём не учтён: {unassigned.Count}",
                    Items = LimitItems(unassigned)
                });
            }

            return groups;
        }

        static List<WarningItemVm> GroupByReason(IEnumerable<DsTkTargetPosition> positions) =>
            positions
                .GroupBy(p => p.Reason ?? "—")
                .Select(g =>
                {
                    var materials = g.Select(p => StripId(p.MaterialName)).Distinct().ToList();
                    var rooms = JoinRooms(g.Select(p => p.RoomName));
                    return new WarningItemVm
                    {
                        Text = Capitalize(g.Key),
                        Detail = materials.Count == 1
                            ? $"{materials[0]} — {rooms}"
                            : $"{g.Count()} поз. — {rooms}"
                    };
                })
                .ToList();

        static List<WarningItemVm> LimitItems(List<WarningItemVm> items)
        {
            const int maxPerGroup = 6;
            if (items.Count <= maxPerGroup)
                return items;
            var shown = items.Take(maxPerGroup).ToList();
            shown.Add(new WarningItemVm { Text = $"… и ещё {items.Count - maxPerGroup} — полный список в «⋯ → Скопировать JSON отправки»" });
            return shown;
        }

        static string JoinRooms(IEnumerable<string> rooms)
        {
            var list = rooms
                .Select(r => string.IsNullOrWhiteSpace(r) ? "без помещения" : r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();
            const int maxRooms = 5;
            return list.Count <= maxRooms
                ? string.Join(", ", list)
                : string.Join(", ", list.Take(maxRooms)) + $" и ещё {list.Count - maxRooms}";
        }

        static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text.Substring(1);

        static string Plural(int n, string one, string few, string many)
        {
            var mod100 = n % 100;
            var mod10 = n % 10;
            if (mod100 is >= 11 and <= 14)
                return many;
            return mod10 switch
            {
                1 => one,
                >= 2 and <= 4 => few,
                _ => many
            };
        }

        static string StripId(string name) => TkMaterialCompareService.StripHtml(name) ?? "—";

        void HideMismatchWarning()
        {
            if (MismatchWarningPanel != null)
                MismatchWarningPanel.Visibility = System.Windows.Visibility.Collapsed;
            if (WarningGroupsItemsControl != null)
                WarningGroupsItemsControl.ItemsSource = null;
        }

        async Task RebuildCompareAsync()
        {
            if (_revitSnapshot == null || _tkSnapshot == null)
                return;

            var tkForCompare = CloneTkSnapshot(_tkSnapshot);
            var fromDs = false;
            _target = null;

            if (_boundDs != null && _boundDs.DsId > 0 && _clientRequestId > 0)
            {
                try
                {
                    var dsRows = await DsTkChangeService
                        .ReadTkMaterialsAsync(_clientRequestId, _boundDs.DsId)
                        .ConfigureAwait(true);
                    DsTkChangeService.ApplyDsQtyOverlay(tkForCompare, dsRows);
                    fromDs = true;
                    _target = DsTkTargetService.Build(dsRows, _scheduleQty, _modelRoomNames);
                    ExportRoomsApplication._logger?.Information(
                        "DS TK target ds={DsId} send={Send} lines={Lines} blocked={Blocked} skipped={Skipped} not_from_model={NotFromModel} extra={Extra}",
                        _boundDs.DsId,
                        _target.ToSend.Count,
                        _target.ChangedLineCount,
                        _target.Blocked.Count,
                        _target.Skipped.Count,
                        _target.NotFromModel.Count,
                        _target.ExtraInModel.Count);
                }
                catch (Exception ex)
                {
                    // Не сравниваем с договором вместо ДС: кандидаты посчитались бы от чужого эталона.
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "DS TK materials overlay failed ds={DsId}",
                        _boundDs.DsId);
                    _result = null;
                    _tkFlattened = null;
                    UpdateStats();
                    BindRooms();
                    UpdateDsActionButtons();
                    HideMismatchWarning();
                    StatusText.Text = $"Не удалось прочитать материалы ДС №{_boundDs.DsId}: {ex.Message}. "
                        + "Сверка остановлена, отправка недоступна. Нажмите «Обновить».";
                    return;
                }
            }

            ClientMaterialTkService.FlattenSets(tkForCompare);
            _tkFlattened = tkForCompare;
            ExportRoomsApplication._logger?.Information(
                "TK flatten cr={ClientRequestId} rows={Rows} set_members={Members} from_ds={FromDs}",
                _clientRequestId,
                tkForCompare.Rows?.Count ?? 0,
                tkForCompare.Rows?.Count(r => r?.IsSetMember == true) ?? 0,
                fromDs);

            _result = DsTkCompareService.Compare(
                _revitSnapshot,
                tkForCompare,
                _materialMeta,
                _scheduleQty,
                qtyBaselineFromDs: fromDs);
            _result = DsTkCompareService.ApplyTarget(_result, _target);

            UpdateStats();
            BindRooms();
            UpdateDsActionButtons();
            UpdateStatusAndAlerts(true, null);
        }

        static ClientMaterialTkSnapshot CloneTkSnapshot(ClientMaterialTkSnapshot source)
        {
            if (source == null)
                return null;

            return new ClientMaterialTkSnapshot
            {
                HasData = source.HasData,
                ClientRequestId = source.ClientRequestId,
                EmptyMessage = source.EmptyMessage,
                Rows = (source.Rows ?? new List<ClientMaterialRowDto>())
                    .Select(ClientMaterialTkService.CloneRow)
                    .Where(r => r != null)
                    .ToList()
            };
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
                var all = await DsTkChangeService.ListAllDsAsync(_clientRequestId).ConfigureAwait(true);
                var items = all
                    .Where(i => string.Equals(i.DsTypeCode, DsTkChangeService.TkChangeTypeCode, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _dsItems = items;
                _openRoomChange = DsTkChangeService.FindOpenRoomChange(all);

                if (preferDsId is > 0)
                {
                    _boundDs = items.FirstOrDefault(i => i.DsId == preferDsId.Value)
                               ?? _boundDs;
                }
                else if (_boundDs != null)
                {
                    // Выбранная вручную ДС могла уйти на согласование — тогда снова последний черновик.
                    var same = items.FirstOrDefault(i => i.DsId == _boundDs.DsId);
                    _boundDs = same is { CanEdit: true } ? same : DsTkChangeBindState.PreferAutoSelect(items);
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
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "SurfaceHoverBrush", "TextSecondaryBrush", "CardBorderBrush");
                return;
            }

            if (item.IsAccept == 1)
            {
                DsBadgeText.Text = $"ДС: утверждена №{item.DsId}";
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "SuccessSoftBrush", "SuccessTextBrush", "SuccessBorderBrush");
                return;
            }

            if (item.IsAccept == 2)
            {
                DsBadgeText.Text = $"ДС: отказана №{item.DsId}";
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "ErrorBackgroundBrush", "ErrorTextBrush", "ErrorBorderBrush");
                return;
            }

            if (item.CardId != null)
            {
                DsBadgeText.Text = $"ДС: на согласовании №{item.DsId}";
                SetBadgeColors(DsBadgeBorder, DsBadgeText, "InfoSoftBrush", "InfoTextBrush", "InfoBorderBrush");
                return;
            }

            DsBadgeText.Text = $"ДС: черновик №{item.DsId}";
            SetBadgeColors(DsBadgeBorder, DsBadgeText, "WarningSoftBrush", "WarningTextBrush", "WarningBorderBrush");
        }

        void ApplyDsBadgeError(string message)
        {
            if (DsBadgeText == null || DsBadgeBorder == null)
                return;

            DsBadgeText.Text = "ДС: ошибка загрузки";
            DsBadgeText.ToolTip = message;
            SetBadgeColors(DsBadgeBorder, DsBadgeText, "ErrorBackgroundBrush", "ErrorTextBrush", "ErrorBorderBrush");
        }

        void SetBadgeColors(System.Windows.Controls.Border border, TextBlock text, string bgKey, string fgKey, string borderKey)
        {
            border.Background = (System.Windows.Media.Brush)FindResource(bgKey);
            border.BorderBrush = (System.Windows.Media.Brush)FindResource(borderKey);
            text.Foreground = (System.Windows.Media.Brush)FindResource(fgKey);
        }

        async Task<List<MeasureApplyRoomDto>> CollectMeasureRoomsAsync()
        {
            StatusText.Text = "Сбор замеров комнат из модели…";
            var measures = RoomMeasurementsService.Collect(_doc);
            var systemRooms = await MeasuresService.ReadAsync(_clientRequestId).ConfigureAwait(true);
            return MeasuresService.BuildPayloadRooms(
                measures?.Rooms,
                MeasuresService.BuildRoomIdsByKey(systemRooms));
        }

        /// <summary>
        /// В буфер: тело запроса, который уйдёт при отправке, расчёт по строкам ДС и прочитанные
        /// строки ведомостей. Ничего не отправляет.
        /// </summary>
        async void CopySendJsonButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dsBusy || _loading)
                return;
            if (_target == null)
            {
                StatusText.Text = "Расчёт ещё не готов — дождитесь сверки или привяжите ДС.";
                return;
            }

            var target = _target;
            var root = new JObject
            {
                ["exported_at"] = DateTime.UtcNow.ToString("o"),
                ["client_request_id"] = _clientRequestId
            };
            SetIfHas(root, "document_title", _doc?.Title);
            if (_boundDs != null)
            {
                root["ds"] = new JObject
                {
                    ["ds_id"] = _boundDs.DsId,
                    ["status"] = _boundDs.StatusDisplay,
                    ["can_edit"] = _boundDs.CanEdit
                };
            }
            SetIfHas(root, "send_blocked_reason", ResolveSendBlockReason());

            List<MeasureApplyRoomDto> measureRooms = null;
            try
            {
                measureRooms = await CollectMeasureRoomsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK copy JSON: measures collect failed");
                root["measure_error"] = ex.Message;
            }

            root["request"] = DsTkChangeService.BuildApplyPayload(
                _clientRequestId,
                _boundDs?.DsId ?? 0,
                target.ToSend,
                measureRooms);
            root["target"] = DsTkTargetService.BuildDebugJson(target);

            if (_scheduleQty != null)
            {
                root["schedule_sources"] = new JArray(_scheduleQty.Sources.Select(s =>
                {
                    var o = new JObject
                    {
                        ["code"] = s.Code,
                        ["found"] = s.Found,
                        ["readable"] = s.Readable,
                        ["line_count"] = s.LineCount
                    };
                    SetIfHas(o, "schedule_found", s.ScheduleNameFound);
                    SetIfHas(o, "message", s.Message);
                    return o;
                }));
                root["schedule_lines"] = new JArray(_scheduleQty.Lines.Select(l => new JObject
                {
                    ["source"] = l.SourceCode,
                    ["schedule"] = l.ScheduleName,
                    ["room_name"] = l.RoomName,
                    ["material_id"] = l.MaterialId,
                    ["material_name"] = l.MaterialName,
                    ["qty"] = l.Quantity,
                    ["unit"] = l.Unit
                }));
                root["schedule_rows_without_id"] = new JArray(_scheduleQty.SkippedRows.Select(r => new JObject
                {
                    ["source"] = r.SourceCode,
                    ["schedule"] = r.ScheduleName,
                    ["room_name"] = r.RoomName,
                    ["text"] = r.Text,
                    ["qty"] = r.Quantity
                }));
            }

            try
            {
                System.Windows.Clipboard.SetText(root.ToString(Formatting.Indented));
                StatusText.Text = $"JSON скопирован: {target.ToSend.Count} поз. к отправке, "
                                  + $"замеров комнат {measureRooms?.Count ?? 0}. Ничего не отправлено.";
            }
            catch (Exception ex)
            {
                // Буфер бывает занят другим приложением.
                ExportRoomsApplication._logger?.Warning(ex, "DS TK copy JSON: clipboard failed");
                StatusText.Text = "Не удалось скопировать в буфер: " + ex.Message;
            }
        }

        async void ApplyQtyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dsBusy || _loading || _clientRequestId <= 0)
                return;

            var blockReason = ResolveSendBlockReason();
            if (blockReason != null)
            {
                AppMessageBox.Show(this, blockReason, "Отправка недоступна", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Замеры уходят вместе с объёмами: работы ДС MySpace считает по замерам комнат.
            List<MeasureApplyRoomDto> measureRooms;
            try
            {
                measureRooms = await CollectMeasureRoomsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK measures collect failed");
                StatusText.Text = "Замеры комнат не собраны — отправка остановлена.";
                AppMessageBox.Show(
                    this,
                    "Без замеров ДС ТК не отправляется: работы MySpace считает по ним.\n\n" + ex.Message,
                    "Замеры не собраны",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var positions = _target.ToSend;
            var preview = DsTkChangeService.BuildPreview(_target);
            var previewWindow = new DsTkQtyApplyPreviewWindow(
                preview,
                _boundDs.DsId,
                _boundDs.StatusDisplay,
                measureRooms.Count)
            {
                Owner = this
            };
            if (previewWindow.ShowDialog() != true || !previewWindow.Confirmed)
            {
                UpdateStatusAndAlerts(true, null);
                return;
            }

            var dsId = _boundDs.DsId;
            _dsBusy = true;
            UpdateDsActionButtons();
            try
            {
                StatusText.Text = $"Отправка замеров и объёмов в ДС №{dsId}…";
                var apply = await DsTkChangeService
                    .ApplyFromModelAsync(_clientRequestId, dsId, positions, measureRooms)
                    .ConfigureAwait(true);

                var msg = $"Замеры комнат: {apply.MeasureRooms}.\nВ ДС №{dsId} записано позиций: {apply.Applied}.";
                if (apply.RevertedToContract > 0)
                    msg += $"\nСовпали с договором, изменение из ДС убрано: {apply.RevertedToContract}.";
                msg += "\n\nРаботы и сумму ДС MySpace пересчитал сам. Проверьте ДС и отправьте на утверждение как обычно.";

                await RefreshDsBindAsync(preferDsId: dsId).ConfigureAwait(true);
                await RebuildCompareAsync().ConfigureAwait(true);
                UpdateStatusAndAlerts(true, null);
                StatusText.Text = $"Отправлено в ДС №{dsId}: {apply.Applied} поз. · " + StatusText.Text;

                AppMessageBox.Show(
                    this,
                    msg,
                    "ДС ТК отправлена",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "DS TK apply failed");
                StatusText.Text = "Не отправлено: в ДС и в замерах ничего не записано.";
                AppMessageBox.Show(
                    this,
                    ex.Message,
                    "Не отправлено",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _dsBusy = false;
                UpdateDsActionButtons();
            }
        }

        /// <summary>Почему сейчас отправлять нельзя; null — можно. brief — одна строка для подвала.</summary>
        string ResolveSendBlockReason(bool brief = false)
        {
            var session = ExportRoomsApplication.CurrentSession;
            if (session?.HasGrant(DsTkChangeService.QtyUpdGrant) != true)
                return "Нет права менять объёмы в ДС на изменение ТК.";
            if (session.HasGrant(DsTkChangeService.MeasureSaveGrant) != true)
                return "Нет права сохранять замеры: ДС ТК отправляется вместе с ними.";
            if (_boundDs == null || !_boundDs.CanEdit)
                return "Нет черновика ДС на изменение ТК — создайте его.";
            if (_openRoomChange != null)
            {
                if (brief)
                    return $"Отправка недоступна: сначала утвердите ДС «Изменение площади» №{_openRoomChange.DsId}.";
                return $"ДС «Изменение площади» №{_openRoomChange.DsId} не утверждена. "
                       + "Сначала утвердите её, потом отправляйте ДС ТК: при утверждении она пересчитает ТК "
                       + "и затрёт отправленные объёмы.";
            }
            if (_target == null)
                return "Объёмы ДС не прочитаны — нажмите «Обновить».";
            if (_target.Blocked.Count > 0)
            {
                var causes = GroupByReason(_target.Blocked);
                if (brief)
                {
                    return $"Отправка недоступна: {causes.Count} {Plural(causes.Count, "проблема", "проблемы", "проблем")} "
                           + $"в модели ({_target.Blocked.Count} поз.) — список выше.";
                }
                return "Есть позиции, которые нельзя посчитать по модели. Исправьте модель и нажмите «Обновить»:\n\n"
                       + string.Join("\n", causes.Take(10).Select(c => $"— {c.Text}\n   {c.Detail}"))
                       + (causes.Count > 10 ? $"\n… и ещё {causes.Count - 10}" : string.Empty);
            }
            if (_target.ToSend.Count == 0)
                return NothingToSendReason;
            return null;
        }

        const string NothingToSendReason = "Объёмы модели совпадают с ДС — отправлять нечего.";

        void UpdateDsActionButtons()
        {
            if (CreateDsButton == null || PickDsButton == null)
                return;

            var session = ExportRoomsApplication.CurrentSession;
            var hasAdd = session?.HasGrant("OA__RemontFormDSAdd") == true;
            var busy = _loading || _dsBusy;
            var hasRequest = _clientRequestId > 0;
            var hasDraft = _dsItems.Any(i => i.CanEdit);
            var hasAny = _dsItems.Count > 0;
            var lines = _target?.ChangedLineCount ?? 0;
            var positions = _target?.ToSend.Count ?? 0;

            CreateDsButton.IsEnabled = hasRequest && hasAdd && !busy && !hasDraft;
            PickDsButton.IsEnabled = hasRequest && !busy && hasAny;
            CreateDsButton.ToolTip = !hasAdd
                ? "Нет права создавать ДС"
                : hasDraft
                    ? "Черновик уже есть — объёмы уйдут в последний"
                    : "Создать пустой черновик ДС на изменение ТК";
            PickDsButton.ToolTip = "По умолчанию берётся последний черновик. Лишние черновики удалите в MySpace.";

            if (QtyCandidateBadgeText != null)
            {
                QtyCandidateBadgeText.Text = _target == null
                    ? "—"
                    : positions == 1 ? "1 позиция" : $"{positions} позиций";
                QtyCandidateBadgeText.ToolTip = _target == null ? null : $"строк с изменением: {lines}";
            }

            if (ApplyQtyButton != null)
            {
                var reason = _result == null ? "Сначала дождитесь сверки" : ResolveSendBlockReason();
                ApplyQtyButton.IsEnabled = hasRequest && !busy && reason == null;
                ApplyQtyButton.Content = _boundDs is { CanEdit: true }
                    ? $"Отправить в ДС №{_boundDs.DsId}…"
                    : "Отправить в ДС…";
                ApplyQtyButton.ToolTip = reason
                    ?? $"Предпросмотр, затем отправка: замеры комнат + {positions} поз. в ДС №{_boundDs?.DsId}";
            }

            if (SendBlockText != null)
            {
                var brief = _loading || _result == null ? null : ResolveSendBlockReason(brief: true);
                SendBlockText.Text = brief ?? string.Empty;
                SendBlockText.Visibility = brief == null
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible;
                SendBlockText.Foreground = (System.Windows.Media.Brush)FindResource(
                    brief == NothingToSendReason ? "TextSecondaryBrush" : "ErrorTextBrush");
            }
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
                StatExtraValue.Text = "0";
                StatQtyMismatchValue.Text = "0";
                return;
            }

            StatRoomsValue.Text = _result.Rooms.Count.ToString(CultureInfo.InvariantCulture);
            StatMatchValue.Text = _result.MatchCount.ToString(CultureInfo.InvariantCulture);
            StatMissingRevitValue.Text = _result.MissingInRevitCount.ToString(CultureInfo.InvariantCulture);
            StatExtraValue.Text = _result.ExtraInRevitCount.ToString(CultureInfo.InvariantCulture);
            StatQtyMismatchValue.Text = _target == null
                ? "—"
                : _target.ToSend.Count.ToString(CultureInfo.InvariantCulture);
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
                        ? room.Rows.Where(r => r.NeedsAttention).ToList()
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
                ? "Проблем нет: состав совпадает с ТК, все объёмы модели можно отправить."
                : "Нет данных для сверки (пустой ТК и/или нет SR_ID в модели).";
        }

        sealed class DsTkCompareRoomVm
        {
            public string RoomName { get; init; }
            public string SummaryBadge { get; init; }
            public List<DsTkCompareRow> Rows { get; init; }
        }

        sealed class WarningGroupVm
        {
            public string Title { get; init; }
            public bool IsBlocking { get; init; }
            public List<WarningItemVm> Items { get; init; } = new();
        }

        sealed class WarningItemVm
        {
            public string Text { get; init; }
            public string Detail { get; init; }
        }
    }
}
