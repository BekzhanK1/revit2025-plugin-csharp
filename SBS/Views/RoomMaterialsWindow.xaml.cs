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
        RoomMaterialsSnapshot _snapshot;
        List<RoomMaterialsRoomVm> _rooms;

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
            _rooms = _snapshot.Rooms.Select(ToRoomVm).ToList();
            BindRooms(_rooms);
            DetailsItemsControl.ItemsSource = BuildDetails(_snapshot);

            FormulaHintTextBlock.Text =
                $"{RoomPaintScheduleService.LitersFormula} — {RoomPaintScheduleService.LitersFormulaNote}";

            UpdateStatusText();
            await ValidateCatalogAsync().ConfigureAwait(true);
        }

        async void RetryValidationButton_Click(object sender, RoutedEventArgs e)
        {
            await ValidateCatalogAsync().ConfigureAwait(true);
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

        async Task ValidateCatalogAsync()
        {
            if (_validationInProgress)
                return;

            if (_rooms == null || _rooms.Count == 0)
            {
                ShowCatalogBanner(
                    "Нет позиций для проверки каталога.",
                    "#FFFBEB", "#FDE68A", "#92400E");
                return;
            }

            var allIds = _rooms
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
                return;
            }

            if (ExportRoomsApplication.CurrentSession == null
                || string.IsNullOrWhiteSpace(ExportRoomsApplication.CurrentSession.AccessToken))
            {
                ShowCatalogBanner(
                    "Проверка каталога недоступна: войдите в Smart Remont через главное окно плагина.",
                    "#FFFBEB", "#FDE68A", "#92400E");
                UpdateStatusText("Проверка каталога: требуется авторизация.");
                return;
            }

            _validationInProgress = true;
            RetryValidationButton.IsEnabled = false;

            try
            {
                ShowCatalogLoading(
                    $"Отправляем {ids.Count} уникальных ID на сервер…\nPOST {Configs.MaterialValidationUrl}");

                var result = await MaterialValidationService.ValidateMaterialIdsAsync(ids)
                    .ConfigureAwait(true);

                _rooms = ApplyCatalogStatuses(_rooms, result.FoundIds);
                BindRooms(_rooms);

                var foundRows = _rooms.SelectMany(r => r.TableRows).Count(r => r.CatalogStatusKey == "found");
                var missingRows = _rooms.SelectMany(r => r.TableRows).Count(r => r.CatalogStatusKey == "missing");

                var skippedNote = skippedNonNumeric > 0
                    ? $" Пропущено текстовых кодов: {skippedNonNumeric}."
                    : string.Empty;

                ShowCatalogBanner(
                    $"Каталог проверен. Запрос: POST {result.RequestUrl}\n"
                    + $"Отправлено уникальных числовых ID: {result.RequestedCount}. "
                    + $"Найдено в базе: {result.FoundIds.Count}. "
                    + $"Строк: зелёных {foundRows}, красных {missingRows}."
                    + skippedNote,
                    "#ECFDF5", "#A7F3D0", "#065F46");

                UpdateStatusText(
                    $"Каталог: найдено {foundRows}, нет в базе {missingRows} (из {result.RequestedCount} числовых ID)."
                    + skippedNote);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Material validation failed");
                ShowCatalogBanner(
                    $"Ошибка проверки каталога:\n{ex.Message}",
                    "#FEF2F2", "#FECACA", "#991B1B");
                UpdateStatusText($"Проверка каталога: {ex.Message}");
            }
            finally
            {
                _validationInProgress = false;
                RetryValidationButton.IsEnabled = true;
            }
        }

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
                        CatalogStatusKey = statusKey
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

        void UpdateStatusText(string validationNote = null)
        {
            var rooms = _rooms ?? new List<RoomMaterialsRoomVm>();
            var snapshot = _snapshot;
            var hasRows = rooms.Count > 0;
            var positionCount = rooms.Sum(r => r.TableRows.Count);
            var instanceCount = rooms.Sum(r => r.TableRows.Sum(t => t.InstanceCount));
            var paintLiters = rooms
                .SelectMany(r => r.TableRows)
                .Sum(t => t.Liters ?? 0d);

            if (!string.IsNullOrWhiteSpace(validationNote) && !hasRows)
            {
                StatusTextBlock.Text = validationNote;
                return;
            }

            if (hasRows)
            {
                var unassigned = snapshot?.UnassignedElements ?? 0;
                var unassignedNote = unassigned > 0 ? $" Не привязано к комнате: {unassigned}." : string.Empty;
                var catalogNote = string.IsNullOrWhiteSpace(validationNote) ? string.Empty : $" {validationNote}";
                StatusTextBlock.Text =
                    $"Помещений: {rooms.Count}. Позиций: {positionCount} (экземпляров: {instanceCount}). "
                    + $"Краска: {paintLiters:0.##} л. "
                    + $"ID: ADSK {snapshot.OnlyAdskCodeCount}, классификатор {snapshot.OnlyClassifierCodeCount}, "
                    + $"ЭОМ {snapshot.OnlyErboEomCodeCount}, оба/несколько {snapshot.BothCodesCount}"
                    + (snapshot.ConflictingCodesCount > 0 ? $", расхождения {snapshot.ConflictingCodesCount}" : "")
                    + $".{unassignedNote}{catalogNote}";
            }
            else if (snapshot?.PaintSource?.Found == true)
            {
                StatusTextBlock.Text = "Ведомость краски найдена, но строки не сопоставились с помещениями.";
            }
            else if (snapshot?.TotalElements > 0)
            {
                StatusTextBlock.Text =
                    $"В модели {snapshot.TotalElements} элементов, но ни один не привязан к помещению.";
            }
            else
            {
                StatusTextBlock.Text = string.IsNullOrWhiteSpace(validationNote)
                    ? "Не найдено данных ни в ведомости краски, ни в модели."
                    : validationNote;
            }
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
                           + "  зелёная строка — ID в каталоге, красная — нет в базе\n"
                           + "  текстовые коды (Furn и др.) не отправляются на проверку\n"
                           + "  POST /common/catalog/validate_material_ids/ · body: { material_ids: [...] }",
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
            DetailsToggleButton.Content = _detailsVisible ? "Скрыть детали" : "Детали расчёта";
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
    }

    sealed class RoomMaterialsDetailVm
    {
        public string LineText { get; init; }
        public Brush StatusBrush { get; init; }
    }
}
