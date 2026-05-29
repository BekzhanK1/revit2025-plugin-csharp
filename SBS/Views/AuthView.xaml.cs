using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartRemont.ExportRooms.Views
{
    public partial class AuthView : UserControl
    {
        public AuthView()
        {
            InitializeComponent();
            Loaded += AuthView_Loaded;
        }

        void AuthView_Loaded(object sender, RoutedEventArgs e)
        {
            var session = AuthService.RestoreSession();
            if (session != null)
                ShowWelcome(session);
            else
                ShowLogin();
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

        async System.Threading.Tasks.Task TryLoginAsync()
        {
            SetBusy(true);
            HideError();

            try
            {
                var session = await AuthService.LoginAsync(EmailTextBox.Text, PasswordBox.Password)
                    .ConfigureAwait(true);
                PasswordBox.Clear();
                ShowWelcome(session);
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

        void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            AuthService.Logout();
            EmailTextBox.Clear();
            PasswordBox.Clear();
            HideError();
            ShowLogin();
        }

        void ShowWelcome(AuthSession session)
        {
            WelcomeTextBlock.Text = $"Добро пожаловать, {session.DisplayName}";
            LoginPanel.Visibility = Visibility.Collapsed;
            WelcomePanel.Visibility = Visibility.Visible;
        }

        void ShowLogin()
        {
            WelcomePanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;
        }

        void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }

        void HideError()
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
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
