using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    /// <summary>SR_ID в проекте, которого нет в ТК заявки: типы, семейства и материалы с этим SR_ID.</summary>
    public sealed class ProjectExtraSrIdItem
    {
        public int SrId { get; init; }
        public string Label { get; init; }
        public string KindDisplay { get; init; }

        /// <summary>FamilySymbol и типы системных семейств (стены, перекрытия…).</summary>
        public IReadOnlyList<ElementId> TypeIds { get; init; } = Array.Empty<ElementId>();

        public IReadOnlyList<ElementId> MaterialIds { get; init; } = Array.Empty<ElementId>();

        /// <summary>Сколько элементов модели стоит на типах с этим SR_ID.</summary>
        public int InstanceCount { get; init; }

        /// <summary>Revit считает элементы неиспользуемыми (как в «Удалить неиспользуемое»).</summary>
        public bool IsUnused { get; init; }

        public string UsageDisplay { get; init; }
    }

    /// <summary>
    /// Элементы проекта с SR_ID не из ТК. Только поиск: плагин их не удаляет.
    /// </summary>
    public static class RevitProjectExtraMaterialsService
    {
        public static List<ProjectExtraSrIdItem> Find(Document doc, ISet<int> tkMaterialIds)
        {
            var result = new List<ProjectExtraSrIdItem>();
            if (doc == null)
                return result;

            var tk = tkMaterialIds ?? new HashSet<int>();
            var types = new Dictionary<int, List<ElementType>>();
            var materials = new Dictionary<int, List<Material>>();

            // FamilySymbol тоже ElementType — отдельный обход семейств не нужен.
            foreach (var type in new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>())
            {
                if (RevitMaterialPresenceService.TryReadSrId(type, out var srId) && srId > 0 && !tk.Contains(srId))
                    AddTo(types, srId, type);
            }

            foreach (var material in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
            {
                if (RevitMaterialPresenceService.TryReadSrId(material, out var srId) && srId > 0 && !tk.Contains(srId))
                    AddTo(materials, srId, material);
            }

            if (types.Count == 0 && materials.Count == 0)
                return result;

            var instanceCounts = CountInstances(doc, types.Values.SelectMany(list => list).Select(t => t.Id));
            var unused = TryGetUnused(doc);

            foreach (var srId in types.Keys.Union(materials.Keys))
            {
                var typeList = types.TryGetValue(srId, out var tl) ? tl : new List<ElementType>();
                var materialList = materials.TryGetValue(srId, out var ml) ? ml : new List<Material>();
                var instanceCount = typeList.Sum(t => instanceCounts.TryGetValue(t.Id, out var n) ? n : 0);

                // Материал типа «используется», пока есть сам тип, поэтому при наличии типов смотрим на типы.
                var isUnused = unused != null
                                && instanceCount == 0
                                && (typeList.Count > 0
                                    ? typeList.All(t => unused.Contains(t.Id))
                                    : materialList.All(m => unused.Contains(m.Id)));

                result.Add(new ProjectExtraSrIdItem
                {
                    SrId = srId,
                    Label = RevitMaterialPresenceService.DescribeElement((Element)typeList.FirstOrDefault() ?? materialList.FirstOrDefault()) ?? "—",
                    KindDisplay = BuildKindDisplay(typeList, materialList),
                    TypeIds = typeList.Select(t => t.Id).ToList(),
                    MaterialIds = materialList.Select(m => m.Id).ToList(),
                    InstanceCount = instanceCount,
                    IsUnused = isUnused,
                    UsageDisplay = BuildUsageDisplay(unused != null, isUnused, instanceCount, typeList.Count > 0),
                });
            }

            ExportRoomsApplication._logger?.Information(
                "Project extra SR_ID: groups={Groups}, unused={Unused}, placed={Placed}, unused_check={UnusedCheck}",
                result.Count,
                result.Count(i => i.IsUnused),
                result.Count(i => i.InstanceCount > 0),
                unused != null);

            return result
                .OrderBy(i => i.IsUnused)
                .ThenBy(i => i.SrId)
                .ToList();
        }

        static HashSet<ElementId> TryGetUnused(Document doc)
        {
            try
            {
                // Пустой набор категорий — все неиспользуемые элементы, как в окне «Удалить неиспользуемое».
                return new HashSet<ElementId>(doc.GetUnusedElements(new HashSet<ElementId>()));
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Warning(ex, "GetUnusedElements failed");
                return null;
            }
        }

        static Dictionary<ElementId, int> CountInstances(Document doc, IEnumerable<ElementId> typeIds)
        {
            var counts = new Dictionary<ElementId, int>();
            var wanted = new HashSet<ElementId>(typeIds);
            if (wanted.Count == 0)
                return counts;

            foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                var typeId = element.GetTypeId();
                if (typeId == ElementId.InvalidElementId || !wanted.Contains(typeId))
                    continue;

                counts[typeId] = counts.TryGetValue(typeId, out var n) ? n + 1 : 1;
            }

            return counts;
        }

        static string BuildKindDisplay(List<ElementType> types, List<Material> materials)
        {
            string kind;
            if (types.Any(t => t is FamilySymbol))
                kind = "Семейство";
            else if (types.Count > 0)
                kind = string.IsNullOrWhiteSpace(types[0].Category?.Name) ? "Тип" : $"Тип: {types[0].Category.Name}";
            else
                kind = "Материал";

            return types.Count > 0 && materials.Count > 0 ? $"{kind} + материал" : kind;
        }

        static string BuildUsageDisplay(bool checkedUnused, bool isUnused, int instanceCount, bool hasTypes)
        {
            if (instanceCount > 0)
                return $"Размещено в модели: {instanceCount} шт.";
            if (!checkedUnused)
                return "Не удалось проверить, используется ли";
            if (isUnused)
                return "Не используется";
            return hasTypes
                ? "Используется в проекте"
                : "Используется в проекте (например, в слоях типа)";
        }

        static void AddTo<T>(Dictionary<int, List<T>> map, int key, T value)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<T>();
            list.Add(value);
        }
    }
}
