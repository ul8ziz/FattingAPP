using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ul8ziz.FittingApp.App.ViewModels;

namespace Ul8ziz.FittingApp.App.Models
{
    /// <summary>Descriptor for a parameter group (section). Rows are loaded lazily when group expander opens.</summary>
    public class GroupDescriptor : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private int _paramsCount;
        private bool _isLoaded;
        private ObservableCollection<SettingItemViewModel> _rows = new ObservableCollection<SettingItemViewModel>();

        public string Id { get => _id; set { _id = value ?? ""; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value ?? ""; OnPropertyChanged(); } }
        public int ParamsCount { get => _paramsCount; set { _paramsCount = value; OnPropertyChanged(); } }
        public bool IsLoaded { get => _isLoaded; set { _isLoaded = value; OnPropertyChanged(); } }

        /// <summary>Row VMs for this group; populated when group is expanded. Assign once on UI thread.</summary>
        public ObservableCollection<SettingItemViewModel> Rows { get => _rows; set { _rows = value ?? new ObservableCollection<SettingItemViewModel>(); OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
