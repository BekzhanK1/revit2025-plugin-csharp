using SmartRemont.ExportRooms.Services;
using System.Globalization;
using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class DsTkQtyApplyPreviewWindow : Window
    {
        public bool Confirmed { get; private set; }

        public DsTkQtyApplyPreviewWindow(
            DsTkQtyApplyPreview preview,
            int dsId,
            string dsStatusDisplay)
        {
            InitializeComponent();

            preview ??= new DsTkQtyApplyPreview
            {
                Mode = DsTkQtyApplyMode.Skip,
                Candidates = System.Array.Empty<DsTkQtyApplyCandidate>()
            };

            SubtitleText.Text = preview.ModeHint;
            StatSendValue.Text = preview.Candidates.Count.ToString(CultureInfo.InvariantCulture);
            StatDsValue.Text = $"№{dsId}";

            StatSkipValue.Text = preview.ProjectAlertCount.ToString(CultureInfo.InvariantCulture);
            StatSkipLabel.Text = "алерт проекта ≥10%";
            EditableColumn.Visibility = Visibility.Collapsed;

            FooterHintText.Text =
                "В ДС уйдут только объёмы с полем ввода в MySpace. Расхождения состава пока не блокируют.";

            if (!string.IsNullOrWhiteSpace(dsStatusDisplay))
                StatDsValue.ToolTip = dsStatusDisplay;

            PreviewGrid.ItemsSource = preview.Candidates;
            ConfirmButton.Content = preview.Candidates.Count == 1
                ? "Отправить 1 позицию"
                : $"Отправить {preview.Candidates.Count} позиций";
            ConfirmButton.IsEnabled = preview.Candidates.Count > 0;
        }

        void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
