using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SBS.Views;
using Serilog;
using System;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace SBS
{
    public class AppTools : IExternalApplication
    {
        static string _thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
        static string _resPath = "pack://application:,,,/SBS;component/Resources/";
        public static DockablePaneId _toolPaneId = new DockablePaneId(new Guid("AC230042-0036-436F-8561-344791B10D6E"));
        public static UIControlledApplication _uiApp;
        public static ILogger _logger { get; set; }
        public static Action _viewActivated = null;
        public static string _path { get; set; }
        public Result OnStartup(UIControlledApplication application)
        {
            LogInit();
            AddRibbonPanel(application);
            _uiApp = application;
            application.ControlledApplication.ApplicationInitialized += ControlledApplication_ApplicationInitialized;
            application.ControlledApplication.DocumentClosed += ControlledApplication_DocumentClosed;
            application.ViewActivated += Application_ViewActivated;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        void LogInit()
        {

            _path = System.IO.Path.GetDirectoryName(_thisAssemblyPath);
            var date = DateTime.Now.ToString("MMMM");
            _logger = new LoggerConfiguration()
                .WriteTo.File($"{_path}\\logs\\{date}\\.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }

        private void Application_ViewActivated(object sender, ViewActivatedEventArgs e)
        {
            _viewActivated?.Invoke();
            _viewActivated = null;
        }

        private void ControlledApplication_DocumentClosed(object sender, DocumentClosedEventArgs e)
        {
            foreach (var item in ViewContainer.Instance)
            {
                item.Value?.ClearCtrl();
            }
        }

        private void ControlledApplication_ApplicationInitialized(object sender, ApplicationInitializedEventArgs e)
        {
            if (!DockablePane.PaneIsRegistered(_toolPaneId))
            {
                var form = new ViewContainer(_toolPaneId.Guid)
                {
                    Dock = DockPosition.Top
                };
                _uiApp.RegisterDockablePane(_toolPaneId, "SBS", form);
            }
        }

        public static T GetSetting<T>(string configName)
        {
            var loc = Assembly.GetExecutingAssembly().Location;
            var config = ConfigurationManager.OpenExeConfiguration($"{loc}");
            var strValue = config.AppSettings.Settings[configName]?.Value;
            if (string.IsNullOrEmpty(strValue))
                return default(T);
            return (T)System.Convert.ChangeType(strValue, typeof(T));
        }

        public static T GetUserSetting<T>(string configName)
        {
            var props = Properties.Settings.Default.Properties.OfType<SettingsProperty>();
            if (props.Any(c => c.Name == configName))
            {
                return (T)System.Convert.ChangeType(Properties.Settings.Default[configName], typeof(T));
            }
            return default(T);
        }

        public static bool SetUserSetting(string configName, object ob)
        {
            var props = Properties.Settings.Default.Properties.OfType<SettingsProperty>();
            if (props.Any(c => c.Name == configName))
            {
                Properties.Settings.Default[configName] = ob;
                Properties.Settings.Default.Save();
                return true;
            }
            return false;
        }

        void AddRibbonPanel(UIControlledApplication application)
        {
            var tabName = "SBS";
            application.CreateRibbonTab(tabName);
            RibbonPanel ribbonPanel1 = application.CreateRibbonPanel(tabName, "Параметры");
            Bi_etaj_button(ribbonPanel1);
            ExportAllElements_button(ribbonPanel1);
            ExportSmartRemont_button(ribbonPanel1);
            ExportSmartRemontSchedules_button(ribbonPanel1);
            ExportSmartRemontDiagnostics_button(ribbonPanel1);
        }

        void Bi_etaj_button(RibbonPanel ribbonPanel)
        {
            PushButtonData b1Data = new PushButtonData("SBS выгрузка стен", "SBS выгрузка стен", _thisAssemblyPath, typeof(Commands.SbsWallsCommand).FullName);
            PushButton pb1 = ribbonPanel.AddItem(b1Data) as PushButton;
            BitmapImage pb2Image = new BitmapImage(new Uri(_resPath + "unit.png"));
            pb1.Image = pb1.LargeImage = pb2Image;
            pb1.ToolTip = "SBS выгрузка стен";
        }

        void ExportAllElements_button(RibbonPanel ribbonPanel)
        {
            PushButtonData btnData = new PushButtonData("SBS экспорт всех элементов", "SBS экспорт\nвсех элементов", _thisAssemblyPath, typeof(Commands.ExportAllElementsCommand).FullName);
            PushButton btn = ribbonPanel.AddItem(btnData) as PushButton;
            BitmapImage btnImage = new BitmapImage(new Uri(_resPath + "unit.png"));
            btn.Image = btn.LargeImage = btnImage;
            btn.ToolTip = "Экспорт всех элементов модели в JSON";
        }

        void ExportSmartRemont_button(RibbonPanel ribbonPanel)
        {
            PushButtonData btnData = new PushButtonData("SBS SmartRemont стены", "SmartRemont\nстены", _thisAssemblyPath, typeof(Commands.ExportSmartRemontCommand).FullName);
            PushButton btn = ribbonPanel.AddItem(btnData) as PushButton;
            BitmapImage btnImage = new BitmapImage(new Uri(_resPath + "unit.png"));
            btn.Image = btn.LargeImage = btnImage;
            btn.ToolTip = "Экспорт стен для SmartRemont в JSON";
        }

        void ExportSmartRemontSchedules_button(RibbonPanel ribbonPanel)
        {
            PushButtonData btnData = new PushButtonData("SBS SmartRemont спецификации", "SmartRemont\nспецификации", _thisAssemblyPath, typeof(Commands.ExportSmartRemontSchedulesCommand).FullName);
            PushButton btn = ribbonPanel.AddItem(btnData) as PushButton;
            BitmapImage btnImage = new BitmapImage(new Uri(_resPath + "unit.png"));
            btn.Image = btn.LargeImage = btnImage;
            btn.ToolTip = "Экспорт спецификаций отделки для SmartRemont";
        }

        void ExportSmartRemontDiagnostics_button(RibbonPanel ribbonPanel)
        {
            PushButtonData btnData = new PushButtonData("SBS SmartRemont диагностика", "SmartRemont\nдиагностика", _thisAssemblyPath, typeof(Commands.ExportSmartRemontDiagnosticsCommand).FullName);
            PushButton btn = ribbonPanel.AddItem(btnData) as PushButton;
            BitmapImage btnImage = new BitmapImage(new Uri(_resPath + "unit.png"));
            btn.Image = btn.LargeImage = btnImage;
            btn.ToolTip = "Полная диагностика модели для SmartRemont";
        }
    }
}
