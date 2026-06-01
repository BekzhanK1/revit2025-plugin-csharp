using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SmartRemont.ExportRooms.Views
{
    public partial class RemontHubWindow : Window
    {
        readonly Document _doc;
        bool _completed;

        public RemontHubWindow(Document doc)
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(CompanyLogoImage);
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            Loaded += RemontHubWindow_Loaded;
        }

        void RemontHubWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SetupFeatureButtons();
            BindRemontInfo(ExportRoomsApplication.SelectedRemont);
        }

        void SetupFeatureButtons()
        {
            ConfigureFeatureButton(
                DsAreaChangeButton,
                "\uE8A7",
                "Отправка площадей помещений в Smart Remont");
            ConfigureFeatureButton(
                MeasuresButton,
                "\uE8B7",
                "Замеры из спецификаций Revit");
            ConfigureFeatureButton(
                DsTkChangeButton,
                "\uE8A5",
                "Изменение технологической карты");
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

        void DsAreaChangeButton_Click(object sender, RoutedEventArgs e)
        {
            var summaryWindow = new SelectedRemontSummaryWindow(_doc);
            summaryWindow.Owner = this;
            summaryWindow.ShowDialog();

            if (summaryWindow.DialogResult == true)
            {
                _completed = true;
                SetStatus(summaryWindow.LastSuccessMessage ?? "Площади отправлены", isSuccess: true);
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

        void MeasuresButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new RoomMeasurementsWindow(_doc);
            window.Owner = this;
            window.ShowDialog();

            if (window.DialogResult == true)
            {
                _completed = true;
                SetStatus(window.LastSuccessMessage ?? "Замеры отправлены", isSuccess: true);
            }
        }

        void DsTkChangeButton_Click(object sender, RoutedEventArgs e) =>
            AppMessageDialog.ShowInDevelopment(
                this,
                "В разработке",
                "ДС по изменению ТК",
                "Раздел находится в разработке. Скоро здесь можно будет оформить изменение технологической карты.");

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = _completed;
            Close();
        }
    }
}
