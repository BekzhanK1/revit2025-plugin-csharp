using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartRemont.ExportSpecifications.Views;

namespace SmartRemont.ExportSpecifications.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSpecificationsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData?.Application?.ActiveUIDocument;
            var doc = uiDoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show("SmartRemont", "Откройте проект Revit.");
                return Result.Cancelled;
            }

            var window = new ExportSpecificationsWindow(doc);
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
