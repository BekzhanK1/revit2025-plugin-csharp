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

        /// <summary>Revit считает типы неиспользуемыми (как в «Удалить неиспользуемое»), удаление ничего в модели не тронет.</summary>
        public bool CanDelete { get; init; }

        public string UsageDisplay { get; init; }
    }

    public sealed class ProjectExtraDeleteResult
    {
        public int DeletedCount { get; set; }
        public List<string> Skipped { get; } = new();
    }

    /// <summary>
    /// Элементы проекта с SR_ID не из ТК: найти и удалить.
    /// Удаляется только то, что Revit считает неиспользуемым, — размещённые элементы модели не удаляются никогда.
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

                // У типа материалы проверяются уже после удаления типа: пока тип есть, его материал «используется».
                var canDelete = unused != null
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
                    CanDelete = canDelete,
                    UsageDisplay = BuildUsageDisplay(unused != null, canDelete, instanceCount, typeList.Count > 0),
                });
            }

            ExportRoomsApplication._logger?.Information(
                "Project extra SR_ID: groups={Groups}, deletable={Deletable}, placed={Placed}, unused_check={UnusedCheck}",
                result.Count,
                result.Count(i => i.CanDelete),
                result.Count(i => i.InstanceCount > 0),
                unused != null);

            return result
                .OrderByDescending(i => i.CanDelete)
                .ThenBy(i => i.SrId)
                .ToList();
        }

        /// <summary>
        /// Удаляет выбранные SR_ID одной отменяемой операцией. Перед удалением заново спрашивает у Revit,
        /// что не используется: всё, что используется, пропускается с причиной.
        /// </summary>
        public static ProjectExtraDeleteResult Delete(Document doc, IReadOnlyCollection<ProjectExtraSrIdItem> items)
        {
            var result = new ProjectExtraDeleteResult();
            if (doc == null || items == null || items.Count == 0)
                return result;

            var reasons = new Dictionary<ElementId, string>();

            using var group = new TransactionGroup(doc, "Smart Remont: удалить материалы не из ТК");
            group.Start();
            try
            {
                // 1. Типы и семейства. Если удаляются все типы семейства — удаляем семейство целиком:
                // последний тип семейства Revit отдельно не удаляет.
                var unused = TryGetUnused(doc) ?? new HashSet<ElementId>();
                var typeIds = new HashSet<ElementId>(items.SelectMany(i => i.TypeIds).Where(id => doc.GetElement(id) != null));
                var deleteIds = new List<ElementId>();
                var handledFamilies = new HashSet<ElementId>();

                foreach (var id in typeIds)
                {
                    if (!unused.Contains(id))
                    {
                        reasons[id] = "используется в проекте";
                        continue;
                    }

                    if (doc.GetElement(id) is FamilySymbol symbol && symbol.Family is Family family)
                    {
                        if (handledFamilies.Contains(family.Id))
                            continue;

                        var familySymbolIds = family.GetFamilySymbolIds();
                        if (familySymbolIds.All(sid => typeIds.Contains(sid) && unused.Contains(sid)))
                        {
                            handledFamilies.Add(family.Id);
                            deleteIds.Add(family.Id);
                            continue;
                        }
                    }

                    deleteIds.Add(id);
                }

                if (deleteIds.Count > 0)
                    RunDelete(doc, "Smart Remont: удалить типы не из ТК", deleteIds, reasons);

                // 2. Материалы: после удаления типов их слои больше не держат материал.
                var materialIds = items.SelectMany(i => i.MaterialIds).Where(id => doc.GetElement(id) != null).ToList();
                if (materialIds.Count > 0)
                {
                    unused = TryGetUnused(doc) ?? new HashSet<ElementId>();
                    var deleteMaterialIds = new List<ElementId>();
                    foreach (var id in materialIds)
                    {
                        if (unused.Contains(id))
                            deleteMaterialIds.Add(id);
                        else
                            reasons[id] = "материал используется в проекте";
                    }

                    if (deleteMaterialIds.Count > 0)
                        RunDelete(doc, "Smart Remont: удалить материалы не из ТК", deleteMaterialIds, reasons);
                }

                group.Assimilate();
            }
            catch
            {
                if (group.GetStatus() == TransactionStatus.Started)
                    group.RollBack();
                throw;
            }

            foreach (var item in items)
            {
                var left = item.TypeIds.Concat(item.MaterialIds).Where(id => doc.GetElement(id) != null).ToList();
                if (left.Count == 0)
                {
                    result.DeletedCount++;
                    continue;
                }

                var reason = left.Select(id => reasons.TryGetValue(id, out var r) ? r : null).FirstOrDefault(r => r != null)
                             ?? "не удалён";
                result.Skipped.Add($"{item.SrId} · {item.Label}: {reason}");
            }

            ExportRoomsApplication._logger?.Information(
                "Project extra SR_ID delete: requested={Requested}, deleted={Deleted}, skipped={Skipped}",
                items.Count,
                result.DeletedCount,
                result.Skipped.Count);

            return result;
        }

        static void RunDelete(Document doc, string name, IEnumerable<ElementId> ids, IDictionary<ElementId, string> reasons)
        {
            using var tx = new Transaction(doc, name);
            var options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new SkipWarnings());
            tx.SetFailureHandlingOptions(options);
            tx.Start();

            foreach (var id in ids)
            {
                if (doc.GetElement(id) == null)
                    continue;

                if (!DocumentValidation.CanDeleteElement(doc, id))
                {
                    reasons[id] = "Revit не даёт удалить (последний тип семейства или системный)";
                    continue;
                }

                // Каждый элемент в своей подтранзакции: ошибка одного не откатывает остальные.
                using var sub = new SubTransaction(doc);
                sub.Start();
                try
                {
                    doc.Delete(id);
                    sub.Commit();
                }
                catch (Exception ex)
                {
                    if (sub.GetStatus() == TransactionStatus.Started)
                        sub.RollBack();
                    reasons[id] = ex.Message;
                    ExportRoomsApplication._logger?.Warning(ex, "Project extra SR_ID delete failed: element_id={ElementId}", id.Value);
                }
            }

            tx.Commit();
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

        static string BuildUsageDisplay(bool checkedUnused, bool canDelete, int instanceCount, bool hasTypes)
        {
            if (instanceCount > 0)
                return $"Размещено в модели: {instanceCount} шт.";
            if (!checkedUnused)
                return "Не удалось проверить, используется ли";
            if (canDelete)
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

        /// <summary>Предупреждения при удалении («элементы удалены» и т.п.) не показываем — их нечего решать.</summary>
        sealed class SkipWarnings : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                foreach (var failure in failuresAccessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                        failuresAccessor.DeleteWarning(failure);
                }

                return FailureProcessingResult.Continue;
            }
        }
    }
}
