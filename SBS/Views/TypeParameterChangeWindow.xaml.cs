using Autodesk.Revit.DB;
using SmartRemont.ExportRooms.Models;
using SmartRemont.ExportRooms.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class TypeParameterChangeWindow : Window
    {
        readonly Document _doc;
        readonly ObservableCollection<TypeParameterRowVm> _parameters = new();
        ElementType _selectedType;
        bool _loadingSelection;

        public TypeParameterChangeWindow(Document doc)
        {
            InitializeComponent();
            WindowLayoutHelper.UseFullWorkAreaHeight(this);
            _doc = doc;
            ParametersDataGrid.ItemsSource = _parameters;
            Loaded += TypeParameterChangeWindow_Loaded;
        }

        void TypeParameterChangeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var categories = TypeParameterChangeService.GetCategories(_doc);
            CategoryComboBox.ItemsSource = categories;

            if (categories.Count == 0)
            {
                SetStatus("В модели не найдены категории с типами.", isError: true);
                return;
            }

            CategoryComboBox.SelectedIndex = 0;
        }

        void CategoryComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loadingSelection)
                return;

            _loadingSelection = true;
            try
            {
                FamilyComboBox.ItemsSource = null;
                TypeComboBox.ItemsSource = null;
                ClearParameters();

                if (CategoryComboBox.SelectedItem is not TypeCategoryOption category)
                    return;

                var families = TypeParameterChangeService.GetFamilies(_doc, category.CategoryId);
                FamilyComboBox.ItemsSource = families;
                FamilyComboBox.SelectedIndex = families.Count > 0 ? 0 : -1;

                if (families.Count == 0)
                    SetStatus("Для выбранной категории не найдены семейства.", isError: true);
            }
            finally
            {
                _loadingSelection = false;
            }

            FamilyComboBox_SelectionChanged(FamilyComboBox, null);
        }

        void FamilyComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loadingSelection)
                return;

            _loadingSelection = true;
            try
            {
                TypeComboBox.ItemsSource = null;
                ClearParameters();

                if (FamilyComboBox.SelectedItem is not TypeFamilyOption family)
                    return;

                var types = TypeParameterChangeService.GetTypes(_doc, family.CategoryId, family.Name);
                TypeComboBox.ItemsSource = types;
                TypeComboBox.SelectedIndex = types.Count > 0 ? 0 : -1;

                if (types.Count == 0)
                    SetStatus("Для выбранного семейства не найдены типы.", isError: true);
            }
            finally
            {
                _loadingSelection = false;
            }

            TypeComboBox_SelectionChanged(TypeComboBox, null);
        }

        void TypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loadingSelection)
                return;

            ClearParameters();

            if (TypeComboBox.SelectedItem is not TypeElementOption option)
                return;

            _selectedType = TypeParameterChangeService.GetElementType(_doc, option.TypeId);
            if (_selectedType == null)
            {
                SetStatus("Выбранный тип не найден в документе.", isError: true);
                return;
            }

            var rows = TypeParameterChangeService.GetParameters(_selectedType);
            if (rows.Count == 0)
            {
                SetStatus(
                    "У выбранного типа нет параметров «ID материала» и «ID типа материала».",
                    isError: true);
                SelectionHintText.Text = string.Empty;
                SaveButton.IsEnabled = false;
                return;
            }

            foreach (var row in rows)
            {
                row.PropertyChanged += ParameterRow_PropertyChanged;
                _parameters.Add(row);
            }

            var editableCount = _parameters.Count(p => p.CanEdit);
            SelectionHintText.Text = $"Найдено параметров: {_parameters.Count}, доступно для изменения: {editableCount}.";
            SetStatus($"Выбран тип: {option.FamilyName} / {option.Name}.", isError: false);

            NewTypeNameTextBox.Text = $"{option.Name} (копия)";

            UpdateSaveButtonState();
            UpdateDuplicateButtonState();
        }

        void ParameterRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TypeParameterRowVm.NewValue)
                || e.PropertyName == nameof(TypeParameterRowVm.IsEdited))
                UpdateSaveButtonState();
        }

        void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedType == null)
                return;

            try
            {
                var result = TypeParameterChangeService.SaveChanges(_doc, _selectedType, _parameters);
                SetStatus(result.Message, result.FailedCount > 0);
                UpdateSaveButtonState();
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Не удалось изменить параметры типа");
                SetStatus($"Ошибка сохранения: {ex.Message}", isError: true);
            }
        }

        void CloseButton_Click(object sender, RoutedEventArgs e) =>
            Close();

        void DuplicateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedType == null)
                return;

            var newName = NewTypeNameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(newName))
            {
                SetStatus("Введите название нового типа.", isError: true);
                return;
            }

            try
            {
                var result = TypeParameterChangeService.DuplicateWithParameters(
                    _doc, _selectedType, newName, _parameters);

                SetStatus(result.Message, result.FailedCount > 0 && result.ChangedCount == 0);
            }
            catch (Exception ex)
            {
                ExportRoomsApplication._logger?.Error(ex, "Не удалось дублировать тип");
                SetStatus($"Ошибка: {ex.Message}", isError: true);
            }
        }

        void NewTypeNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
            UpdateDuplicateButtonState();

        void ClearParameters()
        {
            foreach (var row in _parameters)
                row.PropertyChanged -= ParameterRow_PropertyChanged;

            _parameters.Clear();
            _selectedType = null;
            SelectionHintText.Text = string.Empty;
            SaveButton.IsEnabled = false;
            DuplicateButton.IsEnabled = false;
            SetStatus("Выберите тип для просмотра параметров.", isError: false);
        }

        void UpdateSaveButtonState() =>
            SaveButton.IsEnabled = _selectedType != null && _parameters.Any(p => p.IsEdited);

        void UpdateDuplicateButtonState() =>
            DuplicateButton.IsEnabled = _selectedType != null
                && !string.IsNullOrWhiteSpace(NewTypeNameTextBox.Text);

        void SetStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusBanner.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFromString(isError ? "#FEF2F2" : "#F8FAFC");
            StatusBanner.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFromString(isError ? "#FECACA" : "#E2E8F0");
            StatusTextBlock.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFromString(isError ? "#991B1B" : "#475569");
        }
    }
}
