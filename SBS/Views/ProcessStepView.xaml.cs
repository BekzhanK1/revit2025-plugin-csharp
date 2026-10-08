using SmartRemont.ExportRooms.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    /// <summary>Один этап процесса в хабе. Состояние задаёт <see cref="Apply"/>.</summary>
    public partial class ProcessStepView : UserControl
    {
        const string GlyphCheck = "";
        const string GlyphLock = "";
        const string GlyphClock = "";
        const string GlyphAlert = "";
        const string GlyphSkip = "";

        public event EventHandler PrimaryClick;
        public event EventHandler SecondaryClick;

        public ProcessStepView()
        {
            InitializeComponent();
        }

        public int Number
        {
            get => int.TryParse(IndicatorNumber.Text, out var n) ? n : 0;
            set => IndicatorNumber.Text = value.ToString();
        }

        public string Title
        {
            get => TitleText.Text;
            set => TitleText.Text = value;
        }

        /// <summary>Последний этап — без линии к следующему.</summary>
        public bool IsLast
        {
            get => Connector.Visibility != Visibility.Visible;
            set => Connector.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Доп. содержимое под подсказкой (например, путь к проекту и его кнопки).</summary>
        public object Extra
        {
            get => ExtraContent.Content;
            set
            {
                ExtraContent.Content = value;
                ExtraContent.Visibility = value == null ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>Текст второй кнопки (слева от основной); null — кнопки нет.</summary>
        public string SecondaryText
        {
            get => SecondaryButton.Content as string;
            set
            {
                SecondaryButton.Content = value;
                SecondaryButton.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// Применяет статус этапа. <paramref name="actionsEnabled"/> = false гасит кнопки
        /// (проект утверждён, идёт инициализация).
        /// </summary>
        public void Apply(HubStepStatus status, bool actionsEnabled = true)
        {
            var state = status?.State ?? HubStepState.Locked;

            ApplyIndicator(state);
            ApplyChip(state, status?.Chip);

            var hint = status?.Hint;
            HintText.Text = hint ?? string.Empty;
            HintText.Visibility = string.IsNullOrWhiteSpace(hint) ? Visibility.Collapsed : Visibility.Visible;
            HintText.Foreground = state switch
            {
                HubStepState.Blocked => Res("ErrorTextBrush"),
                HubStepState.Warning => Res("WarningTextBrush"),
                HubStepState.Locked => Res("TextTertiaryBrush"),
                _ => Res("TextSecondaryBrush")
            };

            TitleText.Foreground = state == HubStepState.Locked
                ? Res("TextTertiaryBrush")
                : Res("TextPrimaryBrush");

            // Текущий этап выделен рамкой — сразу видно, где работать.
            var isCurrent = state == HubStepState.Current;
            ContentHost.Background = isCurrent ? Res("CardBackgroundBrush") : Brushes.Transparent;
            ContentHost.BorderBrush = isCurrent ? Res("PrimarySoftBorderBrush") : Brushes.Transparent;

            var hasAction = status?.HasAction == true && state != HubStepState.Locked;
            PrimaryButton.Content = status?.ActionText;
            PrimaryButton.Visibility = hasAction ? Visibility.Visible : Visibility.Collapsed;
            PrimaryButton.Style = (Style)FindResource(
                isCurrent || state == HubStepState.Blocked ? "CompactPrimaryButton" : "CompactSecondaryButton");
            PrimaryButton.IsEnabled = actionsEnabled;
            SecondaryButton.IsEnabled = actionsEnabled;
        }

        void ApplyIndicator(HubStepState state)
        {
            string glyph = null;
            Brush background = Res("CardBackgroundBrush");
            Brush border = Res("BorderStrongBrush");
            Brush foreground = Res("TextTertiaryBrush");

            switch (state)
            {
                case HubStepState.Done:
                    glyph = GlyphCheck;
                    background = border = Res("SuccessBrush");
                    foreground = Brushes.White;
                    break;
                case HubStepState.Skipped:
                    glyph = GlyphSkip;
                    background = Res("SuccessSoftBrush");
                    border = Res("SuccessBorderBrush");
                    foreground = Res("SuccessBrush");
                    break;
                case HubStepState.Warning:
                    glyph = GlyphAlert;
                    background = border = Res("WarningBrush");
                    foreground = Brushes.White;
                    break;
                case HubStepState.Current:
                    background = border = Res("AppPrimaryBrush");
                    foreground = Brushes.White;
                    break;
                case HubStepState.Waiting:
                    glyph = GlyphClock;
                    background = Res("InfoSoftBrush");
                    border = Res("InfoBrush");
                    foreground = Res("InfoBrush");
                    break;
                case HubStepState.Blocked:
                    glyph = GlyphAlert;
                    background = border = Res("ErrorBrush");
                    foreground = Brushes.White;
                    break;
                case HubStepState.Locked:
                    glyph = GlyphLock;
                    background = Res("SurfaceAltBrush");
                    border = Res("DividerBrush");
                    break;
            }

            IndicatorCircle.Background = background;
            IndicatorCircle.BorderBrush = border;
            IndicatorNumber.Visibility = glyph == null ? Visibility.Visible : Visibility.Collapsed;
            IndicatorNumber.Foreground = foreground;
            IndicatorGlyph.Visibility = glyph == null ? Visibility.Collapsed : Visibility.Visible;
            IndicatorGlyph.Text = glyph ?? string.Empty;
            IndicatorGlyph.Foreground = foreground;

            // Линия к следующему этапу зелёная, если этот пройден.
            Connector.Background = state is HubStepState.Done or HubStepState.Skipped or HubStepState.Warning
                ? Res("SuccessBorderBrush")
                : Res("DividerBrush");
        }

        void ApplyChip(HubStepState state, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ChipBorder.Visibility = Visibility.Collapsed;
                return;
            }

            var (bg, border, fg) = state switch
            {
                HubStepState.Done or HubStepState.Skipped => ("SuccessSoftBrush", "SuccessBorderBrush", "SuccessTextBrush"),
                HubStepState.Warning => ("WarningSoftBrush", "WarningBorderBrush", "WarningTextBrush"),
                HubStepState.Waiting => ("InfoSoftBrush", "InfoBorderBrush", "InfoTextBrush"),
                HubStepState.Blocked => ("ErrorBackgroundBrush", "ErrorBorderBrush", "ErrorTextBrush"),
                HubStepState.Current => ("PrimarySoftBrush", "PrimarySoftBorderBrush", "PrimaryTextBrush"),
                _ => ("SurfaceHoverBrush", "CardBorderBrush", "TextSecondaryBrush")
            };

            ChipBorder.Visibility = Visibility.Visible;
            ChipBorder.Background = Res(bg);
            ChipBorder.BorderBrush = Res(border);
            ChipText.Foreground = Res(fg);
            ChipText.Text = text.Trim();
        }

        Brush Res(string key) => (Brush)FindResource(key);

        void PrimaryButton_Click(object sender, RoutedEventArgs e) => PrimaryClick?.Invoke(this, EventArgs.Empty);

        void SecondaryButton_Click(object sender, RoutedEventArgs e) => SecondaryClick?.Invoke(this, EventArgs.Empty);
    }
}
