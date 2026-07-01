using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using SmartRemont.ExportRooms.Views;
using Serilog;
using System;
using System.Reflection;

namespace SmartRemont.ExportRooms
{
    public class ExportRoomsApplication : IExternalApplication
    {
        static string _thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
        public static DockablePaneId _toolPaneId = new DockablePaneId(new Guid("AC230042-0036-436F-8561-344791B10D6E"));
        public static UIControlledApplication _uiApp;
        public static ILogger _logger { get; set; }
        public static Action _viewActivated = null;
        public static string _path { get; set; }
        public static AuthSession CurrentSession { get; set; }
        public static Models.RemontOption SelectedRemont { get; set; }

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
            var logRoot = TenMinuteBucketFileSink.GetLogRootDirectory();
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Sink(new TenMinuteBucketFileSink(logRoot))
                .CreateLogger();

            _logger.Information("Smart Remont plugin started. Logs: {LogRoot}", logRoot);
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
                form.AddControl(new AuthView());
                AuthService.RestoreSession();
                _uiApp.RegisterDockablePane(_toolPaneId, "Smart Remont", form);
            }
        }

        void AddRibbonPanel(UIControlledApplication application)
        {
            var tabName = "Smart Remont";
            application.CreateRibbonTab(tabName);
            RibbonPanel ribbonPanel1 = application.CreateRibbonPanel(tabName, "Параметры");
            ExportSmartRemontRooms_button(ribbonPanel1);
        }



        void ExportSmartRemontRooms_button(RibbonPanel ribbonPanel)
        {
            PushButtonData btnData = new PushButtonData(
                "SmartRemont",
                "SmartRemont",
                _thisAssemblyPath,
                typeof(Commands.ExportSmartRemontRoomsCommand).FullName);
            PushButton btn = ribbonPanel.AddItem(btnData) as PushButton;
            var btnImage = BrandAssets.LoadBitmap(BrandAssets.RibbonIconFileName);
            btn.Image = btn.LargeImage = btnImage;
            btn.ToolTip = "SmartRemont";
        }
    }
}
