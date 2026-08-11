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

            if (preview.Mode == DsTkQtyApplyMode.EditableOnly)
            {
                StatSkipValue.Text = preview.SkippedNotEditable.ToString(CultureInfo.InvariantCulture);
                StatSkipLabel.Text = "пропущено (нет ввода)";
                EditableColumn.Visibility = Visibility.Collapsed;
                FooterHintText.Text =
                    preview.SkippedNotEditable > 0
                        ? $"Из {preview.TotalQtyMismatch} qty≠ отфильтровано {preview.SkippedNotEditable} — в MySpace у них нет поля ввода объёма."
                        : "Все найденные qty≠ можно править в MySpace.";
            }
            else
            {
                StatSkipValue.Text = preview.SkippedNotEditable.ToString(CultureInfo.InvariantCulture);
                StatSkipLabel.Text = "без ввода MySpace";
                EditableColumn.Visibility = Visibility.Visible;
                FooterHintText.Text =
                    "Колонка MySpace = is_material_cnt_input. «нет» обычно нельзя править руками в ДС.";
            }

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
