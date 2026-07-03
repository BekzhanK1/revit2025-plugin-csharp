using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public static class ProjectPostInitExitService
    {
        static bool _shutdownRequested;
        static string _initializedProjectPath;

        public static void RequestShutdownRevitAfterPluginExit(string projectPath)
        {
            _shutdownRequested = true;
            _initializedProjectPath = projectPath;
        }

        public static bool TryConsumeShutdownRequest(out string projectPath)
        {
            if (!_shutdownRequested)
            {
                projectPath = null;
                return false;
            }

            projectPath = _initializedProjectPath;
            _shutdownRequested = false;
            _initializedProjectPath = null;
            return true;
        }

        public static void ScheduleShutdownRevit(UIApplication uiApp, string projectPath)
        {
            if (uiApp == null)
                return;

            void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
            {
                uiApp.Idling -= OnIdling;

                try
                {
                    CloseSecondaryDocuments(uiApp, projectPath);

                    ExportRoomsApplication._logger?.Information(
                        "Project init: exiting Revit after successful initialization");

                    var exitCommandId = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                    uiApp.PostCommand(exitCommandId);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "Could not exit Revit after project init");
                }
            }

            uiApp.Idling += OnIdling;
        }

        static void CloseSecondaryDocuments(UIApplication uiApp, string projectPath)
        {
            ActivateProjectDocument(uiApp, projectPath);

            var projectFullPath = NormalizePath(projectPath);
            foreach (Document document in uiApp.Application.Documents.Cast<Document>().ToList())
            {
                if (!document.IsValidObject)
                    continue;

                var documentPath = NormalizePath(document.PathName);
                if (!string.IsNullOrEmpty(projectFullPath)
                    && string.Equals(documentPath, projectFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    document.Close(false);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "Could not close secondary document {Path} before Revit exit",
                        document.PathName);
                }
            }
        }

        internal static void ActivateProjectDocument(UIApplication uiApp, Document projectDoc)
        {
            if (uiApp == null || projectDoc == null || !projectDoc.IsValidObject)
                return;

            ActivateProjectDocument(uiApp, projectDoc.PathName);
        }

        static void ActivateProjectDocument(UIApplication uiApp, string projectPath)
        {
            if (uiApp == null || string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
                return;

            try
            {
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(projectPath);
                uiApp.OpenAndActivateDocument(modelPath, new OpenOptions(), false);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "Could not activate project document before Revit exit: {Path}",
                    projectPath);
            }
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }
    }
}
