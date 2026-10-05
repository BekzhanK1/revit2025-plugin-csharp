using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SmartRemont.ExportRooms.Views
{
    /// <summary>
    /// Для таблиц/списков внутри общего ScrollViewer: колесо мыши прокручивает
    /// внешний список, а не «застревает» во вложенном DataGrid.
    /// </summary>
    public static class ScrollBehavior
    {
        public static readonly DependencyProperty BubbleMouseWheelProperty =
            DependencyProperty.RegisterAttached(
                "BubbleMouseWheel",
                typeof(bool),
                typeof(ScrollBehavior),
                new PropertyMetadata(false, OnBubbleMouseWheelChanged));

        public static bool GetBubbleMouseWheel(DependencyObject obj) =>
            (bool)obj.GetValue(BubbleMouseWheelProperty);

        public static void SetBubbleMouseWheel(DependencyObject obj, bool value) =>
            obj.SetValue(BubbleMouseWheelProperty, value);

        static void OnBubbleMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
                return;

            element.PreviewMouseWheel -= Element_PreviewMouseWheel;
            if ((bool)e.NewValue)
                element.PreviewMouseWheel += Element_PreviewMouseWheel;
        }

        static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || sender is not UIElement element)
                return;

            if (VisualTreeHelper.GetParent(element) is not UIElement parent)
                return;

            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            });
        }
    }
}
