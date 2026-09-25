using SmartRemont.ExportRooms.Services;
using SmartRemont.ExportRooms;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SmartRemont.ExportRooms.Views
{
    public partial class AuthLoginWindow : Window
    {
        readonly string _pluginVersion;
        bool _versionCheckPassed;

        public AuthLoginWindow()
        {
            InitializeComponent();
            BrandAssets.TryApplyCompanyLogo(CompanyLogoImage);
            WindowLayoutHelper.UseFullWorkAreaHeight(this);

            _pluginVersion = PluginVersion.GetCurrent();
            PluginVersionTextBlock.Text = _pluginVersion;
            LoginHeaderVersionTextBlock.Text = _pluginVersion;
            Title = AppBranding.FormatLoginTitle(_pluginVersion);

            Loaded += AuthLoginWindow_Loaded;
        }

        async void AuthLoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RunVersionCheckAsync().ConfigureAwait(true);
        }

        async void VersionCheckRetryButton_Click(object sender, RoutedEventArgs e)
        {
            await RunVersionCheckAsync().ConfigureAwait(true);
        }

        async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await TryLoginAsync();
        }

        async void JwtLoginButton_Click(object sender, RoutedEventArgs e)
        {
            await TryJwtLoginAsync();
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

        async Task RunVersionCheckAsync()
        {
            _versionCheckPassed = false;
            VersionCheckPanel.Visibility = Visibility.Visible;
            LoginPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Opacity = 0;

            VersionCheckProgressBar.Visibility = Visibility.Visible;
            VersionCheckProgressBar.IsIndeterminate = true;
            VersionCheckTitleTextBlock.Text = "Проверяю версию плагина…";
            VersionCheckDetailTextBlock.Text = $"Текущая версия: {_pluginVersion}";
            VersionCheckSuccessBorder.Visibility = Visibility.Collapsed;
            VersionCheckErrorBorder.Visibility = Visibility.Collapsed;
            VersionCheckRetryButton.Visibility = Visibility.Collapsed;

            try
            {
                var result = await PluginVersionCheckService.CheckAsync(_pluginVersion).ConfigureAwait(true);

                VersionCheckProgressBar.IsIndeterminate = false;
                VersionCheckProgressBar.Visibility = Visibility.Collapsed;

                if (!result.IsSupported)
                {
                    VersionCheckTitleTextBlock.Text = "Версия плагина не поддерживается";
                    var minVersion = string.IsNullOrWhiteSpace(result.MinSupportedVersionName)
                        ? "актуальную"
                        : result.MinSupportedVersionName;
                    VersionCheckErrorTextBlock.Text =
                        $"Установлена версия {_pluginVersion}. Минимально допустимая: {minVersion}. "
                        + "Обновите плагин и повторите вход.";
                    VersionCheckErrorBorder.Visibility = Visibility.Visible;
                    VersionCheckRetryButton.Visibility = Visibility.Visible;
                    return;
                }

                VersionCheckTitleTextBlock.Text = "Версия проверена успешно";
                var successText = $"Версия {_pluginVersion} поддерживается.";
                if (result.UpdateAvailable && !string.IsNullOrWhiteSpace(result.LatestVersionName))
                {
                    successText += $" Доступно обновление: {result.LatestVersionName}.";
                    if (!string.IsNullOrWhiteSpace(result.LatestDownloadUrl))
                        successText += " Его можно установить после входа.";
                }

                VersionCheckSuccessTextBlock.Text = successText;
                VersionCheckSuccessBorder.Visibility = Visibility.Visible;
                VersionCheckDetailTextBlock.Text = string.Empty;

                await Task.Delay(900).ConfigureAwait(true);
                _versionCheckPassed = true;
                await ShowLoginPanelAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                VersionCheckProgressBar.IsIndeterminate = false;
                VersionCheckProgressBar.Visibility = Visibility.Collapsed;
                VersionCheckTitleTextBlock.Text = "Не удалось проверить версию";
                VersionCheckErrorTextBlock.Text = ex.Message;
                VersionCheckErrorBorder.Visibility = Visibility.Visible;
                VersionCheckRetryButton.Visibility = Visibility.Visible;
                ExportRoomsApplication._logger?.Warning(ex, "Plugin version check failed");
            }
        }

        async Task ShowLoginPanelAsync()
        {
            VersionCheckPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;

            var creds = CredentialManager.LoadCredentials();
            if (!string.IsNullOrEmpty(creds.email))
            {
                EmailTextBox.Text = creds.email;
                PasswordBox.Password = creds.password ?? string.Empty;
            }

            JwtLoginPanel.Visibility = Configs.UseTestApi && Configs.IsTestApi
                ? Visibility.Visible
                : Visibility.Collapsed;

            var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            LoginPanel.BeginAnimation(OpacityProperty, animation);
            EmailTextBox.Focus();

            await Task.CompletedTask;
        }

        async Task TryLoginAsync()
        {
            if (!_versionCheckPassed)
            {
                ShowError("Сначала должна пройти проверка версии плагина.");
                return;
            }

            SetBusy(true);
            HideError();

            try
            {
                await AuthService.LoginAsync(EmailTextBox.Text, PasswordBox.Password)
                    .ConfigureAwait(true);
                CredentialManager.SaveCredentials(EmailTextBox.Text, PasswordBox.Password);
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

        async Task TryJwtLoginAsync()
        {
            if (!_versionCheckPassed)
            {
                ShowError("Сначала должна пройти проверка версии плагина.");
                return;
            }

            SetBusy(true);
            HideError();

            try
            {
                await AuthService.LoginWithAccessTokenAsync(JwtTextBox.Text).ConfigureAwait(true);
                JwtTextBox.Clear();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                ExportRoomsApplication._logger?.Warning(ex, "Ошибка входа по JWT");
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
            JwtTextBox.IsEnabled = !isBusy;
            JwtLoginButton.IsEnabled = !isBusy;
            LoginButton.Content = isBusy ? "Вход..." : "Войти";
            JwtLoginButton.Content = isBusy ? "Вход..." : "Войти по JWT";
        }
    }
}
