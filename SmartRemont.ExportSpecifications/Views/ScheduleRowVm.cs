using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartRemont.ExportSpecifications.Views
{
    public class ScheduleRowVm : INotifyPropertyChanged
    {
        bool _isSelected;

        public string ScheduleName { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
