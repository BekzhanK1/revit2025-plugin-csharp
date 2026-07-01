using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class SurfaceImportResult
    {
        public int MaterialId { get; init; }
        public bool Success { get; init; }
        public bool AlreadyInProject { get; init; }
        public string MaterialName { get; init; }
        public string ErrorMessage { get; init; }
    }

    public static class RevitSurfaceImportService
    {
        const string SrIdParameterName = "SR_ID";

        public static List<SurfaceImportResult> CopyMaterialsIntoDocument(
            Document doc,
            string surfacesRvtPath,
            IEnumerable<int> materialIds)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));

            var ids = (materialIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            var results = new List<SurfaceImportResult>();

            if (ids.Count == 0)
                return results;

            if (string.IsNullOrWhiteSpace(surfacesRvtPath) || !System.IO.File.Exists(surfacesRvtPath))
            {
                foreach (var materialId in ids)
                {
                    results.Add(new SurfaceImportResult
                    {
                        MaterialId = materialId,
                        Success = false,
                        ErrorMessage = "Файл surfaces.rvt не найден"
                    });
                }

                return results;
            }

            Document sourceDoc = null;
            try
            {
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(surfacesRvtPath);
                sourceDoc = doc.Application.OpenDocumentFile(modelPath, new OpenOptions());

                var sourceBySrId = BuildSrIdElementMap(sourceDoc);
                LogElementMapDiagnostics(sourceDoc, sourceBySrId, ids);

                using var tx = new Transaction(doc, "Smart Remont: импорт surface-типов");
                tx.Start();

                foreach (var materialId in ids)
                {
                    if (!sourceBySrId.TryGetValue(materialId, out var sourceElement))
                    {
                        results.Add(new SurfaceImportResult
                        {
                            MaterialId = materialId,
                            Success = false,
                            ErrorMessage = $"{SrIdParameterName}={materialId} не найден в surfaces.rvt"
                        });
                        continue;
                    }

                    var existing = FindElementBySrId(doc, materialId);
                    if (existing != null)
                    {
                        results.Add(new SurfaceImportResult
                        {
                            MaterialId = materialId,
                            Success = true,
                            AlreadyInProject = true,
                            MaterialName = FormatElementLabel(existing)
                        });
                        continue;
                    }

                    try
                    {
                        var copyOptions = new CopyPasteOptions();
                        copyOptions.SetDuplicateTypeNamesHandler(new UseDestinationDuplicateHandler());

                        var copiedIds = ElementTransformUtils.CopyElements(
                            sourceDoc,
                            new List<ElementId> { sourceElement.Id },
                            doc,
                            Transform.Identity,
                            copyOptions);

                        if (copiedIds == null || copiedIds.Count == 0)
                        {
                            results.Add(new SurfaceImportResult
                            {
                                MaterialId = materialId,
                                Success = false,
                                ErrorMessage = "Не удалось скопировать элемент"
                            });
                            continue;
                        }

                        var copiedElement = doc.GetElement(copiedIds.First());
                        var label = FormatElementLabel(copiedElement ?? sourceElement);
                        ExportRoomsApplication._logger?.Information(
                            "Surface copied: material_id={MaterialId}, element={Element}",
                            materialId,
                            label);
                        results.Add(new SurfaceImportResult
                        {
                            MaterialId = materialId,
                            Success = true,
                            MaterialName = label
                        });
                    }
                    catch (Exception ex)
                    {
                        ExportRoomsApplication._logger?.Warning(
                            ex,
                            "Surface type copy failed for material {MaterialId}",
                            materialId);
                        results.Add(new SurfaceImportResult
                        {
                            MaterialId = materialId,
                            Success = false,
                            ErrorMessage = ex.Message
                        });
                    }
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Failed to open surfaces.rvt from {Path}", surfacesRvtPath);
                foreach (var materialId in ids.Where(id => results.All(r => r.MaterialId != id)))
                {
                    results.Add(new SurfaceImportResult
                    {
                        MaterialId = materialId,
                        Success = false,
                        ErrorMessage = $"Не удалось открыть surfaces.rvt: {ex.Message}"
                    });
                }
            }
            finally
            {
                SafeCloseSourceDocument(sourceDoc, doc);
            }

            return results;
        }

        static void SafeCloseSourceDocument(Document sourceDoc, Document projectDoc)
        {
            if (sourceDoc == null || !sourceDoc.IsValidObject || ReferenceEquals(sourceDoc, projectDoc))
                return;

            try
            {
                sourceDoc.Close(false);
            }
            catch (InvalidOperationException ex)
            {
                // OpenDocumentFile может сделать surfaces.rvt активным — API не закрывает active document.
                ExportRoomsApplication._logger?.Warning(
                    ex,
                    "surfaces.rvt не закрыт через API (активный документ). Импорт мог уже выполниться — закройте вкладку вручную.");
            }
        }

        /// <summary>
        /// surfaces.rvt: SR_ID обычно на типах системных семейств (перекрытие, стена…),
        /// но ищем по всем типам элементов и материалам.
        /// </summary>
        static Dictionary<int, Element> BuildSrIdElementMap(Document sourceDoc)
        {
            var map = new Dictionary<int, Element>();

            foreach (var element in CollectSrIdSearchTargets(sourceDoc))
            {
                if (!TryReadSrIdFromElement(element, out var srId, out _))
                    continue;

                if (map.ContainsKey(srId))
                {
                    ExportRoomsApplication._logger?.Warning(
                        "surfaces.rvt: duplicate {Param}={SrId} on {Existing} and {Duplicate}, using first",
                        SrIdParameterName,
                        srId,
                        FormatElementLabel(map[srId]),
                        FormatElementLabel(element));
                    continue;
                }

                map[srId] = element;
            }

            return map;
        }

        static IEnumerable<Element> CollectSrIdSearchTargets(Document document)
        {
            foreach (ElementType elementType in new FilteredElementCollector(document)
                         .WhereElementIsElementType()
                         .Cast<ElementType>())
            {
                yield return elementType;
            }

            foreach (Material material in new FilteredElementCollector(document)
                         .OfClass(typeof(Material))
                         .Cast<Material>())
            {
                yield return material;
            }
        }

        static Element FindElementBySrId(Document doc, int materialId)
        {
            foreach (var element in CollectSrIdSearchTargets(doc))
            {
                if (TryReadSrIdFromElement(element, out var srId, out _)
                    && srId == materialId)
                    return element;
            }

            return null;
        }

        static string FormatElementLabel(Element element)
        {
            if (element == null)
                return null;

            var familyName = element.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString();
            if (string.IsNullOrWhiteSpace(familyName))
                familyName = element.Category?.Name;

            var typeName = element.Name;
            if (string.IsNullOrWhiteSpace(familyName))
                return typeName;

            return $"{familyName}: {typeName}";
        }

        static Parameter FindSrIdParameter(Element element)
        {
            if (element == null)
                return null;

            var direct = element.LookupParameter(SrIdParameterName);
            if (direct != null)
                return direct;

            foreach (Parameter parameter in element.Parameters)
            {
                if (string.Equals(parameter.Definition?.Name, SrIdParameterName, StringComparison.OrdinalIgnoreCase))
                    return parameter;
            }

            return null;
        }

        static bool TryReadSrIdFromElement(Element element, out int srId, out string rawValue)
        {
            srId = 0;
            rawValue = null;
            return TryReadSrId(FindSrIdParameter(element), out srId, out rawValue);
        }

        static bool TryReadSrId(Parameter parameter, out int srId, out string rawValue)
        {
            srId = 0;
            rawValue = null;

            if (parameter == null)
                return false;

            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.Integer:
                        srId = parameter.AsInteger();
                        rawValue = srId.ToString(CultureInfo.InvariantCulture);
                        return true;

                    case StorageType.String:
                        rawValue = parameter.AsString()?.Trim();
                        if (string.IsNullOrWhiteSpace(rawValue))
                            rawValue = parameter.AsValueString()?.Trim();
                        return !string.IsNullOrWhiteSpace(rawValue)
                               && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out srId);

                    case StorageType.Double:
                        var value = parameter.AsDouble();
                        rawValue = value.ToString(CultureInfo.InvariantCulture);
                        if (Math.Abs(value - Math.Round(value)) > 1e-9)
                            return false;
                        srId = (int)Math.Round(value);
                        return true;

                    default:
                        rawValue = parameter.AsValueString()?.Trim();
                        if (string.IsNullOrWhiteSpace(rawValue))
                            rawValue = parameter.AsString()?.Trim();
                        return !string.IsNullOrWhiteSpace(rawValue)
                               && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out srId);
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Debug(ex, "Failed to read {Param} from element", SrIdParameterName);
                return false;
            }
        }

        static void LogElementMapDiagnostics(
            Document sourceDoc,
            Dictionary<int, Element> map,
            IReadOnlyCollection<int> requestedIds)
        {
            var targets = CollectSrIdSearchTargets(sourceDoc).ToList();
            var withSrId = targets.Where(t => TryReadSrIdFromElement(t, out _, out _)).ToList();
            var sample = new StringBuilder();

            foreach (var element in withSrId.Take(10))
            {
                TryReadSrIdFromElement(element, out var srId, out var raw);
                sample.Append($"{FormatElementLabel(element)}={srId}({raw}); ");
            }

            var byCategory = withSrId
                .GroupBy(t => t.Category?.Name ?? t.GetType().Name)
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToList();

            ExportRoomsApplication._logger?.Information(
                "surfaces.rvt scan: scanned={ScannedCount}, with SR_ID={WithSrIdCount}, mapped={MappedCount}, categories=[{Categories}], requested=[{Requested}], sample=[{Sample}]",
                targets.Count,
                withSrId.Count,
                map.Count,
                string.Join(", ", byCategory),
                string.Join(", ", requestedIds),
                sample.Length == 0 ? "—" : sample.ToString().Trim());
        }

        sealed class UseDestinationDuplicateHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) =>
                DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
