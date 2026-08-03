using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public partial class RoomMaterialsWindow : Window
    {
        readonly Document _doc;
        bool _detailsVisible;
        bool _validationInProgress;
        bool _tkValidationInProgress;
        RoomMaterialsSnapshot _snapshot;
        List<RoomMaterialsRoomVm> _rooms;
        List<RoomMaterialsRoomVm> _allRooms;     // current view (may include tk_only rows)
        List<RoomMaterialsRoomVm> _baseRooms;    // Revit-only rows, never includes tk_only
        ClientMaterialTkSnapshot _tkSnapshot;

        public RoomMaterialsWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            Loaded += RoomMaterialsWindow_Loaded;
        }

        async void RoomMaterialsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _snapshot = RoomMaterialsService.Collect(_doc);
            _baseRooms = _snapshot.Rooms.Select(ToRoomVm).ToList();
            _allRooms = _baseRooms;
            _rooms = _allRooms;
            BindRooms(_rooms);
            DetailsItemsControl.ItemsSource = BuildDetails(_snapshot);

            UpdateSummary();
            UpdateStatusText();
            await RefreshValidationsAsync().ConfigureAwait(true);
        }

        async void RetryValidationButton_Click(object sender, RoutedEventArgs e) =>
            await RefreshValidationsAsync().ConfigureAwait(true);

        async Task RefreshValidationsAsync()
        {
            var catalogChecked = await ValidateCatalogAsync().ConfigureAwait(true);
            await ValidateTkAsync(catalogChecked).ConfigureAwait(true);
        }

        void FinalizeMaterialView(string validationNote = null, string tkNote = null)
        {
            _allRooms = ApplyUnifiedStatuses(_allRooms);
            _rooms = ApplyProblemFilter(_allRooms);
            BindRooms(_rooms);
            UpdateSummary();
            if (validationNote != null || tkNote != null)
                UpdateStatusText(validationNote, tkNote);
        }

        void BindRooms(List<RoomMaterialsRoomVm> rooms)
        {
            RoomsItemsControl.ItemsSource = null;
            RoomsItemsControl.ItemsSource = rooms;

            var hasRows = rooms.Count > 0;
            RoomsItemsControl.Visibility = hasRows
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            NoDataTextBlock.Visibility = hasRows
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        async Task<bool> ValidateCatalogAsync()
        {
            if (_validationInProgress)
                return false;

            if (_allRooms == null || _allRooms.Count == 0)
            {
                ShowCatalogBanner(
                    "Нет позиций для проверки каталога.",
                    "#FFFBEB", "#FDE68A", "#92400E");
                return false;
            }

            var allIds = _allRooms
                .SelectMany(r => r.TableRows)
                .Select(r => r.ProductId)
                .Where(id => !IsDash(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ids = allIds
                .Where(MaterialValidationService.IsNumericMaterialId)
                .ToList();

            var skippedNonNumeric = allIds.Count - ids.Count;

            if (ids.Count == 0)
            {
                ShowCatalogBanner(
                    skippedNonNumeric > 0
                        ? $"Нет числовых ID для проверки. Пропущено текстовых кодов: {skippedNonNumeric} (например Furn)."
                        : "Нет ID для проверки — у позиций не заполнены коды материалов.",
                    "#FFFBEB", "#FDE68A", "#92400E");
                return false;
            }

            if (ExportRoomsApplication.CurrentSession == null
                || string.IsNullOrWhiteSpace(ExportRoomsApplication.CurrentSession.AccessToken))
            {
                ShowCatalogBanner(
                    "Проверка каталога недоступна: войдите в Smart Remont через главное окно плагина.",
                    "#FFFBEB", "#FDE68A", "#92400E");
                UpdateStatusText("Проверка каталога: требуется авторизация.");
                return false;
            }

            _validationInProgress = true;
            RetryValidationButton.IsEnabled = false;

            try
            {
                ShowCatalogLoading($"Проверка каталога: {ids.Count} ID…");

                var result = await MaterialValidationService.ValidateMaterialIdsAsync(ids)
                    .ConfigureAwait(true);

                _baseRooms = ApplyCatalogStatuses(_baseRooms, result.FoundIds);
                _allRooms = _baseRooms;

                CatalogBanner.Visibility = System.Windows.Visibility.Collapsed;

                var missingRows = _allRooms
                    .SelectMany(r => r.TableRows)
                    .Count(r => r.CatalogStatusKey == "missing");
                var skippedNote = skippedNonNumeric > 0
                    ? $" Текстовых кодов без проверки: {skippedNonNumeric}."
                    : string.Empty;

                _lastCatalogNote =
                    $"Каталог: {result.RequestedCount} ID, нет в системе {missingRows}."
                    + skippedNote;

                return true;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Material validation failed");
                ShowCatalogBanner(
                    $"Ошибка проверки каталога:\n{ex.Message}",
                    "#FEF2F2", "#FECACA", "#991B1B");
                _lastCatalogNote = $"Проверка каталога: {ex.Message}";
                return false;
            }
            finally
            {
                _validationInProgress = false;
                RetryValidationButton.IsEnabled = true;
            }
        }

        string _lastCatalogNote;

        static List<RoomMaterialsRoomVm> ApplyCatalogStatuses(
            List<RoomMaterialsRoomVm> rooms,
            IReadOnlySet<string> foundIds)
        {
            return rooms.Select(room => new RoomMaterialsRoomVm
            {
                RoomName = room.RoomName,
                SummaryBadge = room.SummaryBadge,
                HasRowsVisibility = room.HasRowsVisibility,
                TableRows = room.TableRows.Select(row =>
                {
                    var statusKey = ResolveCatalogStatusKey(row.ProductId, foundIds);
                    return new RoomMaterialTableRowVm
                    {
                        Name = row.Name,
                        ProductId = row.ProductId,
                        QuantityDisplay = row.QuantityDisplay,
                        InstanceCount = row.InstanceCount,
                        Liters = row.Liters,
                        CatalogStatusKey = statusKey,
                        TkStatusKey = row.TkStatusKey
                    };
                }).ToList()
            }).ToList();
        }

        static string ResolveCatalogStatusKey(string productId, IReadOnlySet<string> foundIds)
        {
            if (IsDash(productId) || !MaterialValidationService.IsNumericMaterialId(productId))
                return string.Empty;

            return foundIds.Contains(productId) ? "found" : "missing";
        }

        async Task ValidateTkAsync(bool catalogChecked)
        {
            if (_tkValidationInProgress)
                return;

            var remont = ExportRoomsApplication.SelectedRemont;
            if (remont == null || remont.ClientRequestId <= 0)
            {
                FinalizeMaterialView(_lastCatalogNote, "ТК: нет client_request_id для загрузки.");
                return;
            }

            if (ExportRoomsApplication.CurrentSession == null
                || string.IsNullOrWhiteSpace(ExportRoomsApplication.CurrentSession.AccessToken))
            {
                FinalizeMaterialView(_lastCatalogNote, "ТК: требуется авторизация.");
                return;
            }

            _tkValidationInProgress = true;

            try
            {
                _tkSnapshot = await ClientMaterialTkService
                    .ReadAsync(remont.ClientRequestId)
                    .ConfigureAwait(true);

                _allRooms = ApplyTkComparison(_baseRooms, _tkSnapshot);

                var tkNote = _tkSnapshot != null && _tkSnapshot.HasData
                    ? BuildTkStatusNote(_tkSnapshot, _allRooms)
                    : _tkSnapshot?.EmptyMessage ?? "ТК не загружен.";

                FinalizeMaterialView(
                    catalogChecked ? _lastCatalogNote : null,
                    tkNote);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "TK material read failed");
                FinalizeMaterialView(_lastCatalogNote, $"ТК: {ex.Message}");
            }
            finally
            {
                _tkValidationInProgress = false;
            }
        }

        static int CountRowsByStatus(List<RoomMaterialsRoomVm> rooms, string statusKey) =>
            (rooms ?? new List<RoomMaterialsRoomVm>())
                .SelectMany(r => r.TableRows)
                .Count(r => string.Equals(r.StatusKey, statusKey, StringComparison.OrdinalIgnoreCase));

        static string BuildTkStatusNote(ClientMaterialTkSnapshot tk, List<RoomMaterialsRoomVm> rooms)
        {
            if (tk == null || !tk.HasData)
                return tk?.EmptyMessage ?? "ТК не загружен.";

            var rows = rooms.SelectMany(r => r.TableRows).ToList();
            var inTk = rows.Count(r => r.StatusKey == "in_tk");
            var notInTk = rows.Count(r => r.StatusKey == "not_in_tk");
            var tkOnly = rows.Count(r => r.StatusKey == "tk_only");

            return $"Совпадает с ТК: {inTk}. Нет в ТК: {notInTk}. Только в ТК: {tkOnly}.";
        }

        void UpdateSummary()
        {
            var rooms = _allRooms ?? _rooms ?? new List<RoomMaterialsRoomVm>();
            var rows = rooms.SelectMany(r => r.TableRows).ToList();

            StatRoomsValue.Text = rooms.Count.ToString(CultureInfo.InvariantCulture);
            StatTotalValue.Text = rows.Count.ToString(CultureInfo.InvariantCulture);
            StatInTkValue.Text = rows.Count(r => r.StatusKey == "in_tk")
                .ToString(CultureInfo.InvariantCulture);
            StatMissingSystemValue.Text = rows.Count(r => r.StatusKey == "missing_system")
                .ToString(CultureInfo.InvariantCulture);
            StatNotInTkValue.Text = rows.Count(r => r.StatusKey == "not_in_tk")
                .ToString(CultureInfo.InvariantCulture);
            StatTkOnlyValue.Text = rows.Count(r => r.StatusKey == "tk_only")
                .ToString(CultureInfo.InvariantCulture);
        }

        static List<RoomMaterialsRoomVm> ApplyUnifiedStatuses(List<RoomMaterialsRoomVm> rooms) =>
            (rooms ?? new List<RoomMaterialsRoomVm>())
                .Select(room => new RoomMaterialsRoomVm
                {
                    RoomName = room.RoomName,
                    SummaryBadge = room.SummaryBadge,
                    HasRowsVisibility = room.HasRowsVisibility,
                    TableRows = room.TableRows
                        .Select(row =>
                        {
                            var (statusKey, statusDisplay) = ResolveMaterialStatus(
                                row.CatalogStatusKey,
                                row.TkStatusKey,
                                row.ProductId);
                            return CloneRow(row, row.TkStatusKey, statusKey, statusDisplay);
                        })
                        .ToList()
                })
                .ToList();

        static (string StatusKey, string StatusDisplay) ResolveMaterialStatus(
            string catalogStatusKey,
            string tkStatusKey,
            string productId)
        {
            if (catalogStatusKey == "missing")
                return ("missing_system", "Отсутствует в системе");

            if (tkStatusKey == "tk_only")
                return ("tk_only", "Только в ТК");

            if (tkStatusKey == "not_in_tk")
                return ("not_in_tk", "Нет в ТК");

            if (tkStatusKey == "in_tk")
                return ("in_tk", "Совпадает с ТК");

            if (catalogStatusKey == "found")
                return ("in_system", "В системе");

            if (!IsDash(productId) && !MaterialValidationService.IsNumericMaterialId(productId))
                return ("no_check", "Без проверки");

            return ("neutral", "—");
        }

        static List<RoomMaterialsRoomVm> ApplyTkComparison(
            List<RoomMaterialsRoomVm> rooms,
            ClientMaterialTkSnapshot tk)
        {
            if (rooms == null)
                return new List<RoomMaterialsRoomVm>();

            if (tk == null || !tk.HasData)
                return rooms.Select(ClearTkStatuses).ToList();

            var tkByRoom = TkMaterialCompareService.BuildEntriesByRoomKey(tk.Rows);
            var result = new List<RoomMaterialsRoomVm>();
            var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var room in rooms)
            {
                var key = DsAreaCompareService.GetRoomCompareKey(room.RoomName);
                processedKeys.Add(key);
                tkByRoom.TryGetValue(key, out var tkEntries);
                result.Add(BuildRoomWithTk(room, tkEntries));
            }

            foreach (var kvp in tkByRoom)
            {
                if (processedKeys.Contains(kvp.Key))
                    continue;

                var roomName = tk.Rows?
                    .FirstOrDefault(r => string.Equals(
                        DsAreaCompareService.GetRoomCompareKey(r.RoomName ?? string.Empty),
                        kvp.Key,
                        StringComparison.OrdinalIgnoreCase))
                    ?.RoomName?.Trim() ?? kvp.Key;

                result.Add(BuildTkOnlyRoom(roomName, kvp.Value));
            }

            return result
                .OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static RoomMaterialsRoomVm ClearTkStatuses(RoomMaterialsRoomVm room) =>
            new RoomMaterialsRoomVm
            {
                RoomName = room.RoomName,
                SummaryBadge = room.SummaryBadge,
                HasRowsVisibility = room.HasRowsVisibility,
                TableRows = room.TableRows
                    .Select(row => CloneRow(row, string.Empty))
                    .ToList()
            };

        static RoomMaterialsRoomVm BuildRoomWithTk(
            RoomMaterialsRoomVm room,
            List<TkMaterialEntry> tkEntries)
        {
            tkEntries ??= new List<TkMaterialEntry>();
            var rows = room.TableRows
                .Select(row =>
                {
                    var status = TkMaterialCompareService.ResolveRevitRowStatus(row.ProductId, tkEntries);
                    var tkKey = ToTkStatusKey(status);
                    var (statusKey, statusDisplay) = ResolveMaterialStatus(row.CatalogStatusKey, tkKey, row.ProductId);
                    return CloneRow(row, tkKey, statusKey, statusDisplay);
                })
                .ToList();

            var revitIds = rows.Select(r => r.ProductId);
            foreach (var tkOnly in TkMaterialCompareService.GetTkOnlyEntries(tkEntries, revitIds))
            {
                rows.Add(new RoomMaterialTableRowVm
                {
                    Name = tkOnly.IsSet
                        ? $"{tkOnly.DisplayName} (набор)"
                        : tkOnly.DisplayName,
                    ProductId = tkOnly.MaterialId,
                    QuantityDisplay = "—",
                    InstanceCount = 0,
                    CatalogStatusKey = string.Empty,
                    TkStatusKey = "tk_only",
                    StatusKey = "tk_only",
                    StatusDisplay = "Только в ТК"
                });
            }

            return new RoomMaterialsRoomVm
            {
                RoomName = room.RoomName,
                SummaryBadge = room.SummaryBadge,
                HasRowsVisibility = rows.Count > 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed,
                TableRows = rows
                    .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        static RoomMaterialsRoomVm BuildTkOnlyRoom(string roomName, List<TkMaterialEntry> tkEntries)
        {
            var rows = (tkEntries ?? new List<TkMaterialEntry>())
                .Select(entry => new RoomMaterialTableRowVm
                {
                    Name = entry.IsSet
                        ? $"{entry.DisplayName} (набор)"
                        : entry.DisplayName,
                    ProductId = entry.MaterialId,
                    QuantityDisplay = "—",
                    InstanceCount = 0,
                    CatalogStatusKey = string.Empty,
                    TkStatusKey = "tk_only",
                    StatusKey = "tk_only",
                    StatusDisplay = "Только в ТК"
                })
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new RoomMaterialsRoomVm
            {
                RoomName = roomName,
                SummaryBadge = $"только ТК {rows.Count}",
                HasRowsVisibility = rows.Count > 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed,
                TableRows = rows
            };
        }

        static RoomMaterialTableRowVm CloneRow(
            RoomMaterialTableRowVm row,
            string tkStatusKey,
            string statusKey = "",
            string statusDisplay = "")
        {
            if (string.IsNullOrEmpty(statusKey) || string.IsNullOrEmpty(statusDisplay))
            {
                var resolved = ResolveMaterialStatus(row.CatalogStatusKey, tkStatusKey, row.ProductId);
                statusKey = resolved.StatusKey;
                statusDisplay = resolved.StatusDisplay;
            }

            return new RoomMaterialTableRowVm
            {
                Name = row.Name,
                ProductId = row.ProductId,
                QuantityDisplay = row.QuantityDisplay,
                InstanceCount = row.InstanceCount,
                Liters = row.Liters,
                CatalogStatusKey = row.CatalogStatusKey,
                TkStatusKey = tkStatusKey,
                StatusKey = statusKey,
                StatusDisplay = statusDisplay
            };
        }

        static string ToTkStatusKey(TkMaterialCompareStatus status) => status switch
        {
            TkMaterialCompareStatus.InTk => "in_tk",
            TkMaterialCompareStatus.NotInTk => "not_in_tk",
            TkMaterialCompareStatus.TkOnly => "tk_only",
            _ => string.Empty
        };

        List<RoomMaterialsRoomVm> ApplyProblemFilter(List<RoomMaterialsRoomVm> rooms)
        {
            if (TkDifferencesOnlyCheckBox.IsChecked != true)
                return rooms?.ToList() ?? new List<RoomMaterialsRoomVm>();

            return (rooms ?? new List<RoomMaterialsRoomVm>())
                .Select(room => new RoomMaterialsRoomVm
                {
                    RoomName = room.RoomName,
                    SummaryBadge = room.SummaryBadge,
                    TableRows = room.TableRows
                        .Where(r => r.IsProblem)
                        .ToList(),
                    HasRowsVisibility = room.TableRows.Any(r => r.IsProblem)
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed
                })
                .Where(room => room.TableRows.Count > 0)
                .ToList();
        }

        void TkDifferencesOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_allRooms == null)
                return;

            _rooms = ApplyProblemFilter(_allRooms);
            BindRooms(_rooms);
        }

        void ShowCatalogLoading(string message)
        {
            CatalogBanner.Visibility = System.Windows.Visibility.Visible;
            CatalogBanner.Background = ToBrush("#EFF6FF");
            CatalogBanner.BorderBrush = ToBrush("#BFDBFE");
            CatalogLoadingPanel.Visibility = System.Windows.Visibility.Visible;
            CatalogLoadingTextBlock.Text = message;
            CatalogBannerTextBlock.Visibility = System.Windows.Visibility.Collapsed;
            CatalogBannerTextBlock.Text = string.Empty;
        }

        void ShowCatalogBanner(string message, string backgroundHex, string borderHex, string textHex)
        {
            CatalogLoadingPanel.Visibility = System.Windows.Visibility.Collapsed;
            CatalogBanner.Visibility = System.Windows.Visibility.Visible;
            CatalogBanner.Background = ToBrush(backgroundHex);
            CatalogBanner.BorderBrush = ToBrush(borderHex);
            CatalogBannerTextBlock.Visibility = System.Windows.Visibility.Visible;
            CatalogBannerTextBlock.Text = message;
            CatalogBannerTextBlock.Foreground = ToBrush(textHex);
        }

        void UpdateStatusText(string validationNote = null, string tkNote = null)
        {
            var rooms = _allRooms ?? _rooms ?? new List<RoomMaterialsRoomVm>();
            var snapshot = _snapshot;
            var hasRows = rooms.Count > 0;

            if (!string.IsNullOrWhiteSpace(validationNote) && !hasRows)
            {
                StatusTextBlock.Text = AppendNote(validationNote, tkNote);
                return;
            }

            if (hasRows)
            {
                var unassigned = snapshot?.UnassignedElements ?? 0;
                var unassignedNote = unassigned > 0 ? $" Не привязано к комнате: {unassigned}." : string.Empty;
                var catalogNote = string.IsNullOrWhiteSpace(validationNote) ? string.Empty : validationNote;
                var tkStatusNote = string.IsNullOrWhiteSpace(tkNote) ? string.Empty : $" {tkNote}";
                StatusTextBlock.Text = string.IsNullOrWhiteSpace(catalogNote)
                    ? $"{tkStatusNote.Trim()}{unassignedNote}".Trim()
                    : $"{catalogNote}{tkStatusNote}{unassignedNote}".Trim();
            }
            else if (snapshot?.PaintSource?.Found == true)
            {
                StatusTextBlock.Text = AppendNote(
                    "Ведомость краски найдена, но строки не сопоставились с помещениями.",
                    tkNote);
            }
            else if (snapshot?.TotalElements > 0)
            {
                StatusTextBlock.Text = AppendNote(
                    $"В модели {snapshot.TotalElements} элементов, но ни один не привязан к помещению.",
                    tkNote);
            }
            else
            {
                StatusTextBlock.Text = AppendNote(
                    string.IsNullOrWhiteSpace(validationNote)
                        ? "Не найдено данных ни в ведомости краски, ни в модели."
                        : validationNote,
                    tkNote);
            }
        }

        static string AppendNote(string primary, string secondary)
        {
            if (string.IsNullOrWhiteSpace(secondary))
                return primary;

            return string.IsNullOrWhiteSpace(primary)
                ? secondary
                : $"{primary} {secondary}";
        }

        static RoomMaterialsRoomVm ToRoomVm(RoomMaterialsRoomRow row)
        {
            var tableRows = BuildTableRows(row);
            var paintCount = row.PaintItems?.Count ?? 0;
            var modelCount = row.Items?.Count ?? 0;
            var instanceCount = tableRows.Sum(t => t.InstanceCount);

            var parts = new List<string>();
            if (paintCount > 0) parts.Add($"краска {paintCount}");
            if (modelCount > 0) parts.Add($"позиций {modelCount}");
            if (instanceCount > modelCount + paintCount)
                parts.Add($"экз. {instanceCount}");

            return new RoomMaterialsRoomVm
            {
                RoomName = row.RoomName,
                TableRows = tableRows,
                HasRowsVisibility = tableRows.Count > 0
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed,
                SummaryBadge = parts.Count > 0 ? string.Join(" · ", parts) : "нет данных"
            };
        }

        static List<RoomMaterialTableRowVm> BuildTableRows(RoomMaterialsRoomRow row)
        {
            var result = new List<RoomMaterialTableRowVm>();

            foreach (var paint in row.PaintItems ?? Enumerable.Empty<RoomPaintItem>())
            {
                result.Add(new RoomMaterialTableRowVm
                {
                    Name = FormatPaintName(paint),
                    ProductId = DisplayOrDash(paint.ProductId),
                    QuantityDisplay = "—",
                    InstanceCount = 1,
                    Liters = paint.Liters
                });
            }

            foreach (var item in row.Items ?? Enumerable.Empty<RoomMaterialItem>())
            {
                result.Add(new RoomMaterialTableRowVm
                {
                    Name = item.Name,
                    ProductId = FormatUnifiedId(item.AdskProductCode, item.ClassificationCode, item.ErboEomCode),
                    QuantityDisplay = FormatQuantity(item),
                    InstanceCount = item.Quantity
                });
            }

            return result
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string FormatPaintName(RoomPaintItem paint)
        {
            var name = DisplayOrDash(paint.MaterialType);
            if (name == "—")
                name = "Краска";

            var parts = new List<string>();
            if (paint.AreaM2.HasValue)
                parts.Add($"{paint.AreaM2.Value:0.##} м²");
            if (paint.Liters.HasValue)
                parts.Add($"{paint.Liters.Value:0.##} л");

            return parts.Count > 0 ? $"{name} · {string.Join(" · ", parts)}" : name;
        }

        static string FormatUnifiedId(string adsk, string classifier, string erboEom)
        {
            var hasAdsk = !IsDash(adsk);
            var hasClassifier = !IsDash(classifier);
            var hasErboEom = !IsDash(erboEom);

            if (hasAdsk && hasClassifier)
            {
                if (string.Equals(adsk, classifier, StringComparison.OrdinalIgnoreCase))
                    return adsk;
                return adsk;
            }

            if (hasAdsk)
                return adsk;

            if (hasClassifier)
                return classifier;

            if (hasErboEom)
                return erboEom;

            return "—";
        }

        static bool IsDash(string value) =>
            string.IsNullOrWhiteSpace(value) || value == "—";

        static string FormatQuantity(RoomMaterialItem item)
        {
            if (item.Quantity <= 1)
                return "—";

            return $"{item.Quantity} шт";
        }

        static List<RoomMaterialsDetailVm> BuildDetails(RoomMaterialsSnapshot snapshot)
        {
            var lines = new List<RoomMaterialsDetailVm>();
            var paint = snapshot.PaintSource;

            if (paint != null)
            {
                var statusColor = paint.Found ? "#1B6FC8" : "#CC6666";
                lines.Add(new RoomMaterialsDetailVm
                {
                    LineText = "Краска · ведомость Revit\n"
                               + $"  ожидается: «{paint.ScheduleNameExpected}»\n"
                               + $"  найдена: {paint.ScheduleNameFound ?? "—"}\n"
                               + $"  формула: {paint.LitersFormula}\n"
                               + $"  {paint.LitersFormulaNote}\n"
                               + $"  {paint.Message}",
                    StatusBrush = ToBrush(statusColor)
                });

                if (!string.IsNullOrWhiteSpace(paint.DetailLines))
                {
                    lines.Add(new RoomMaterialsDetailVm
                    {
                        LineText = "Строки ведомости:\n" + paint.DetailLines.Replace("\n", "\n  "),
                        StatusBrush = ToBrush("#374151")
                    });
                }
            }

            lines.Add(new RoomMaterialsDetailVm
            {
                LineText = "Элементы модели · источники ID\n"
                           + "  ADSK_Код изделия — shared-параметр (экземпляр → тип)\n"
                           + "  Код по классификатору — Blocks / Celite и др. (экземпляр → тип)\n"
                           + "  ERBO_ЭОМ_Наименование — розетки, выключатели и др. электрика (экземпляр → тип)\n"
                           + "  одинаковые позиции (название + ID) сворачиваются в кол-во\n"
                           + "  исключение: полы / стены / потолки / кровля — каждый сегмент отдельно\n"
                           + $"  только ADSK: {snapshot.OnlyAdskCodeCount}, только классификатор: {snapshot.OnlyClassifierCodeCount}\n"
                           + $"  только ЭОМ: {snapshot.OnlyErboEomCodeCount}, несколько параметров: {snapshot.BothCodesCount}\n"
                           + $"  расхождения: {snapshot.ConflictingCodesCount}\n"
                           + $"  всего с ID: {snapshot.ElementsWithCode}, без ID: {snapshot.SkippedWithoutCode}\n"
                           + $"  не привязано к комнате: {snapshot.UnassignedElements}\n"
                           + "  «Отсутствует в системе» — ID нет в каталоге\n"
                           + "  «Нет в ТК» / «Только в ТК» — сверка с текстовым конструктором\n"
                           + "  текстовые коды (Furn и др.) не отправляются на проверку каталога",
                StatusBrush = ToBrush("#374151")
            });

            return lines;
        }

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        static SolidColorBrush ToBrush(string hex) =>
            new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));

        void DetailsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _detailsVisible = !_detailsVisible;
            DetailsPanel.Visibility = _detailsVisible
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            DetailsToggleButton.Content = _detailsVisible ? "Скрыть" : "Детали";
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    sealed class RoomMaterialsRoomVm
    {
        public string RoomName { get; init; }
        public string SummaryBadge { get; init; }
        public System.Windows.Visibility HasRowsVisibility { get; init; }
        public List<RoomMaterialTableRowVm> TableRows { get; init; } = new();
    }

    sealed class RoomMaterialTableRowVm
    {
        public string Name { get; init; }
        public string ProductId { get; init; }
        public string QuantityDisplay { get; init; }
        public int InstanceCount { get; init; }
        public double? Liters { get; init; }
        public string CatalogStatusKey { get; init; } = string.Empty;
        public string TkStatusKey { get; init; } = string.Empty;
        public string StatusKey { get; init; } = string.Empty;
        public string StatusDisplay { get; init; } = "—";

        public bool IsProblem =>
            StatusKey is "missing_system" or "not_in_tk" or "tk_only";
    }

    sealed class RoomMaterialsDetailVm
    {
        public string LineText { get; init; }
        public Brush StatusBrush { get; init; }
    }
}
