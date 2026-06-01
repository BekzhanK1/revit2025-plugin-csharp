using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class SuccessDialog : Window
    {
        SuccessDialog()
        {
            InitializeComponent();
        }

        public static void Show(Window owner, string title, string message, string details = null)
        {
            var dialog = new SuccessDialog { Owner = owner };
            if (owner == null)
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;

            if (string.IsNullOrWhiteSpace(details))
                dialog.DetailsText.Visibility = Visibility.Collapsed;
            else
                dialog.DetailsText.Text = details;

            dialog.ShowDialog();
        }

        void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
