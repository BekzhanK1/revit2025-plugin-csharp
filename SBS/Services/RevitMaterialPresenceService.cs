using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public sealed class MaterialPresenceInfo
    {
        public bool IsInProject { get; init; }
        public string Label { get; init; }

        /// <summary>
        /// Значение SR_ID, фактически прочитанное в Revit. Индекс строится по ключу SR_ID,
        /// поэтому при найденном совпадении оно всегда равно запрошенному material_id —
        /// поле сделано отдельным для явного отображения в UI (диагностика/доверие к данным).
        /// </summary>
        public int? SrId { get; init; }
    }

    /// <summary>
    /// Проверяет наличие материала в проекте Revit по параметру SR_ID (= material_id).
    /// RFA — FamilySymbol; surface — типы элементов и Material.
    /// </summary>
    public static class RevitMaterialPresenceService
    {
        const string SrIdParameterName = "SR_ID";

        public static Dictionary<int, MaterialPresenceInfo> CheckMaterials(
            Document doc,
            IEnumerable<int> materialIds)
        {
            var ids = (materialIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            var index = BuildSrIdIndex(doc);
            var result = new Dictionary<int, MaterialPresenceInfo>();

            foreach (var materialId in ids)
            {
                if (index.TryGetValue(materialId, out var label))
                {
                    result[materialId] = new MaterialPresenceInfo
                    {
                        IsInProject = true,
                        Label = label,
                        SrId = materialId
                    };
                }
                else
                {
                    result[materialId] = new MaterialPresenceInfo
                    {
                        IsInProject = false,
                        Label = null,
                        SrId = null
                    };
                }
            }

            return result;
        }

        public static MaterialPresenceInfo CheckMaterial(Document doc, int materialId) =>
            CheckMaterials(doc, new[] { materialId }).TryGetValue(materialId, out var info)
                ? info
                : new MaterialPresenceInfo { IsInProject = false };

        public static MaterialPresenceInfo LookupInIndex(IReadOnlyDictionary<int, string> index, int materialId)
        {
            if (index != null && index.TryGetValue(materialId, out var label))
            {
                return new MaterialPresenceInfo
                {
                    IsInProject = true,
                    Label = label,
                    SrId = materialId
                };
            }

            return new MaterialPresenceInfo { IsInProject = false };
        }

        public static void AddToIndex(IDictionary<int, string> index, int materialId, string label)
        {
            if (index == null || materialId <= 0 || string.IsNullOrWhiteSpace(label))
                return;

            if (!index.ContainsKey(materialId))
                index[materialId] = label.Trim();
        }

        /// <summary>
        /// Читает SR_ID с экземпляра, иначе с типа. Возвращает false, если параметра нет / не число.
        /// </summary>
        public static bool TryGetSrId(Element element, Document doc, out int srId, out string sourceLevel)
        {
            srId = 0;
            sourceLevel = null;

            if (element == null)
                return false;

            if (TryReadSrIdFromElement(element, out srId, out _))
            {
                sourceLevel = "экземпляр";
                return true;
            }

            if (doc == null)
                return false;

            var type = doc.GetElement(element.GetTypeId()) as ElementType;
            if (type == null)
                return false;

            if (!TryReadSrIdFromElement(type, out srId, out _))
                return false;

            sourceLevel = "тип";
            return true;
        }

        public static Dictionary<int, string> BuildSrIdIndex(Document doc)
        {
            var index = new Dictionary<int, string>();
            if (doc == null)
                return index;

            foreach (FamilySymbol symbol in new FilteredElementCollector(doc)
                         .OfClass(typeof(FamilySymbol))
                         .Cast<FamilySymbol>())
            {
                TryAddToIndex(index, symbol, FormatFamilySymbolLabel(symbol));
            }

            foreach (ElementType elementType in new FilteredElementCollector(doc)
                         .WhereElementIsElementType()
                         .Cast<ElementType>())
            {
                TryAddToIndex(index, elementType, FormatElementTypeLabel(elementType));
            }

            foreach (Material material in new FilteredElementCollector(doc)
                         .OfClass(typeof(Material))
                         .Cast<Material>())
            {
                TryAddToIndex(index, material, material.Name);
            }

            return index;
        }

        static void TryAddToIndex(Dictionary<int, string> index, Element element, string label)
        {
            if (!TryReadSrIdFromElement(element, out var srId, out _))
                return;

            if (!index.ContainsKey(srId))
                index[srId] = label;
        }

        static string FormatFamilySymbolLabel(FamilySymbol symbol)
        {
            if (symbol == null)
                return null;

            var familyName = symbol.FamilyName;
            var typeName = symbol.Name;
            return string.IsNullOrWhiteSpace(familyName)
                ? typeName
                : $"{familyName}: {typeName}";
        }

        static string FormatElementTypeLabel(ElementType elementType)
        {
            if (elementType == null)
                return null;

            var familyName = elementType.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM)?.AsString();
            if (string.IsNullOrWhiteSpace(familyName))
                familyName = elementType.Category?.Name;

            var typeName = elementType.Name;
            return string.IsNullOrWhiteSpace(familyName)
                ? typeName
                : $"{familyName}: {typeName}";
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
                ExportRoomsApplication._logger?.Debug(ex, "Failed to read {Param} in project scan", SrIdParameterName);
                return false;
            }
        }
    }
}
