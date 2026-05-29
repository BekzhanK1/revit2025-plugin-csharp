using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class HomeWindow : Window
    {
        public HomeWindow()
        {
            InitializeComponent();
            Loaded += HomeWindow_Loaded;
        }

        void HomeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var session = ExportRoomsApplication.CurrentSession;
            var name = session?.DisplayName ?? "пользователь";
            WelcomeTextBlock.Text = $"Добро пожаловать, {name}";

            RemontComboBox.ItemsSource = RemontService.GetMockRemonts();
            if (RemontComboBox.Items.Count > 0)
                RemontComboBox.SelectedIndex = 0;

            if (ExportRoomsApplication.SelectedRemont != null)
            {
                foreach (RemontOption item in RemontComboBox.Items)
                {
                    if (item.Id == ExportRoomsApplication.SelectedRemont.Id)
                    {
                        RemontComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = RemontComboBox.SelectedItem as RemontOption;
            if (selected == null)
            {
                MessageBox.Show("Выберите ремонт", "Smart Remont",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ExportRoomsApplication.SelectedRemont = selected;
            DialogResult = true;
            Close();
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
