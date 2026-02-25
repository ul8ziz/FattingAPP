using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>Section within a category; contains a list of parameters.</summary>
    public class SettingSection : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private readonly ObservableCollection<SettingItem> _items = new ObservableCollection<SettingItem>();

        public string Id { get => _id; set { _id = value ?? string.Empty; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value ?? string.Empty; OnPropertyChanged(); } }
        public ObservableCollection<SettingItem> Items => _items;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
