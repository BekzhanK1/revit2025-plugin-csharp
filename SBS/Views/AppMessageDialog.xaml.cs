using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public enum AppMessageKind
    {
        Success,
        InDevelopment
    }

    public partial class AppMessageDialog : Window
    {
        AppMessageDialog()
        {
            InitializeComponent();
        }

        public static void ShowSuccess(Window owner, string title, string message, string details = null) =>
            Show(owner, AppMessageKind.Success, title, message, details);

        public static void ShowInDevelopment(Window owner, string title, string message, string details = null) =>
            Show(owner, AppMessageKind.InDevelopment, title, message, details);

        public static void Show(Window owner, AppMessageKind kind, string title, string message, string details = null)
        {
            var dialog = new AppMessageDialog { Owner = owner };
            if (owner == null)
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dialog.SuccessIconPanel.Visibility =
                kind == AppMessageKind.Success ? Visibility.Visible : Visibility.Collapsed;
            dialog.DevelopmentIconPanel.Visibility =
                kind == AppMessageKind.InDevelopment ? Visibility.Visible : Visibility.Collapsed;

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
