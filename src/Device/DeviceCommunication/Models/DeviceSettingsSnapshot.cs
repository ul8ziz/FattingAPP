using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>In-memory snapshot of all device settings per side. Category -> Section -> Parameter list.</summary>
    public class DeviceSettingsSnapshot : INotifyPropertyChanged
    {
        private DeviceSide _side;
        private readonly ObservableCollection<SettingCategory> _categories = new ObservableCollection<SettingCategory>();

        public DeviceSide Side { get => _side; set { _side = value; OnPropertyChanged(); } }
        public ObservableCollection<SettingCategory> Categories => _categories;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
