using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartRemont.ExportRooms.Views;

namespace SmartRemont.ExportRooms.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportSmartRemontRoomsCommand : BaseCommand
    {
        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            if (!EnsureAuthenticated())
                return Result.Cancelled;

            try
            {
                if (doc == null)
                    {
                        TaskDialog.Show("Ошибка", "doc is null после base.Execute()");
                        return Result.Failed;
                    }

                var homeWindow = new HomeWindow();
                if (homeWindow.ShowDialog() != true)
                    return Result.Cancelled;

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