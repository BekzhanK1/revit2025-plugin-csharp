using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class FamilyImportResult
    {
        public int MaterialId { get; init; }
        public bool Success { get; init; }
        public bool AlreadyInProject { get; init; }
        public bool NotSupported { get; init; }
        public string FamilyName { get; init; }
        public string ErrorMessage { get; init; }
    }

    public static class RevitFamilyImportService
    {
        const string SrIdParameterName = "SR_ID";

        public static List<FamilyImportResult> LoadFamiliesIntoDocument(
            Document doc,
            IEnumerable<(int materialId, string filePath, string revitFileType)> items)
        {
            if (doc == null)
                throw new System.ArgumentNullException(nameof(doc));

            var itemList = (items ?? Enumerable.Empty<(int, string, string)>()).ToList();
            var results = new List<FamilyImportResult>();

            if (itemList.Count == 0)
                return results;

            // LoadFamily нельзя вызывать внутри открытой Transaction — иначе часто возвращает false.
            foreach (var (materialId, filePath, revitFileType) in itemList)
            {
                var type = (revitFileType ?? string.Empty).Trim().ToLowerInvariant();

                if (type != "rfa")
                {
                    results.Add(new FamilyImportResult
                    {
                        MaterialId = materialId,
                        NotSupported = true,
                        Success = false,
                        ErrorMessage = "Импорт материалов пока не поддержан"
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                {
                    results.Add(new FamilyImportResult
                    {
                        MaterialId = materialId,
                        Success = false,
                        ErrorMessage = "Файл семейства не найден"
                    });
                    continue;
                }

                // Уже есть тип/семейство с этим SR_ID — повторная загрузка не нужна.
                var already = RevitMaterialPresenceService.CheckMaterial(doc, materialId);
                if (already.IsInProject)
                {
                    results.Add(new FamilyImportResult
                    {
                        MaterialId = materialId,
                        Success = true,
                        AlreadyInProject = true,
                        FamilyName = already.Label
                    });
                    continue;
                }

                try
                {
                    var loadOptions = new AcceptExistingFamilyLoadOptions();
                    var loaded = doc.LoadFamily(filePath, loadOptions, out Family family);

                    if (family != null || loaded)
                    {
                        RevitMaterialsDownloadService.MarkCacheFileReadOnly(filePath);
                        results.Add(new FamilyImportResult
                        {
                            MaterialId = materialId,
                            Success = true,
                            AlreadyInProject = loadOptions.FamilyAlreadyInProject,
                            FamilyName = family?.Name ?? System.IO.Path.GetFileNameWithoutExtension(filePath)
                        });
                        continue;
                    }

                    var existingFamily = TryFindExistingFamilyByFileName(doc, filePath);
                    if (existingFamily != null)
                    {
                        RevitMaterialsDownloadService.MarkCacheFileReadOnly(filePath);
                        results.Add(new FamilyImportResult
                        {
                            MaterialId = materialId,
                            Success = true,
                            AlreadyInProject = true,
                            FamilyName = existingFamily.Name
                        });
                        continue;
                    }

                    // LoadFamily вернул false, но SR_ID мог уже оказаться в проекте.
                    var afterLoad = RevitMaterialPresenceService.CheckMaterial(doc, materialId);
                    if (afterLoad.IsInProject)
                    {
                        RevitMaterialsDownloadService.MarkCacheFileReadOnly(filePath);
                        results.Add(new FamilyImportResult
                        {
                            MaterialId = materialId,
                            Success = true,
                            AlreadyInProject = true,
                            FamilyName = afterLoad.Label
                        });
                        continue;
                    }

                    ExportRoomsApplication._logger?.Warning(
                        "Material {MaterialId}: LoadFamily=false, file={Path}",
                        materialId, filePath);
                    results.Add(new FamilyImportResult
                    {
                        MaterialId = materialId,
                        Success = false,
                        ErrorMessage = $"Не удалось загрузить семейство (LoadFamily=false): {System.IO.Path.GetFileName(filePath)}"
                    });
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "LoadFamily failed for material {MaterialId}",
                        materialId);
                    results.Add(new FamilyImportResult
                    {
                        MaterialId = materialId,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    ExportRoomsApplication._logger?.Warning(
                        ex,
                        "LoadFamily failed for material {MaterialId}",
                        materialId);
                    results.Add(new FamilyImportResult
                    {
                        MaterialId = materialId,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            return results;
        }

        static string ValidateSrIdInRfaFile(
            Autodesk.Revit.ApplicationServices.Application app,
            string filePath,
            int expectedMaterialId)
        {
            if (app == null || string.IsNullOrWhiteSpace(filePath))
                return "Не удалось проверить SR_ID";

            ExportRoomsApplication._logger?.Information(
                "SR_ID check start: file={Path}, expected material_id={MaterialId}",
                filePath,
                expectedMaterialId);

            Document familyDoc = null;
            try
            {
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                familyDoc = app.OpenDocumentFile(modelPath, new OpenOptions());
                return ValidateSrIdInFamilyDocument(familyDoc, expectedMaterialId);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось проверить SR_ID в {Path}", filePath);
                return $"Не удалось проверить SR_ID: {ex.Message}";
            }
            finally
            {
                if (familyDoc != null)
                    familyDoc.Close(false);
            }
        }

        static string ValidateSrIdInFamilyDocument(Document familyDoc, int expectedMaterialId)
        {
            if (familyDoc == null || !familyDoc.IsFamilyDocument)
                return "Некорректный документ семейства";

            ExportRoomsApplication._logger?.Information(
                "SR_ID check family document: title={Title}, owner={Owner}",
                familyDoc.Title,
                familyDoc.OwnerFamily?.Name ?? "—");

            var familyManagerError = ValidateSrIdViaFamilyManager(familyDoc, expectedMaterialId);
            if (familyManagerError == null)
                return null;

            ExportRoomsApplication._logger?.Information(
                "SR_ID FamilyManager check failed ({Reason}), trying FamilySymbol lookup",
                familyManagerError);

            return ValidateSrIdViaFamilySymbols(familyDoc, expectedMaterialId);
        }

        static string ValidateSrIdViaFamilyManager(Document familyDoc, int expectedMaterialId)
        {
            var familyManager = familyDoc.FamilyManager;
            var srParameter = FindFamilyManagerParameter(familyManager, SrIdParameterName);

            if (srParameter == null)
            {
                LogFamilyManagerParameterNames(familyManager);
                return $"Параметр {SrIdParameterName} не найден в FamilyManager";
            }

            ExportRoomsApplication._logger?.Information(
                "SR_ID parameter in FamilyManager: storage={StorageType}, isInstance={IsInstance}",
                srParameter.StorageType,
                srParameter.IsInstance);

            var typesChecked = 0;
            foreach (FamilyType familyType in familyManager.Types)
            {
                typesChecked++;

                if (!TryReadSrIdFromFamilyManager(familyType, srParameter, out var srId, out var rawValue))
                {
                    ExportRoomsApplication._logger?.Warning(
                        "SR_ID FamilyManager type {TypeName}: empty or not numeric, raw={Raw}",
                        familyType.Name,
                        rawValue ?? "—");
                    return $"Тип «{familyType.Name}»: {SrIdParameterName} пуст или не число";
                }

                ExportRoomsApplication._logger?.Information(
                    "SR_ID FamilyManager type {TypeName}: raw={Raw}, parsed={Parsed}",
                    familyType.Name,
                    rawValue,
                    srId);

                if (srId != expectedMaterialId)
                    return $"{SrIdParameterName} ({srId}) не совпадает с material_id ({expectedMaterialId})";
            }

            if (typesChecked == 0)
                return "В семействе нет типов";

            ExportRoomsApplication._logger?.Information(
                "SR_ID FamilyManager check OK for {TypeCount} type(s)",
                typesChecked);

            return null;
        }

        static string ValidateSrIdViaFamilySymbols(Document familyDoc, int expectedMaterialId)
        {
            var symbols = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            if (symbols.Count == 0)
                return "В семействе нет типов";

            ExportRoomsApplication._logger?.Information(
                "SR_ID FamilySymbol scan: {SymbolCount} symbol(s)",
                symbols.Count);

            var typesWithSrId = 0;

            foreach (var symbol in symbols)
            {
                var parameter = symbol.LookupParameter(SrIdParameterName);
                if (parameter == null)
                {
                    ExportRoomsApplication._logger?.Debug(
                        "SR_ID symbol {SymbolName}: parameter missing",
                        symbol.Name);
                    continue;
                }

                typesWithSrId++;

                if (!parameter.HasValue)
                {
                    ExportRoomsApplication._logger?.Warning(
                        "SR_ID symbol {SymbolName}: parameter exists but HasValue=false, storage={StorageType}",
                        symbol.Name,
                        parameter.StorageType);
                    return $"Тип «{symbol.Name}»: {SrIdParameterName} пуст";
                }

                if (!TryReadSrId(parameter, out var srId, out var rawValue))
                {
                    ExportRoomsApplication._logger?.Warning(
                        "SR_ID symbol {SymbolName}: not numeric, raw={Raw}, storage={StorageType}",
                        symbol.Name,
                        rawValue ?? "—",
                        parameter.StorageType);
                    return string.IsNullOrWhiteSpace(rawValue)
                        ? $"Тип «{symbol.Name}»: {SrIdParameterName} пуст"
                        : $"Тип «{symbol.Name}»: {SrIdParameterName} не число ({rawValue})";
                }

                ExportRoomsApplication._logger?.Information(
                    "SR_ID symbol {SymbolName}: raw={Raw}, parsed={Parsed}",
                    symbol.Name,
                    rawValue,
                    srId);

                if (srId != expectedMaterialId)
                    return $"{SrIdParameterName} ({srId}) не совпадает с material_id ({expectedMaterialId})";
            }

            if (typesWithSrId == 0)
            {
                ExportRoomsApplication._logger?.Warning(
                    "SR_ID not found on any of {SymbolCount} FamilySymbol(s)",
                    symbols.Count);
                return $"Параметр {SrIdParameterName} не найден ни у одного типа семейства";
            }

            ExportRoomsApplication._logger?.Information(
                "SR_ID FamilySymbol check OK for {TypeCount} symbol(s)",
                typesWithSrId);

            return null;
        }

        static FamilyParameter FindFamilyManagerParameter(FamilyManager familyManager, string parameterName)
        {
            if (familyManager == null || string.IsNullOrWhiteSpace(parameterName))
                return null;

            foreach (FamilyParameter parameter in familyManager.Parameters)
            {
                if (string.Equals(parameter.Definition?.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    return parameter;
            }

            return null;
        }

        static void LogFamilyManagerParameterNames(FamilyManager familyManager)
        {
            if (familyManager == null)
                return;

            var names = new List<string>();
            foreach (FamilyParameter parameter in familyManager.Parameters)
            {
                var name = parameter.Definition?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            ExportRoomsApplication._logger?.Information(
                "FamilyManager parameters ({Count}): {Names}",
                names.Count,
                names.Count == 0 ? "—" : string.Join(", ", names));
        }

        static bool TryReadSrIdFromFamilyManager(
            FamilyType familyType,
            FamilyParameter parameter,
            out int srId,
            out string rawValue)
        {
            srId = 0;
            rawValue = null;

            if (familyType == null || parameter == null)
                return false;

            if (!familyType.HasValue(parameter))
                return false;

            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.Integer:
                        var intValue = familyType.AsInteger(parameter);
                        if (!intValue.HasValue)
                            return false;
                        srId = intValue.Value;
                        rawValue = srId.ToString(CultureInfo.InvariantCulture);
                        return true;

                    case StorageType.String:
                        rawValue = familyType.AsString(parameter)?.Trim();
                        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out srId);

                    case StorageType.Double:
                        var doubleValue = familyType.AsDouble(parameter);
                        if (!doubleValue.HasValue)
                            return false;
                        rawValue = doubleValue.Value.ToString(CultureInfo.InvariantCulture);
                        if (Math.Abs(doubleValue.Value - Math.Round(doubleValue.Value)) > 1e-9)
                            return false;
                        srId = (int)Math.Round(doubleValue.Value);
                        return true;

                    default:
                        rawValue = familyType.AsValueString(parameter)?.Trim();
                        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out srId);
                }
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Failed to read SR_ID from FamilyManager");
                return false;
            }
        }

        static bool TryReadSrId(Parameter parameter, out int srId, out string rawValue)
        {
            srId = 0;
            rawValue = null;

            if (parameter == null || !parameter.HasValue)
                return false;

            switch (parameter.StorageType)
            {
                case StorageType.Integer:
                    srId = parameter.AsInteger();
                    rawValue = srId.ToString(CultureInfo.InvariantCulture);
                    return true;

                case StorageType.String:
                    rawValue = parameter.AsString()?.Trim();
                    return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out srId);

                case StorageType.Double:
                    var value = parameter.AsDouble();
                    rawValue = value.ToString(CultureInfo.InvariantCulture);
                    if (Math.Abs(value - Math.Round(value)) > 1e-9)
                        return false;
                    srId = (int)Math.Round(value);
                    return true;

                default:
                    rawValue = parameter.AsValueString()?.Trim();
                    return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out srId);
            }
        }

        static Family TryFindExistingFamily(Document doc, string filePath)
        {
            var familyName = TryGetFamilyNameFromRfa(doc.Application, filePath);
            if (string.IsNullOrWhiteSpace(familyName))
                return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Ищет семейство в проекте по имени файла RFA без открытия документа —
        /// OpenDocumentFile отравляет внутренний кеш Revit и ломает последующий LoadFamily.
        /// </summary>
        static Family TryFindExistingFamilyByFileName(Document doc, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            string familyName;
            try
            {
                familyName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(familyName))
                return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase));
        }

        static string TryGetFamilyNameFromRfa(Autodesk.Revit.ApplicationServices.Application app, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            try
            {
                var nameFromFileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                if (!string.IsNullOrWhiteSpace(nameFromFileName))
                    return nameFromFileName;
            }
            catch { }

            if (app == null)
                return null;

            Document familyDoc = null;
            try
            {
                var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
                familyDoc = app.OpenDocumentFile(modelPath, new OpenOptions());
                return !string.IsNullOrWhiteSpace(familyDoc.Title) ? familyDoc.Title : familyDoc.OwnerFamily?.Name;
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "Не удалось прочитать имя семейства из {Path}", filePath);
                return null;
            }
            finally
            {
                if (familyDoc != null)
                    familyDoc.Close(false);
            }
        }

        /// <summary>
        /// При повторной загрузке Revit вызывает OnFamilyFound — без этого LoadFamily возвращает false.
        /// </summary>
        sealed class AcceptExistingFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool FamilyAlreadyInProject { get; private set; }

            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                FamilyAlreadyInProject = true;
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(
                Family sharedFamily,
                bool familyInUse,
                out FamilySource source,
                out bool overwriteParameterValues)
            {
                FamilyAlreadyInProject = true;
                source = FamilySource.Project;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
