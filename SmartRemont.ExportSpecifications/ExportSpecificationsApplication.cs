using Autodesk.Revit.UI;
using SmartRemont.ExportSpecifications.Commands;
using System;
using System.Reflection;

namespace SmartRemont.ExportSpecifications
{
    public class ExportSpecificationsApplication : IExternalApplication
    {
        static readonly string AssemblyPath = Assembly.GetExecutingAssembly().Location;

        public Result OnStartup(UIControlledApplication application)
        {
            AddRibbonButton(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        static void AddRibbonButton(UIControlledApplication application)
        {
            const string tabName = "Smart Remont";
            const string panelName = "Спецификации";

            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Вкладка уже создана основным плагином.
            }

            var ribbonPanel = application.CreateRibbonPanel(tabName, panelName);
            var btnData = new PushButtonData(
                "ExportSpecifications",
                "экспорт\nспецификаций",
                AssemblyPath,
                typeof(ExportSpecificationsCommand).FullName);

            var btn = ribbonPanel.AddItem(btnData) as PushButton;
            var icon = BrandAssets.LoadBitmap(BrandAssets.RibbonIconFileName);
            btn.Image = btn.LargeImage = icon;
            btn.ToolTip = "SmartRemont — экспорт спецификаций";
        }
    }
}
