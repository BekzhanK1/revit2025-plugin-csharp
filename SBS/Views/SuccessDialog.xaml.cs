using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class SuccessDialog : Window
    {
        SuccessDialog()
        {
            InitializeComponent();
        }

        public static void Show(Window owner, string title, string message, string details = null) =>
            AppMessageDialog.ShowSuccess(owner, title, message, details);

        void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
