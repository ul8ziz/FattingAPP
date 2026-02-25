using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ul8ziz.FittingApp.Device.DeviceCommunication.Models
{
    /// <summary>Top-level category (e.g. QuickFit, Fine Tuning); contains sections.</summary>
    public class SettingCategory : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private readonly ObservableCollection<SettingSection> _sections = new ObservableCollection<SettingSection>();

        public string Id { get => _id; set { _id = value ?? string.Empty; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value ?? string.Empty; OnPropertyChanged(); } }
        public ObservableCollection<SettingSection> Sections => _sections;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
