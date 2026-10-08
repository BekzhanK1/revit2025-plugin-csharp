using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>
    /// После успешного init: когда окна плагина закрыты, открывает новый проект в Revit
    /// и закрывает остальные документы без несохранённых правок. Revit не завершается.
    /// </summary>
    public static class ProjectPostInitOpenService
    {
        static string _pendingProjectPath;

        public static void RequestOpenProjectAfterPluginExit(string projectPath)
        {
            _pendingProjectPath = projectPath;
        }

        public static bool TryConsumeOpenRequest(out string projectPath)
        {
            projectPath = _pendingProjectPath;
            _pendingProjectPath = null;
            return !string.IsNullOrWhiteSpace(projectPath);
        }

        /// <summary>
        /// OpenAndActivateDocument нельзя вызывать, пока открыт модальный диалог плагина и идёт
        /// внешняя команда, поэтому откладываем до первого Idling.
        /// </summary>
        public static void ScheduleOpenProject(UIApplication uiApp, string projectPath)
        {
            if (uiApp == null)
                return;

            void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
            {
                uiApp.Idling -= OnIdling;

                try
                {
                    if (!OpenProject(uiApp, projectPath))
                    {
                        TaskDialog.Show(
                            "Smart Remont",
                            "Проект создан и сохранён, но Revit не смог открыть его автоматически.\n\n"
                            + "Откройте файл через Файл → Открыть:\n" + NormalizePath(projectPath));
                        return;
                    }

                    CloseOtherDocuments(uiApp, projectPath);
                    ExportRoomsApplication._logger?.Information(
                        "Project init: opened initialized project {Path}",
                        projectPath);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "Could not open project after init: {Path}",
                        projectPath);
                }
            }

            uiApp.Idling += OnIdling;
        }

        /// <summary>
        /// Новый проект после init уже открыт в памяти (без окна) — OpenAndActivateDocument
        /// показывает его. Если не вышло, закрываем копию в памяти и открываем файл с диска.
        /// </summary>
        static bool OpenProject(UIApplication uiApp, string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
                return false;

            // Путь как его видит Windows: без точек и пробелов в конце имён папок.
            // Revit по «сырому» пути с такой папкой файл не находит.
            projectPath = NormalizePath(projectPath);

            if (TryOpenAndActivate(uiApp, projectPath))
                return true;

            var inMemory = FindDocument(uiApp, projectPath);
            if (inMemory != null && !IsActive(uiApp, inMemory))
            {
                try
                {
                    inMemory.Close(false);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "Could not close in-memory project before reopening: {Path}",
                        projectPath);
                    return false;
                }
            }

            return TryOpenAndActivate(uiApp, projectPath);
        }

        static bool TryOpenAndActivate(UIApplication uiApp, string projectPath)
        {
            try
            {
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(projectPath);
                uiApp.OpenAndActivateDocument(modelPath, new OpenOptions(), false);
                return IsActivePath(uiApp, projectPath);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "OpenAndActivateDocument failed for {Path}",
                    projectPath);
                return false;
            }
        }

        static void CloseOtherDocuments(UIApplication uiApp, string projectPath)
        {
            var projectFullPath = NormalizePath(projectPath);
            foreach (Document document in uiApp.Application.Documents.Cast<Document>().ToList())
            {
                if (!document.IsValidObject || document.IsLinked)
                    continue;

                if (string.Equals(NormalizePath(document.PathName), projectFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Несохранённые правки пользователя не выбрасываем: такой документ остаётся открытым.
                if (document.IsModified)
                {
                    ExportRoomsApplication._logger?.Information(
                        "Keeping modified document open after project init: {Path}",
                        string.IsNullOrEmpty(document.PathName) ? document.Title : document.PathName);
                    continue;
                }

                try
                {
                    document.Close(false);
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "Could not close document {Path} after project init",
                        document.PathName);
                }
            }
        }

        internal static void ActivateProjectDocument(UIApplication uiApp, Document projectDoc)
        {
            if (uiApp == null || projectDoc == null || !projectDoc.IsValidObject)
                return;

            var projectPath = NormalizePath(projectDoc.PathName);
            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
                return;

            TryOpenAndActivate(uiApp, projectPath);
        }

        static Document FindDocument(UIApplication uiApp, string projectPath)
        {
            var fullPath = NormalizePath(projectPath);
            return uiApp.Application.Documents
                .Cast<Document>()
                .FirstOrDefault(d => d.IsValidObject
                                     && string.Equals(NormalizePath(d.PathName), fullPath, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsActive(UIApplication uiApp, Document document) =>
            ReferenceEquals(uiApp.ActiveUIDocument?.Document, document);

        static bool IsActivePath(UIApplication uiApp, string projectPath) =>
            string.Equals(
                NormalizePath(uiApp.ActiveUIDocument?.Document?.PathName),
                NormalizePath(projectPath),
                StringComparison.OrdinalIgnoreCase);

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
