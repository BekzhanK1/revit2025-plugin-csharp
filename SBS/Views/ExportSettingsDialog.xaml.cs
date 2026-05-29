using System.Windows;

namespace SmartRemont.ExportRooms.Views
{
    public partial class ExportSettingsDialog : Window
    {
        public ExportSettings Settings { get; private set; }

        public ExportSettingsDialog()
        {
            InitializeComponent();
            Settings = new ExportSettings();
        }

        private void ChkAll_Checked(object sender, RoutedEventArgs e)
        {
            SetAllParameterGroups(true);
        }

        private void ChkAll_Unchecked(object sender, RoutedEventArgs e)
        {
            // Не снимаем все автоматически при снятии "Все параметры"
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            chkGeometry.IsChecked = true;
            chkBoundingBox.IsChecked = true;
            chkAll.IsChecked = true;
            SetAllParameterGroups(true);
            chkSharedParams.IsChecked = true;
            chkProjectParams.IsChecked = true;
            chkTypeParams.IsChecked = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            chkGeometry.IsChecked = false;
            chkBoundingBox.IsChecked = false;
            chkAll.IsChecked = false;
            SetAllParameterGroups(false);
            chkSharedParams.IsChecked = false;
            chkProjectParams.IsChecked = false;
            chkTypeParams.IsChecked = false;
        }

        private void SetAllParameterGroups(bool value)
        {
            chkGeometryParams.IsChecked = value;
            chkMaterials.IsChecked = value;
            chkConstruction.IsChecked = value;
            chkIdentity.IsChecked = value;
            chkPhasing.IsChecked = value;
            chkStructural.IsChecked = value;
            chkAnalytical.IsChecked = value;
            chkElectrical.IsChecked = value;
            chkMechanical.IsChecked = value;
            chkPlumbing.IsChecked = value;
            chkGraphics.IsChecked = value;
            chkOther.IsChecked = value;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Settings.ExportGeometry = chkGeometry.IsChecked == true;
            Settings.ExportBoundingBox = chkBoundingBox.IsChecked == true;
            
            Settings.ExportAllParameters = chkAll.IsChecked == true;
            Settings.ExportGeometryParams = chkGeometryParams.IsChecked == true;
            Settings.ExportMaterials = chkMaterials.IsChecked == true;
            Settings.ExportConstruction = chkConstruction.IsChecked == true;
            Settings.ExportIdentity = chkIdentity.IsChecked == true;
            Settings.ExportPhasing = chkPhasing.IsChecked == true;
            Settings.ExportStructural = chkStructural.IsChecked == true;
            Settings.ExportAnalytical = chkAnalytical.IsChecked == true;
            Settings.ExportElectrical = chkElectrical.IsChecked == true;
            Settings.ExportMechanical = chkMechanical.IsChecked == true;
            Settings.ExportPlumbing = chkPlumbing.IsChecked == true;
            Settings.ExportGraphics = chkGraphics.IsChecked == true;
            Settings.ExportOther = chkOther.IsChecked == true;
            
            Settings.ExportSharedParams = chkSharedParams.IsChecked == true;
            Settings.ExportProjectParams = chkProjectParams.IsChecked == true;
            Settings.ExportTypeParams = chkTypeParams.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class ExportSettings
    {
        public bool ExportGeometry { get; set; } = true;
        public bool ExportBoundingBox { get; set; } = true;
        
        public bool ExportAllParameters { get; set; } = true;
        public bool ExportGeometryParams { get; set; } = true;
        public bool ExportMaterials { get; set; } = true;
        public bool ExportConstruction { get; set; } = true;
        public bool ExportIdentity { get; set; } = true;
        public bool ExportPhasing { get; set; } = true;
        public bool ExportStructural { get; set; } = true;
        public bool ExportAnalytical { get; set; } = false;
        public bool ExportElectrical { get; set; } = false;
        public bool ExportMechanical { get; set; } = false;
        public bool ExportPlumbing { get; set; } = false;
        public bool ExportGraphics { get; set; } = false;
        public bool ExportOther { get; set; } = true;
        
        public bool ExportSharedParams { get; set; } = true;
        public bool ExportProjectParams { get; set; } = true;
        public bool ExportTypeParams { get; set; } = true;

        public bool ShouldExportParameter(string groupName)
        {
            if (ExportAllParameters)
                return true;

            if (string.IsNullOrEmpty(groupName))
                return ExportOther;

            // Проверяем по группе параметра
            if (groupName.Contains("PG_GEOMETRY") || groupName.Contains("Размеры") || groupName.Contains("Геометрия"))
                return ExportGeometryParams;
            if (groupName.Contains("PG_MATERIALS") || groupName.Contains("Материалы"))
                return ExportMaterials;
            if (groupName.Contains("PG_CONSTRUCTION") || groupName.Contains("Конструкция"))
                return ExportConstruction;
            if (groupName.Contains("PG_IDENTITY") || groupName.Contains("Идентификация") || groupName.Contains("Данные"))
                return ExportIdentity;
            if (groupName.Contains("PG_PHASING") || groupName.Contains("Стадии"))
                return ExportPhasing;
            if (groupName.Contains("PG_STRUCTURAL") || groupName.Contains("Конструктивн"))
                return ExportStructural;
            if (groupName.Contains("PG_ANALYTICAL") || groupName.Contains("Аналитич"))
                return ExportAnalytical;
            if (groupName.Contains("PG_ELECTRICAL") || groupName.Contains("Электрич"))
                return ExportElectrical;
            if (groupName.Contains("PG_MECHANICAL") || groupName.Contains("Механич") || groupName.Contains("ОВиК"))
                return ExportMechanical;
            if (groupName.Contains("PG_PLUMBING") || groupName.Contains("Сантехник"))
                return ExportPlumbing;
            if (groupName.Contains("PG_GRAPHICS") || groupName.Contains("Графика"))
                return ExportGraphics;

            return ExportOther;
        }
    }
}

