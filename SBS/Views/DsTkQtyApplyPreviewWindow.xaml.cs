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
            string dsStatusDisplay,
            int measureRoomCount = 0)
        {
            InitializeComponent();
            WindowLayoutHelper.EnableEnvironmentBranding(this);

            preview ??= new DsTkQtyApplyPreview
            {
                Mode = DsTkQtyApplyMode.Skip,
                Candidates = System.Array.Empty<DsTkQtyApplyCandidate>()
            };

            SubtitleText.Text = preview.ModeHint;
            StatSendValue.Text = preview.PositionCount.ToString(CultureInfo.InvariantCulture);
            StatSendValue.ToolTip = $"строк с изменением: {preview.Candidates.Count}";
            StatDsValue.Text = $"№{dsId}";

            StatSkipValue.Text = preview.UntouchedCount.ToString(CultureInfo.InvariantCulture);
            StatSkipLabel.Text = "строк с вводом как в ДС";
            StatSkipValue.ToolTip = "Конструктив не из модели (мебель, кондиционер, доплаты…) или правится только в MySpace";

            FooterHintText.Text =
                $"Сначала замеры {measureRoomCount} комнат, потом объёмы. Записывается всё вместе: "
                + "если сервер отклонит хоть что-то, не запишется ни замер, ни объём.";

            if (!string.IsNullOrWhiteSpace(dsStatusDisplay))
                StatDsValue.ToolTip = dsStatusDisplay;

            PreviewGrid.ItemsSource = preview.Candidates;
            ConfirmButton.Content = preview.PositionCount == 1
                ? "Отправить 1 позицию"
                : $"Отправить {preview.PositionCount} позиций";
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
