using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ul8ziz.FittingApp.App.Models
{
    /// <summary>Descriptor for a tab (category). Groups are loaded lazily when tab is selected.</summary>
    public class TabDescriptor : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _title = string.Empty;
        private int _groupsCount;
        private bool _isLoaded;

        public string Id { get => _id; set { _id = value ?? ""; OnPropertyChanged(); } }
        public string Title { get => _title; set { _title = value ?? ""; OnPropertyChanged(); } }
        public int GroupsCount { get => _groupsCount; set { _groupsCount = value; OnPropertyChanged(); } }
        public bool IsLoaded { get => _isLoaded; set { _isLoaded = value; OnPropertyChanged(); } }

        /// <summary>Group descriptors for this tab; populated when tab is loaded.</summary>
        public ObservableCollection<GroupDescriptor> Groups { get; } = new ObservableCollection<GroupDescriptor>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
