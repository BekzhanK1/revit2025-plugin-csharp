using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SBS.Views;

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSmartRemontRoomsCommand : BaseCommand
    {
        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                if (doc == null)
                    {
                        TaskDialog.Show("Ошибка", "doc is null после base.Execute()");
                        return Result.Failed;
                    }
                var window = new ExportSmartRemontRoomsWindow(doc);
                window.ShowDialog();

                // DialogResult == true means user clicked "Export" and succeeded
                return window.DialogResult == true ? Result.Succeeded : Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Ошибка", $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}