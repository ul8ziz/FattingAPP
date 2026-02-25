using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Ul8ziz.FittingApp.App.Services;
using Ul8ziz.FittingApp.App.Views;

namespace Ul8ziz.FittingApp.App.ViewModels
{
    /// <summary>ViewModel for Audiogram screen: hosts Graphs panel and wires it to session snapshot.</summary>
    public class AudiogramViewModel : INotifyPropertyChanged
    {
        private readonly GraphsPanelViewModel _graphsPanelViewModel;
        private readonly FittingSessionManager _sessionMgr;

        public AudiogramViewModel()
        {
            var mappingService = new GraphParameterMappingService();
            _graphsPanelViewModel = new GraphsPanelViewModel(new GraphService(mappingService), mappingService);
            _sessionMgr = FittingSessionManager.Instance;

            RefreshFromSessionCommand = new RelayCommand(_ => SetDataSourceFromSession(), _ => true);
        }

        public GraphsPanelViewModel GraphsPanelViewModel => _graphsPanelViewModel;
        public ICommand RefreshFromSessionCommand { get; }

        /// <summary>Pushes current session snapshot/memory/library to the graphs panel. Call on navigate or after device refresh. Safe when session or library is null.</summary>
        public void SetDataSourceFromSession()
        {
            try
            {
                var snapshot = _sessionMgr?.LeftSnapshot;
                var memoryIndex = _sessionMgr?.SelectedMemoryIndex ?? 0;
                var libraryKey = _sessionMgr?.Library?.LoadedLibraryName ?? _sessionMgr?.ParamFileName ?? string.Empty;
                _graphsPanelViewModel.SetDataSource(snapshot, memoryIndex, libraryKey ?? string.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Audiogram] SetDataSourceFromSession error: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
