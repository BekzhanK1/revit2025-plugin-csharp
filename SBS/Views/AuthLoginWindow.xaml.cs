using SmartRemont.ExportRooms.Services;
using SmartRemont.ExportRooms;
using System;
using System.Windows;
using System.Windows.Input;

namespace SmartRemont.ExportRooms.Views
{
    public partial class AuthLoginWindow : Window
    {
        public AuthLoginWindow()
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(CompanyLogoImage);
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
        }

        async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await TryLoginAsync();
        }

        async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await TryLoginAsync();
        }

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        async System.Threading.Tasks.Task TryLoginAsync()
        {
            SetBusy(true);
            HideError();

            try
            {
                await AuthService.LoginAsync(EmailTextBox.Text, PasswordBox.Password)
                    .ConfigureAwait(true);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка входа");
            }
            finally
            {
                SetBusy(false);
            }
        }

        void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorBorder.Visibility = Visibility.Visible;
        }

        void HideError()
        {
            ErrorBorder.Visibility = Visibility.Collapsed;
        }

        void SetBusy(bool isBusy)
        {
            LoginButton.IsEnabled = !isBusy;
            EmailTextBox.IsEnabled = !isBusy;
            PasswordBox.IsEnabled = !isBusy;
            LoginButton.Content = isBusy ? "Вход..." : "Войти";
        }
    }
}
