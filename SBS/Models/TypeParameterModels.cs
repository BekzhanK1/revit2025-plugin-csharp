using Autodesk.Revit.DB;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartRemont.ExportRooms.Models
{
    public class TypeCategoryOption
    {
        public ElementId CategoryId { get; set; }
        public string Name { get; set; }
    }

    public class TypeFamilyOption
    {
        public ElementId CategoryId { get; set; }
        public string Name { get; set; }
    }

    public class TypeElementOption
    {
        public ElementId TypeId { get; set; }
        public string FamilyName { get; set; }
        public string Name { get; set; }
    }

    public class TypeParameterRowVm : INotifyPropertyChanged
    {
        string _currentValue;
        string _newValue;

        public string Name { get; set; }
        public string StorageTypeName { get; set; }
        public string EditNote { get; set; }
        public bool CanEdit { get; set; }
        public Parameter Parameter { get; set; }

        public string CurrentValue
        {
            get => _currentValue;
            set
            {
                if (_currentValue == value)
                    return;

                _currentValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEdited));
            }
        }

        public string NewValue
        {
            get => _newValue;
            set
            {
                if (_newValue == value)
                    return;

                _newValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEdited));
            }
        }

        public bool IsEdited => CanEdit && (CurrentValue ?? string.Empty) != (NewValue ?? string.Empty);

        public event PropertyChangedEventHandler PropertyChanged;

        public void AcceptValue(string value)
        {
            _currentValue = value;
            _newValue = value;
            OnPropertyChanged(nameof(CurrentValue));
            OnPropertyChanged(nameof(NewValue));
            OnPropertyChanged(nameof(IsEdited));
        }

        void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class TypeParameterSaveResult
    {
        public int ChangedCount { get; set; }
        public int FailedCount { get; set; }
        public string Message { get; set; }
    }
}
