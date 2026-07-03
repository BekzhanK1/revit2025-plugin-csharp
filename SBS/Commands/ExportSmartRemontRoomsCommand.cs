using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SmartRemont.ExportRooms.Services;
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

            ExportRoomsApplication.CurrentUiApplication = uiApp;

            try
            {
                if (doc == null)
                    {
                        TaskDialog.Show("Ошибка", "doc is null после base.Execute()");
                        return Result.Failed;
                    }

                ProjectRemontBindingService.TryBindFromDocument(doc);

                var homeWindow = new HomeWindow(doc);
                if (homeWindow.ShowDialog() != true)
                    return Result.Cancelled;

                var hubWindow = new RemontHubWindow(doc);
                if (hubWindow.ShowDialog() != true)
                    return Result.Cancelled;

                if (ProjectPostInitExitService.TryConsumeShutdownRequest(out var initializedPath))
                    ProjectPostInitExitService.ScheduleShutdownRevit(uiApp, initializedPath);

                // ExportSmartRemontRoomsWindow — полный экспорт, временно не используется
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("Ошибка", $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}");
                message = ex.Message;
                return Result.Failed;
            }
            finally
            {
                ExportRoomsApplication.CurrentUiApplication = null;
            }
        }
    }
}