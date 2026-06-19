using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SmartRemont.ExportRooms.Services
{
    public static class TypeParameterChangeService
    {
        public static List<TypeCategoryOption> GetCategories(Document doc) =>
            GetModelTypes(doc)
                .GroupBy(t => t.Category.Id.Value)
                .Select(g =>
                {
                    var category = g.First().Category;
                    return new TypeCategoryOption
                    {
                        CategoryId = category.Id,
                        Name = category.Name
                    };
                })
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        public static List<TypeFamilyOption> GetFamilies(Document doc, ElementId categoryId) =>
            GetTypesByCategory(doc, categoryId)
                .Select(t => GetFamilyName(t))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => new TypeFamilyOption
                {
                    CategoryId = categoryId,
                    Name = n
                })
                .ToList();

        public static List<TypeElementOption> GetTypes(Document doc, ElementId categoryId, string familyName) =>
            GetTypesByCategory(doc, categoryId)
                .Where(t => string.Equals(GetFamilyName(t), familyName, StringComparison.CurrentCultureIgnoreCase))
                .Select(t => new TypeElementOption
                {
                    TypeId = t.Id,
                    FamilyName = GetFamilyName(t),
                    Name = GetTypeName(t)
                })
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        public static ElementType GetElementType(Document doc, ElementId typeId) =>
            doc?.GetElement(typeId) as ElementType;

        public static List<TypeParameterRowVm> GetParameters(ElementType type)
        {
            if (type == null)
                return new List<TypeParameterRowVm>();

            return type.Parameters
                .Cast<Parameter>()
                .Where(p => p?.Definition != null)
                .OrderBy(p => p.Definition.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p =>
                {
                    var canEdit = CanEdit(p);
                    var value = FormatParameterValue(p);
                    return new TypeParameterRowVm
                    {
                        Name = p.Definition.Name,
                        StorageTypeName = GetStorageTypeName(p.StorageType),
                        CurrentValue = value,
                        NewValue = value,
                        CanEdit = canEdit,
                        EditNote = canEdit ? string.Empty : GetReadOnlyReason(p),
                        Parameter = p
                    };
                })
                .ToList();
        }

        public static TypeParameterSaveResult SaveChanges(Document doc, ElementType type, IEnumerable<TypeParameterRowVm> rows)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var changedRows = (rows ?? Enumerable.Empty<TypeParameterRowVm>())
                .Where(r => r?.IsEdited == true)
                .ToList();

            if (changedRows.Count == 0)
            {
                return new TypeParameterSaveResult
                {
                    Message = "Нет изменений для сохранения."
                };
            }

            var changed = 0;
            var failures = new List<string>();

            using (var tx = new Transaction(doc, "Smart Remont: изменение параметров типа"))
            {
                tx.Start();

                foreach (var row in changedRows)
                {
                    try
                    {
                        SetParameterValue(row.Parameter, row.NewValue);
                        changed++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{row.Name}: {ex.Message}");
                    }
                }

                if (changed > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            foreach (var row in changedRows)
            {
                if (row.Parameter != null && !failures.Any(f => f.StartsWith(row.Name + ":", StringComparison.Ordinal)))
                    row.AcceptValue(FormatParameterValue(row.Parameter));
            }

            var message = changed > 0
                ? $"Сохранено параметров: {changed}."
                : "Параметры не изменены.";

            if (failures.Count > 0)
                message += Environment.NewLine + "Не удалось изменить:" + Environment.NewLine + string.Join(Environment.NewLine, failures);

            return new TypeParameterSaveResult
            {
                ChangedCount = changed,
                FailedCount = failures.Count,
                Message = message
            };
        }

        static List<ElementType> GetModelTypes(Document doc)
        {
            if (doc == null)
                return new List<ElementType>();

            return new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .OfType<ElementType>()
                .Where(t => t?.Category != null && t.Category.CategoryType == CategoryType.Model)
                .ToList();
        }

        static IEnumerable<ElementType> GetTypesByCategory(Document doc, ElementId categoryId) =>
            GetModelTypes(doc)
                .Where(t => SameElementId(t.Category?.Id, categoryId));

        static bool SameElementId(ElementId left, ElementId right) =>
            left != null && right != null && left.Value == right.Value;

        static string GetFamilyName(ElementType type)
        {
            if (type == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(type.FamilyName))
                return type.FamilyName.Trim();

            return type.Category?.Name?.Trim() ?? "Без семейства";
        }

        static string GetTypeName(ElementType type) =>
            string.IsNullOrWhiteSpace(type?.Name)
                ? $"Type {type?.Id.Value}"
                : type.Name.Trim();

        static bool CanEdit(Parameter parameter) =>
            parameter != null
            && parameter.Definition != null
            && !parameter.IsReadOnly
            && parameter.StorageType != StorageType.None;

        static string GetReadOnlyReason(Parameter parameter)
        {
            if (parameter == null || parameter.Definition == null)
                return "Недоступен";
            if (parameter.IsReadOnly)
                return "Только чтение";
            if (parameter.StorageType == StorageType.None)
                return "Нет значения";

            return "Недоступен для записи";
        }

        static string GetStorageTypeName(StorageType storageType)
        {
            return storageType switch
            {
                StorageType.String => "Текст",
                StorageType.Integer => "Целое",
                StorageType.Double => "Число / единицы",
                StorageType.ElementId => "ElementId",
                _ => storageType.ToString()
            };
        }

        static string FormatParameterValue(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue)
                return string.Empty;

            return parameter.StorageType switch
            {
                StorageType.String => parameter.AsString() ?? string.Empty,
                StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
                StorageType.Double => parameter.AsValueString()
                                      ?? parameter.AsDouble().ToString(CultureInfo.InvariantCulture),
                StorageType.ElementId => parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture),
                _ => parameter.AsValueString() ?? string.Empty
            };
        }

        static void SetParameterValue(Parameter parameter, string value)
        {
            if (!CanEdit(parameter))
                throw new InvalidOperationException("Параметр недоступен для записи.");

            value ??= string.Empty;

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    parameter.Set(value);
                    break;

                case StorageType.Integer:
                    parameter.Set(ParseInteger(value));
                    break;

                case StorageType.Double:
                    if (!parameter.SetValueString(value))
                    {
                        var number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                        parameter.Set(number);
                    }
                    break;

                case StorageType.ElementId:
                    parameter.Set(new ElementId(ParseElementId(value)));
                    break;

                default:
                    throw new InvalidOperationException($"Тип значения {parameter.StorageType} не поддерживается.");
            }
        }

        static int ParseInteger(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                return number;

            if (bool.TryParse(value, out var boolean))
                return boolean ? 1 : 0;

            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized is "да" or "yes" or "y")
                return 1;
            if (normalized is "нет" or "no" or "n")
                return 0;

            throw new FormatException("Введите целое число, 0/1 или да/нет.");
        }

        static long ParseElementId(string value)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                return id;

            throw new FormatException("Введите числовой ElementId.");
        }
    }
}
