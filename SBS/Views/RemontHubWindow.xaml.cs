using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.DTO;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SmartRemont.ExportRooms.Views
{
    public partial class RemontHubWindow : Window
    {
        readonly Document _doc;

        const string DsAreaSubtitle = "Отправка площадей помещений в Smart Remont";
        const string MeasuresSubtitle = "Замеры из спецификаций Revit";
        const string MeasuresFromCodeSubtitle = "Площадь стен из модели Revit";
        const string MeasuresCompareSubtitle = "Спецификация и код — в одной таблице с подсветкой";
        const string RoomMaterialsSubtitle = "Краска из ведомости и элементы модели по помещениям";
        const string TypeParametersSubtitle = "Категория, семейство, тип и параметры типа";
        const string DsTkSubtitle = "Изменение технологической карты";

        public RemontHubWindow(Document doc)
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(CompanyLogoImage);
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
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
            await RefreshEventStatusesAsync().ConfigureAwait(true);
        }

        void SetupFeatureButtons()
        {
            ConfigureFeatureButton(DsAreaChangeButton, "\uE8A7", DsAreaSubtitle);
            ConfigureFeatureButton(MeasuresButton, "\uE8B7", MeasuresSubtitle);
            ConfigureFeatureButton(MeasuresFromCodeButton, "\uE8F1", MeasuresFromCodeSubtitle);
            ConfigureFeatureButton(MeasuresCompareButton, "\uE8AB", MeasuresCompareSubtitle);
            ConfigureFeatureButton(RoomMaterialsButton, "\uE719", RoomMaterialsSubtitle);
            ConfigureFeatureButton(TypeParametersButton, "\uE8B9", TypeParametersSubtitle);
            ConfigureFeatureButton(DsTkChangeButton, "\uE8A5", DsTkSubtitle);
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
                ClientRequestIdText.Text = "—";
                RemontIdText.Text = "—";
                ClientNameText.Text = "—";
                ResidentNameText.Text = "—";
                FlatNumText.Text = "—";
                PresetNameText.Text = "—";
                RemontSubtitleText.Text = "Выберите действие";
                return;
            }

            ClientRequestIdText.Text = remont.ClientRequestId.ToString();
            RemontIdText.Text = remont.RemontId.HasValue ? remont.RemontId.Value.ToString() : "—";
            ClientNameText.Text = DisplayOrDash(remont.ClientName);
            ResidentNameText.Text = DisplayOrDash(remont.ResidentName);
            FlatNumText.Text = DisplayOrDash(remont.FlatNum);
            PresetNameText.Text = DisplayOrDash(remont.PresetName);
            RemontSubtitleText.Text = BuildSubtitle(remont);
        }

        static string BuildSubtitle(RemontOption remont)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(remont.ResidentName))
                parts.Add(remont.ResidentName.Trim());
            if (!string.IsNullOrWhiteSpace(remont.FlatNum))
                parts.Add($"кв. {remont.FlatNum.Trim()}");
            if (!string.IsNullOrWhiteSpace(remont.PresetName))
                parts.Add(remont.PresetName.Trim());
            return parts.Count > 0 ? string.Join(" · ", parts) : "Выберите действие";
        }

        static string DisplayOrDash(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

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

        void TypeParametersButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new TypeParameterChangeWindow(_doc);
            window.Owner = this;
            window.ShowDialog();
        }

        void DsTkChangeButton_Click(object sender, RoutedEventArgs e) =>
            AppMessageDialog.ShowInDevelopment(
                this,
                "В разработке",
                "ДС по изменению ТК",
                "Раздел находится в разработке. Скоро здесь можно будет оформить изменение технологической карты.");

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Всегда возвращаем true, чтобы команда вернула Result.Succeeded.
            // Result.Cancelled откатывает все транзакции сессии, включая уже закоммиченные.
            DialogResult = true;
            Close();
        }
    }
}
