using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SmartRemont.ExportRooms.Views
{
    /// <summary>
    /// Interaction logic for ViewConatainer.xaml
    /// </summary>
    public partial class ViewContainer : Page, IDockablePaneProvider
    {
        public static Dictionary<Guid, ViewContainer> Instance = new Dictionary<Guid, ViewContainer>();

        public DockPosition? Dock { get; set; }

        public ViewContainer(Guid guid)
        {
            Instance[guid] = this;
            InitializeComponent();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.VisibleByDefault = false;
            data.InitialState = new DockablePaneState()
            {
                DockPosition = Dock.HasValue ? Dock.Value : DockPosition.Floating
            };
        }

        public void ClearCtrl()
        {
            grid.Children.Clear();
        }

        public void AddControl(FrameworkElement ctrl)
        {
            grid.Children.Clear();
            grid.Children.Add(ctrl);
        }

        ~ViewContainer()
        {
            Instance = null;
        }
    }
}
