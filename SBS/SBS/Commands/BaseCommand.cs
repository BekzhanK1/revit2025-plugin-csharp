using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public abstract class BaseCommand : IExternalCommand
    {
        protected Document doc;
        protected UIDocument uiDoc;
        protected UIApplication uiApp;
        public virtual Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            doc = commandData.Application.ActiveUIDocument.Document;
            uiDoc = commandData.Application.ActiveUIDocument;
            uiApp = commandData.Application;
            return Result.Succeeded;
        }

        protected void ShowInPane(FrameworkElement element)
        {

        }
    }
}
