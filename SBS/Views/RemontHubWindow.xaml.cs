using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms;
using System.Windows;

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

        void RemontHubWindow_Loaded(object sender, RoutedEventArgs e) =>
            BindRemontInfo(ExportRoomsApplication.SelectedRemont);

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
                return;
            }

            ClientRequestIdText.Text = remont.ClientRequestId.ToString();
            RemontIdText.Text = remont.RemontId.HasValue ? remont.RemontId.Value.ToString() : "—";
            ClientNameText.Text = DisplayOrDash(remont.ClientName);
            ResidentNameText.Text = DisplayOrDash(remont.ResidentName);
            FlatNumText.Text = DisplayOrDash(remont.FlatNum);
            PresetNameText.Text = DisplayOrDash(remont.PresetName);
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
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    isSuccess ? "#1B6FC8" : "#666666"));
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
            ShowInDevelopment();

        static void ShowInDevelopment() =>
            MessageBox.Show("В разработке", "Smart Remont",
                MessageBoxButton.OK, MessageBoxImage.Information);

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = _completed;
            Close();
        }
    }
}
