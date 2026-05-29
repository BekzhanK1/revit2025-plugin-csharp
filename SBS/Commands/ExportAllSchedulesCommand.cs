using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using RevitTaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace SBS.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportAllSchedulesCommand : BaseCommand
    {
        public override Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            base.Execute(commandData, ref message, elements);

            try
            {
                // ── 1. Найти все спецификации ──────────────────────────────────
                var allSchedules = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .Where(IsExportable)
                    .OrderBy(s => s.Name)
                    .ToList();

                if (!allSchedules.Any())
                {
                    RevitTaskDialog.Show("Экспорт спецификаций",
                        "В проекте не найдено подходящих спецификаций для экспорта.");
                    return Result.Succeeded;
                }

                // ── 2. Выбрать папку ───────────────────────────────────────────
                string folderPath;
                using (var dlg = new FolderBrowserDialog
                {
                    Description         = $"Выберите папку для экспорта {allSchedules.Count} спецификаций",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                })
                {
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return Result.Cancelled;

                    folderPath = dlg.SelectedPath;
                }

                // ── 3. Настройки экспорта ──────────────────────────────────────
                var options = new ViewScheduleExportOptions
                {
                    FieldDelimiter      = ";",
                    TextQualifier       = ExportTextQualifier.DoubleQuote,
                    ColumnHeaders       = ExportColumnHeaders.OneRow,
                    HeadersFootersBlanks = true
                };

                // ── 4. Экспорт каждой спецификации ────────────────────────────
                var exported = new List<string>();
                var failed   = new List<(string Name, string Error)>();

                foreach (var schedule in allSchedules)
                {
                    try
                    {
                        var fileName = SanitizeFileName(schedule.Name) + ".csv";
                        schedule.Export(folderPath, fileName, options);
                        exported.Add(schedule.Name);
                    }
                    catch (Exception ex)
                    {
                        failed.Add((schedule.Name, ex.Message));
                    }
                }

                // ── 5. Итоговый отчёт ──────────────────────────────────────────
                var report = $"Экспорт завершён.\n\n" +
                             $"✓ Успешно: {exported.Count} из {allSchedules.Count}\n" +
                             $"Папка: {folderPath}";

                if (failed.Any())
                {
                    report += $"\n\n✗ Ошибки ({failed.Count}):\n";
                    report += string.Join("\n", failed.Select(f => $"  • {f.Name}: {f.Error}"));
                }

                RevitTaskDialog.Show("Экспорт спецификаций", report);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppTools._logger?.Error(ex, "Ошибка при экспорте спецификаций");
                message = ex.Message;
                RevitTaskDialog.Show("Экспорт спецификаций", $"Ошибка: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Возвращает true если спецификацию нужно экспортировать.
        /// Исключает: ревизии, листы, шаблоны и внутренние служебные спецификации.
        /// </summary>
        private static bool IsExportable(ViewSchedule schedule)
        {
            if (schedule == null)                return false;
            if (schedule.IsTemplate)             return false;
            if (schedule.IsTitleblockRevisionSchedule) return false;
            if (schedule.IsInternalKeynoteSchedule)    return false;

            // Исключаем спецификации листов (Sheet List)
            var defn = schedule.Definition;
            if (defn == null) return false;

            var categoryId = defn.CategoryId;
            if (categoryId == new ElementId(BuiltInCategory.OST_Sheets))        return false;
            if (categoryId == new ElementId(BuiltInCategory.OST_Revisions))     return false;
            if (categoryId == new ElementId(BuiltInCategory.OST_Views))         return false;

            return true;
        }

        /// <summary>
        /// Очищает имя файла от недопустимых символов Windows.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var result  = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

            // Обрезаем до 200 символов чтобы не превысить MAX_PATH
            if (result.Length > 200)
                result = result.Substring(0, 200);

            return result.Trim();
        }
    }
}
