using SmartRemont.ExportRooms.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartRemont.ExportRooms.Views
{
    public partial class DsTkPickWindow : Window
    {
        public DsTkChangeItem SelectedItem { get; private set; }

        public DsTkPickWindow(IReadOnlyList<DsTkChangeItem> items)
        {
            InitializeComponent();
            var list = (items ?? new List<DsTkChangeItem>())
                .OrderByDescending(i => i.CanEdit)
                .ThenByDescending(i => i.DsId)
                .ToList();

            ItemsList.ItemsSource = list;
            ItemsList.SelectionChanged += ItemsList_SelectionChanged;

            var preferred = list.FirstOrDefault(i => i.CanEdit) ?? list.FirstOrDefault();
            if (preferred != null)
                ItemsList.SelectedItem = preferred;

            UpdateSelectEnabled();
        }

        void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdateSelectEnabled();

        void UpdateSelectEnabled()
        {
            var item = ItemsList.SelectedItem as DsTkChangeItem;
            SelectButton.IsEnabled = item is { CanEdit: true };
            SelectButton.ToolTip = item == null
                ? null
                : item.CanEdit
                    ? null
                    : "Можно привязать только черновик (не на согласовании и не утверждённую ДС)";
        }

        void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectButton.IsEnabled)
                AcceptSelection();
        }

        void SelectButton_Click(object sender, RoutedEventArgs e) => AcceptSelection();

        void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        void AcceptSelection()
        {
            if (ItemsList.SelectedItem is not DsTkChangeItem item || !item.CanEdit)
                return;

            SelectedItem = item;
            DialogResult = true;
            Close();
        }
    }
}
