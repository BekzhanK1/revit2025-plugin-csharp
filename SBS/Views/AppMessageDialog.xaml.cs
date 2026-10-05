using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    public enum AppMessageKind
    {
        Success,
        InDevelopment,
        Info,
        Warning,
        Error,
        Question
    }

    public partial class AppMessageDialog : Window
    {
        MessageBoxResult _primaryResult = MessageBoxResult.OK;
        MessageBoxResult _secondaryResult = MessageBoxResult.None;
        MessageBoxResult _tertiaryResult = MessageBoxResult.None;
        MessageBoxResult _result;

        AppMessageDialog()
        {
            InitializeComponent();
        }

        public static void ShowSuccess(
            Window owner,
            string title,
            string message,
            string details = null,
            string buttonText = "OK") =>
            Show(owner, AppMessageKind.Success, title, message, details, buttonText);

        public static void ShowInDevelopment(Window owner, string title, string message, string details = null) =>
            Show(owner, AppMessageKind.InDevelopment, title, message, details);

        public static void Show(
            Window owner,
            AppMessageKind kind,
            string title,
            string message,
            string details = null,
            string buttonText = "OK")
        {
            ShowCore(owner, kind, title, message, details, MessageBoxButton.OK, MessageBoxResult.None, buttonText);
        }

        internal static MessageBoxResult ShowCore(
            Window owner,
            AppMessageKind kind,
            string title,
            string message,
            string details,
            MessageBoxButton buttons,
            MessageBoxResult defaultResult,
            string primaryText = null)
        {
            var dialog = new AppMessageDialog();
            var resolvedOwner = ResolveOwner(owner);
            if (resolvedOwner != null)
                dialog.Owner = resolvedOwner;
            else
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            WindowLayoutHelper.EnableEnvironmentBranding(dialog);

            dialog.ApplyKind(kind);
            dialog.TitleText.Text = title ?? string.Empty;
            dialog.TitleText.Visibility = string.IsNullOrWhiteSpace(title) ? Visibility.Collapsed : Visibility.Visible;
            dialog.MessageText.Text = message ?? string.Empty;
            dialog.MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

            if (string.IsNullOrWhiteSpace(details))
                dialog.DetailsPanel.Visibility = Visibility.Collapsed;
            else
                dialog.DetailsText.Text = details;

            dialog.ApplyButtons(buttons, defaultResult, primaryText);
            dialog.ShowDialog();
            return dialog._result;
        }

        void ApplyKind(AppMessageKind kind)
        {
            string glyph;
            string soft;
            string strong;

            switch (kind)
            {
                case AppMessageKind.Success:
                    glyph = ""; soft = "SuccessSoftBrush"; strong = "SuccessBrush";
                    break;
                case AppMessageKind.InDevelopment:
                    glyph = ""; soft = "WarningSoftBrush"; strong = "WarningBrush";
                    break;
                case AppMessageKind.Warning:
                    glyph = ""; soft = "WarningSoftBrush"; strong = "WarningBrush";
                    break;
                case AppMessageKind.Error:
                    glyph = ""; soft = "ErrorBackgroundBrush"; strong = "ErrorBrush";
                    break;
                case AppMessageKind.Question:
                    glyph = ""; soft = "PrimarySoftBrush"; strong = "AppPrimaryBrush";
                    break;
                default:
                    glyph = ""; soft = "InfoSoftBrush"; strong = "InfoBrush";
                    break;
            }

            IconGlyph.Text = glyph;
            IconTile.Background = (Brush)FindResource(soft);
            IconGlyph.Foreground = (Brush)FindResource(strong);
        }

        void ApplyButtons(MessageBoxButton buttons, MessageBoxResult defaultResult, string primaryText)
        {
            MessageBoxResult closeResult;

            switch (buttons)
            {
                case MessageBoxButton.OKCancel:
                    SetButtons("OK", MessageBoxResult.OK, "Отмена", MessageBoxResult.Cancel, null, MessageBoxResult.None);
                    closeResult = MessageBoxResult.Cancel;
                    break;
                case MessageBoxButton.YesNo:
                    SetButtons("Да", MessageBoxResult.Yes, "Нет", MessageBoxResult.No, null, MessageBoxResult.None);
                    closeResult = MessageBoxResult.No;
                    break;
                case MessageBoxButton.YesNoCancel:
                    SetButtons("Да", MessageBoxResult.Yes, "Нет", MessageBoxResult.No, "Отмена", MessageBoxResult.Cancel);
                    closeResult = MessageBoxResult.Cancel;
                    break;
                default:
                    SetButtons("OK", MessageBoxResult.OK, null, MessageBoxResult.None, null, MessageBoxResult.None);
                    closeResult = MessageBoxResult.OK;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(primaryText))
                OkButton.Content = primaryText.Trim();

            _result = closeResult;

            // Esc — тот же результат, что и закрытие крестиком.
            OkButton.IsCancel = _primaryResult == closeResult;
            SecondaryButton.IsCancel = _secondaryResult == closeResult;
            TertiaryButton.IsCancel = _tertiaryResult == closeResult;

            if (defaultResult != MessageBoxResult.None && defaultResult == _secondaryResult)
            {
                OkButton.IsDefault = false;
                SecondaryButton.IsDefault = true;
            }
            else if (defaultResult != MessageBoxResult.None && defaultResult == _tertiaryResult)
            {
                OkButton.IsDefault = false;
                TertiaryButton.IsDefault = true;
            }

            Loaded += (_, __) =>
            {
                if (SecondaryButton.IsDefault)
                    SecondaryButton.Focus();
                else if (TertiaryButton.IsDefault)
                    TertiaryButton.Focus();
                else
                    OkButton.Focus();
            };
        }

        void SetButtons(
            string primary, MessageBoxResult primaryResult,
            string secondary, MessageBoxResult secondaryResult,
            string tertiary, MessageBoxResult tertiaryResult)
        {
            OkButton.Content = primary;
            _primaryResult = primaryResult;

            _secondaryResult = secondaryResult;
            SecondaryButton.Content = secondary;
            SecondaryButton.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;

            _tertiaryResult = tertiaryResult;
            TertiaryButton.Content = tertiary;
            TertiaryButton.Visibility = tertiary == null ? Visibility.Collapsed : Visibility.Visible;
        }

        static Window ResolveOwner(Window owner)
        {
            if (owner != null && new WindowInteropHelper(owner).Handle != IntPtr.Zero)
                return owner;

            foreach (var source in PresentationSource.CurrentSources)
            {
                if (source is HwndSource hwndSource
                    && hwndSource.RootVisual is Window window
                    && window.IsActive)
                    return window;
            }

            return null;
        }

        void Finish(MessageBoxResult result)
        {
            _result = result;
            DialogResult = true;
            Close();
        }

        void OkButton_Click(object sender, RoutedEventArgs e) => Finish(_primaryResult);

        void SecondaryButton_Click(object sender, RoutedEventArgs e) => Finish(_secondaryResult);

        void TertiaryButton_Click(object sender, RoutedEventArgs e) => Finish(_tertiaryResult);
    }

    /// <summary>
    /// Замена <see cref="MessageBox"/> в стиле плагина: те же параметры и те же
    /// <see cref="MessageBoxResult"/>, чтобы вызовы менялись без изменения логики.
    /// </summary>
    public static class AppMessageBox
    {
        public static MessageBoxResult Show(string messageBoxText) =>
            Show(null, messageBoxText, null, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

        public static MessageBoxResult Show(string messageBoxText, string caption) =>
            Show(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
            Show(null, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None);

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
            Show(null, messageBoxText, caption, button, icon, MessageBoxResult.None);

        public static MessageBoxResult Show(
            string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
            Show(null, messageBoxText, caption, button, icon, defaultResult);

        public static MessageBoxResult Show(Window owner, string messageBoxText) =>
            Show(owner, messageBoxText, null, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption) =>
            Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None);

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button) =>
            Show(owner, messageBoxText, caption, button, MessageBoxImage.None, MessageBoxResult.None);

        public static MessageBoxResult Show(
            Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
            Show(owner, messageBoxText, caption, button, icon, MessageBoxResult.None);

        public static MessageBoxResult Show(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
        {
            var kind = MapKind(icon, button);
            var title = BuildTitle(caption, kind);
            return AppMessageDialog.ShowCore(owner, kind, title, messageBoxText, null, button, defaultResult);
        }

        static AppMessageKind MapKind(MessageBoxImage icon, MessageBoxButton button)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    return AppMessageKind.Error;
                case MessageBoxImage.Warning:
                    return AppMessageKind.Warning;
                case MessageBoxImage.Question:
                    return AppMessageKind.Question;
                case MessageBoxImage.Information:
                    return AppMessageKind.Info;
                default:
                    return button == MessageBoxButton.YesNo || button == MessageBoxButton.YesNoCancel
                        ? AppMessageKind.Question
                        : AppMessageKind.Info;
            }
        }

        /// <summary>Заголовок окна MessageBox превращаем в заголовок диалога (без «Smart Remont — »).</summary>
        static string BuildTitle(string caption, AppMessageKind kind)
        {
            var text = (caption ?? string.Empty).Trim();
            const string separator = " — ";
            if (text.StartsWith(AppBranding.ProductName, StringComparison.Ordinal))
            {
                var idx = text.IndexOf(separator, StringComparison.Ordinal);
                text = idx >= 0 ? text.Substring(idx + separator.Length).Trim() : string.Empty;
            }

            if (string.IsNullOrEmpty(text))
            {
                switch (kind)
                {
                    case AppMessageKind.Error: return "Ошибка";
                    case AppMessageKind.Warning: return "Внимание";
                    case AppMessageKind.Question: return "Подтверждение";
                    default: return "Информация";
                }
            }

            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
